using Godot;

namespace Librarian.Mechanics;

/// <summary>Original C# adapter over the fixed game's installed Spine extension. Contains no runtime source.</summary>
internal sealed class LibrarianSpineNativeBinding : ILibrarianSpineBinding
{
    private static readonly LibrarianSpineSocket[] SocketKinds = [LibrarianSpineSocket.ScreenLeft, LibrarianSpineSocket.ScreenRight, LibrarianSpineSocket.Book];
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private readonly Node _sprite;                 // Borrowed Node; never Dispose it.
    private readonly GodotObject _state;           // Engine-owned RefCounted; never Dispose shared objects.
    private readonly GodotObject _skeleton;        // Engine-owned RefCounted; never Dispose shared objects.
    private readonly Dictionary<string, GodotObject> _slots = new(StringComparer.Ordinal); // Our find_slot wrappers.
    private readonly List<(StringName Signal, Callable Callable)> _connections = [];
    private readonly Dictionary<ulong, GodotObject> _callbackRetirement = [];
    private readonly Dictionary<LibrarianSpineSocket, LibrarianSpineSocketSample> _sockets = [];
    private LibrarianSpinePoseDriver? _driver;
    private bool _disposed;
    private bool _connected;
    private bool _disposeQueued;
    private readonly Action<Exception>? _fault;
    private readonly Action? _released;

    internal LibrarianSpineNativeBinding(Node sprite, LibrarianSpineBank bank, Action<Exception>? fault = null, Action? released = null)
    {
        _sprite = sprite;
        _fault = fault;
        _released = released;
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
            throw new InvalidOperationException("Create Spine bindings on Godot's actual main thread.");
        CheckClass(sprite, "SpineSprite", "get_animation_state", "get_skeleton", "get_skeleton_data_res", "get_global_bone_transform");
        _state = Object(sprite, "get_animation_state") ?? throw new InvalidOperationException("Spine state is not ready.");
        _skeleton = Object(sprite, "get_skeleton") ?? throw new InvalidOperationException("Spine skeleton is not ready.");
        CheckClass(_state, "SpineAnimationState", "set_animation", "get_current", "clear_track", "update", "apply");
        CheckClass(_skeleton, "SpineSkeleton", "find_slot", "find_bone");
        try
        {
            foreach (string name in bank.Slots)
            {
                var slot = Object(_skeleton, "find_slot", name) ?? throw new InvalidDataException("Missing Spine selection slot: " + name);
                try { CheckClass(slot, "SpineSlot", "get_attachment", "set_attachment"); }
                catch { ReleaseOwned(slot); throw; }
                _slots.Add(name, slot);
            }
            ValidateAnimations(bank);
            foreach (var socket in SocketKinds)
            {
                // get_global_bone_transform silently falls back to the Sprite for an absent bone; reject that case.
                var bone = Object(_skeleton, "find_bone", LibrarianSpineMotionProfiles.BoneName(socket))
                    ?? throw new InvalidDataException("Missing authored Spine socket: " + socket);
                ReleaseOwned(bone);
            }
        }
        catch { foreach (var slot in _slots.Values) ReleaseOwned(slot); _slots.Clear(); throw; }
    }
    private void ValidateAnimations(LibrarianSpineBank bank)
    {
        // Resource is borrowed from the Sprite; the array getter creates fresh wrappers owned by this call.
        var resource = Object(_sprite, "get_skeleton_data_res") ?? throw new InvalidDataException("Spine data resource is absent.");
        try
        {
            if (!resource.HasMethod("find_animation") || !resource.HasMethod("get_animations"))
                throw new InvalidDataException("Spine animation metadata binding is unavailable.");
            using var value = Call(resource, "get_animations");
            using var animations = value.AsGodotArray();
            var expected = bank.Names.ToHashSet(StringComparer.Ordinal);
            if (animations.Count != expected.Count)
                throw new InvalidDataException("Native Spine animation count differs from frozen receipt.");
            for (int i = 0; i < animations.Count; i++)
            {
                using var item = animations[i];
                var animation = item.AsGodotObject() ?? throw new InvalidDataException("Null native animation metadata.");
                try
                {
                    CheckClass(animation, "SpineAnimation", "get_duration", "get_name");
                    string name = String(animation, "get_name");
                    if (!expected.Remove(name)) throw new InvalidDataException("Unexpected/duplicate native animation: " + name);
                    bank.ValidateNativeAnimation(name, Float(animation, "get_duration"));
                }
                finally { ReleaseOwned(animation); }
            }
            if (expected.Count != 0) throw new InvalidDataException("Native Spine bank is missing frozen animation names.");
        }
        finally { GC.KeepAlive(resource); }
    }

    internal void Connect(LibrarianSpinePoseDriver driver)
    {
        AssertThread();
        if (_connected) throw new InvalidOperationException("Exactly one pose driver can own this binding.");
        _driver = driver;
        Add("animation_started", Callable.From<GodotObject, GodotObject, GodotObject>((sprite, state, entry) =>
            EntrySignal(sprite, state, entry, e => driver.NativeStarted(e))));
        Add("animation_completed", Callable.From<GodotObject, GodotObject, GodotObject>((sprite, state, entry) =>
            EntrySignal(sprite, state, entry, e => driver.NativeCompleted(e))));
        Add("animation_interrupted", Callable.From<GodotObject, GodotObject, GodotObject>((sprite, state, entry) =>
            EntrySignal(sprite, state, entry, e => driver.NativeInterrupted(e))));
        Add("animation_event", Callable.From<GodotObject, GodotObject, GodotObject, GodotObject>((sprite, state, entry, ev) =>
        {
            RetainCallbackTransient(ev);
            EntrySignal(sprite, state, entry, e =>
            {
                var data = Object(ev, "get_data") ?? throw new InvalidDataException("Null Spine event data.");
                try
                {
                    CheckClass(data, "SpineEventData", "get_event_name");
                    driver.NativeEvent(e, String(data, "get_event_name"), Float(ev, "get_time"));
                }
                finally { ReleaseOwned(data); GC.KeepAlive(ev); }
            });
        }));
        Add("before_animation_state_update", Callable.From<GodotObject>(BeforeUpdate));
        // Verified fixed extension order: state.apply -> this signal -> world transforms -> render.
        Add("before_world_transforms_change", Callable.From<GodotObject>(AfterApply));
        Add("world_transforms_changed", Callable.From<GodotObject>(AfterWorldTransforms));
        Add("tree_exiting", Callable.From(OnTreeExiting));
        _connected = true;
    }

    private void Add(string signal, Callable callable)
    {
        if (!_sprite.HasSignal(signal)) throw new InvalidDataException("Missing Spine signal: " + signal);
        Error result = _sprite.Connect(signal, callable);
        GC.KeepAlive(_sprite);
        GC.KeepAlive(callable);
        if (result != Error.Ok) throw new InvalidOperationException("Could not connect Spine signal: " + signal + "/" + result);
        _connections.Add((signal, callable));
    }
    private bool SameSprite(GodotObject sprite) => !_disposed && GodotObject.IsInstanceValid(_sprite)
        && sprite.GetInstanceId() == _sprite.GetInstanceId();
    private void EntrySignal(GodotObject sprite, GodotObject state, GodotObject entry, Action<ILibrarianSpineEntry> handler)
    {
        RetainCallbackTransient(entry);
        Guard(() =>
        {
            if (!SameSprite(sprite) || state.GetInstanceId() != _state.GetInstanceId() || _driver?.IsDisposed != false) return;
            // Callback arguments are borrowed for the whole signal broadcast, so do not dispose them inside a listener.
            var borrowed = new NativeEntry(this, entry, ownsWrapper: false);
            handler(borrowed);
            GC.KeepAlive(entry);
            GC.KeepAlive(state);
            GC.KeepAlive(sprite);
        });
    }

    // Fixed CPP creates a fresh RefCounted TrackEntry/Event wrapper for each callback. Retain it until
    // the broadcast/apply batch has returned, then retire it on this same main thread. State/Sprite are excluded.
    private void RetainCallbackTransient(GodotObject? value)
    {
        AssertThread();
        if (value is not RefCounted || !GodotObject.IsInstanceValid(value)) return;
        string cls = value.GetClass();
        if (cls is not "SpineTrackEntry" and not "SpineEvent") return;
        _callbackRetirement.TryAdd(value.GetInstanceId(), value);
        GC.KeepAlive(value);
    }
    private void DrainCallbackTransients()
    {
        AssertThread();
        foreach (var value in _callbackRetirement.Values) ReleaseOwned(value);
        _callbackRetirement.Clear();
    }
    private void Guard(Action action)
    {
        if (_disposed || _disposeQueued) return;
        AssertThread();
        try { action(); }
        catch (Exception error) { Fault(error); }
    }
    // Named frame handlers avoid creating a closure/Action on every native frame.
    private void BeforeUpdate(GodotObject sprite)
    {
        if (_disposed || _disposeQueued || !SameSprite(sprite)) return;
        AssertThread();
        try { DrainCallbackTransients(); _driver?.BeforeUpdate(); }
        catch (Exception error) { Fault(error); }
    }
    private void AfterApply(GodotObject sprite)
    {
        if (_disposed || _disposeQueued || !SameSprite(sprite)) return;
        AssertThread();
        try { _driver?.AfterApply(); DrainCallbackTransients(); }
        catch (Exception error) { Fault(error); }
    }
    private void AfterWorldTransforms(GodotObject sprite)
    {
        if (_disposed || _disposeQueued || !SameSprite(sprite) || _driver is not { IsDisposed: false } driver) return;
        AssertThread();
        try
        {
            // The fixed SpineSprite API returns the entire Godot global transform, including the
            // Sprite and parent transforms, Spine's Y convention and mirroring. Do not scale/flip it again.
            foreach (var socket in SocketKinds)
            {
                using var value = Call(_sprite, "get_global_bone_transform", LibrarianSpineMotionProfiles.BoneName(socket));
                var transform = value.AsTransform2D();
                Vector2 direction = transform.X.Normalized();
                if (!transform.Origin.IsFinite() || !direction.IsFinite() || direction.LengthSquared() < .5f)
                    throw new InvalidDataException("Nonfinite/degenerate Spine socket: " + socket);
                _sockets[socket] = new(transform.Origin, direction, driver.Generation, driver.BodyTag, Engine.GetProcessFrames());
            }
            driver.AfterWorldTransforms();
            DrainCallbackTransients();
        }
        catch (Exception error) { Fault(error); }
    }
    internal bool TryGetSocket(LibrarianSpineSocket socket, out LibrarianSpineSocketSample sample)
    {
        AssertThread();
        sample = default;
        return !_disposed && !_disposeQueued && _driver is { IsDisposed: false } driver
            && _sockets.TryGetValue(socket, out sample) && sample.Generation == driver.Generation
            && sample.TagBits == driver.BodyTag;
    }
    private void Fault(Exception error)
    {
        // Presentation faults never throw into a native gameplay command or alter its Task.
        try { _fault?.Invoke(error); } catch { }
        _disposeQueued = true;
        // Finish the native signal broadcast before retiring any callback-created wrapper.
        Callable.From(() => _driver?.Dispose()).CallDeferred();
    }
    private void OnTreeExiting() => _driver?.Dispose();

    public ILibrarianSpineEntry SetAnimation(string name, bool loop, int track)
    {
        AssertThread();
        var value = Object(_state, "set_animation", name, loop, track)
            ?? throw new InvalidOperationException("Spine set_animation returned null: " + name);
        return new NativeEntry(this, value, ownsWrapper: true);
    }
    public int? CurrentTag(int track)
    {
        AssertThread();
        var value = Object(_state, "get_current", track);
        if (value is null) return null;
        using var entry = new NativeEntry(this, value, ownsWrapper: true);
        return entry.TagBits;
    }
    public IReadOnlyList<ILibrarianSpineAttachment> Capture(IReadOnlyList<string> slots)
    {
        AssertThread();
        var result = new List<ILibrarianSpineAttachment>(slots.Count);
        try
        {
            foreach (string name in slots)
            {
                var slot = _slots[name];
                // get_attachment returns a new wrapper of the actual underlying pointer, or null.
                result.Add(new Attachment(this, name, slot, Object(slot, "get_attachment")));
            }
            return result;
        }
        catch { foreach (var attachment in result) attachment.Dispose(); throw; }
    }
    public void ApplyZero(Action afterApply)
    {
        AssertThread();
        Void(_state, "update", 0f);
        Void(_state, "apply", _skeleton);
        // Manual state.apply does not emit the Sprite's post-apply signal; apply our hold in this call too.
        afterApply();
    }
    public void ClearTrackIfOwned(int track, int tagBits)
    {
        AssertThread();
        if (tagBits != 0 && GodotObject.IsInstanceValid(_sprite) && CurrentTag(track) == tagBits)
            Void(_state, "clear_track", track);
    }
    public void DisconnectListeners()
    {
        AssertThread();
        if (GodotObject.IsInstanceValid(_sprite))
        {
            foreach (var (signal, callable) in _connections)
                if (_sprite.IsConnected(signal, callable)) _sprite.Disconnect(signal, callable);
            GC.KeepAlive(_sprite);
        }
        _connections.Clear();
        _connected = false;
    }
    public void Dispose()
    {
        if (_disposed) return;
        AssertThread();
        DisconnectListeners();
        DrainCallbackTransients();
        foreach (var slot in _slots.Values) ReleaseOwned(slot);
        _slots.Clear();
        _sockets.Clear();
        _driver = null;
        _disposed = true;
        _released?.Invoke();
        // _sprite, _state, _skeleton and the skeleton resource remain owned by Godot/native code.
        GC.KeepAlive(_state); GC.KeepAlive(_skeleton); GC.KeepAlive(_sprite);
    }

    private void AssertThread()
    {
        if (System.Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Spine wrappers must be called/released on their owning main thread.");
    }
    private static void CheckClass(GodotObject value, string expected, params string[] methods)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(value) || value.GetClass() != expected)
                throw new InvalidDataException("Unexpected Spine binding class; expected " + expected);
            foreach (string method in methods)
                if (!value.HasMethod(method)) throw new InvalidDataException(expected + " has no " + method);
        }
        finally { GC.KeepAlive(value); }
    }
    private Variant Call(GodotObject receiver, string method, params Variant[] arguments)
    {
        AssertThread();
        try { return receiver.Call(method, arguments); }
        finally
        {
            GC.KeepAlive(receiver);
            GC.KeepAlive(arguments);
            foreach (var argument in arguments) argument.Dispose();
        }
    }
    private GodotObject? Object(GodotObject receiver, string method, params Variant[] arguments)
    {
        using var result = Call(receiver, method, arguments);
        return result.VariantType == Variant.Type.Object ? result.AsGodotObject() : null;
    }
    private float Float(GodotObject receiver, string method) { using var value = Call(receiver, method); return value.AsSingle(); }
    private int Int(GodotObject receiver, string method) { using var value = Call(receiver, method); return value.AsInt32(); }
    private bool Bool(GodotObject receiver, string method) { using var value = Call(receiver, method); return value.AsBool(); }
    private string String(GodotObject receiver, string method) { using var value = Call(receiver, method); return value.AsString(); }
    private void Void(GodotObject receiver, string method, params Variant[] arguments) { using var result = Call(receiver, method, arguments); }
    private void ReleaseOwned(GodotObject? value)
    {
        AssertThread();
        if (value is not null && GodotObject.IsInstanceValid(value)) value.Dispose();
        GC.KeepAlive(value);
    }

    private sealed class NativeEntry : ILibrarianSpineEntry
    {
        private readonly LibrarianSpineNativeBinding _owner;
        private GodotObject? _value;
        private readonly bool _ownsWrapper;
        private GodotObject Value => _value ?? throw new ObjectDisposedException(nameof(NativeEntry));
        internal NativeEntry(LibrarianSpineNativeBinding owner, GodotObject value, bool ownsWrapper)
        {
            _owner = owner; _value = value; _ownsWrapper = ownsWrapper;
            CheckClass(value, "SpineTrackEntry", "get_track_index", "get_animation", "get_track_time", "set_track_time",
                "get_mix_time", "get_mix_duration", "set_mix_duration", "get_track_end", "set_track_end",
                "get_time_scale", "set_time_scale", "is_complete", "set_event_threshold",
                "set_mix_attachment_threshold", "set_mix_draw_order_threshold");
        }
        public int Track => _owner.Int(Value, "get_track_index");
        public string Name => Animation(a => _owner.String(a, "get_name"));
        public float Duration => Animation(a => _owner.Float(a, "get_duration"));
        private T Animation<T>(Func<GodotObject, T> read)
        {
            var animation = _owner.Object(Value, "get_animation") ?? throw new InvalidDataException("Entry animation is null.");
            try { return read(animation); } finally { _owner.ReleaseOwned(animation); GC.KeepAlive(Value); }
        }
        public float TrackTime { get => _owner.Float(Value, "get_track_time"); set => _owner.Void(Value, "set_track_time", value); }
        public float MixTime => _owner.Float(Value, "get_mix_time");
        public float MixDuration { get => _owner.Float(Value, "get_mix_duration"); set => _owner.Void(Value, "set_mix_duration", value); }
        public float TimeScale { get => _owner.Float(Value, "get_time_scale"); set => _owner.Void(Value, "set_time_scale", value); }
        public int TagBits { get => LibrarianSpineEntryTags.FromFloat(_owner.Float(Value, "get_track_end"));
            set => _owner.Void(Value, "set_track_end", LibrarianSpineEntryTags.AsFloat(value)); }
        public bool IsComplete => _owner.Bool(Value, "is_complete");
        public void DisableMixingCallbacks()
        {
            _owner.Void(Value, "set_event_threshold", 0f);
            _owner.Void(Value, "set_mix_attachment_threshold", 0f);
            _owner.Void(Value, "set_mix_draw_order_threshold", 0f);
        }
        public void Dispose()
        {
            if (_value is null) return;
            if (_ownsWrapper) _owner.ReleaseOwned(_value);
            _value = null;
        }
    }
    private sealed class Attachment(LibrarianSpineNativeBinding owner, string name, GodotObject slot, GodotObject? value)
        : ILibrarianSpineAttachment
    {
        private GodotObject? _value = value; // null is a real cached pointer, never replaced with setup/fallback.
        private bool _disposed;
        public string SlotName => name;
        public void Restore()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Attachment));
            owner.Void(slot, "set_attachment", _value is null ? default : Variant.From(_value));
            GC.KeepAlive(_value); GC.KeepAlive(slot);
        }
        public void Dispose()
        {
            if (_disposed) return;
            owner.ReleaseOwned(_value); _value = null; _disposed = true;
        }
    }
}

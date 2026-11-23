using System.Runtime.CompilerServices;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Librarian.Mechanics;

internal sealed record LibrarianCardRemasterObservation(string CardClass, string Phase, Creature Target,
    CardPlay? Play, Node2D? Effect, ulong EffectIdentity = 0, bool? HasContact = null, long HitIdentity = 0,
    Creature? OriginalTarget = null);

/// <summary>
/// Visual command boundary: native AttackCommand alone chooses targets and hit count.
/// Each native hit factory starts one flight; BeforeDamage commits its contact. No RNG,
/// damage, target filtering or rule state is owned here. Native result feedback stays native.
/// </summary>
internal static class LibrarianCardRemasterVfx
{
    private sealed class Attachment { }
    private sealed class HitPresentation(Creature target)
    {
        internal readonly Creature Target = target;
        internal readonly long Identity = Interlocked.Increment(ref _nextHit);
        internal LibrarianCardFlightVfx? Effect;
    }
    private sealed record HitStamp(long Identity);
    private sealed class PendingHits { internal readonly ConditionalWeakTable<Creature,HitStamp> Targets = new(); }
    private static long _nextHit;
    private static readonly ConditionalWeakTable<CardModel,PendingHits> PendingResults = new();
    private static void MarkResult(CardModel card,Creature target,long identity)
    {
        var targets = PendingResults.GetValue(card,_ => new PendingHits()).Targets;
        targets.Remove(target);
        targets.Add(target,new(identity));
    }
    private static readonly ConditionalWeakTable<AttackCommand, Attachment> Attached = new();
    internal static Action<LibrarianCardRemasterObservation>? Observer;

    internal static AttackCommand Attach(AttackCommand command, CardModel card, CardPlay play)
    {
        if (!LibrarianCardVfx050.TryProfile(card, out var profile) || Attached.TryGetValue(command, out _)) return command;
        Attached.Add(command, new());
        var recipe = LibrarianRemasterRecipe.For(card.GetType().Name, profile);
        var hits = new List<HitPresentation>();
        string? nativeHitVfx = null;
        bool nativeSuppressed = false;
        // This boundary is for our card builders, which do not install BeforeDamage logic.
        // Do not apply it to foreign builders: the native BeforeDamage setter is not additive.
        command.WithHitVfxNode(target =>
        {
            if (hits.Count == 0) nativeHitVfx = command.HitVfx;
            var hit = new HitPresentation(target);
            hits.Add(hit); // Include rejected visuals: the native target was already selected.
            LibrarianOrbVfx.Safely(() =>
            {
                if (!LibrarianCardVfx050.Available(card.Owner)) return;
                var room = NCombatRoom.Instance;
                var targetBox = room?.GetCreatureNode(target)?.Hitbox;
                var casterBox = room?.GetCreatureNode(card.Owner.Creature)?.Hitbox;
                if (targetBox is null || casterBox is null) return;
                if (LibrarianCardVfx050.Active >= LibrarianPreferences050.Current.EffectLimit)
                { LibrarianCardVfx050.RecordDropped(); return; }
                Vector2 start = LibrarianSpineRuntime.TryGetSocket(card.Owner.Creature, LibrarianSpineSocket.ScreenRight, out var socket)
                    ? socket.GlobalPosition : casterBox.GetGlobalTransform() * (casterBox.Size * new Vector2(.65f, .45f));
                Vector2 end = targetBox.GetGlobalTransform() * (targetBox.Size * new Vector2(.5f, .46f));
                if (recipe.Motion is LibrarianRemasterMotion.GroundRoots or LibrarianRemasterMotion.WaterSurge)
                {
                    float floor = recipe.Motion == LibrarianRemasterMotion.GroundRoots ? .88f : .85f;
                    start = casterBox.GetGlobalTransform() * (casterBox.Size * new Vector2(.65f,floor));
                    end = targetBox.GetGlobalTransform() * (targetBox.Size * new Vector2(.5f,floor));
                }
                hit.Effect = new LibrarianCardFlightVfx
                {
                    Name = "LibrarianCardFlightVfx", Profile = profile, Card = card, Play = play,
                    Target = target, StartGlobal = start, EndGlobal = end,
                    HitIdentity = hit.Identity,
                    Recipe = recipe, TargetDesignSize = targetBox.Size,
                    DesignScale = targetBox.GetGlobalTransform().Scale.Abs().X,
                    TravelDuration = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode
                        == MegaCrit.Sts2.Core.Settings.FastModeType.Fast ? recipe.Fast : recipe.Normal
                };
                // The old universal slash would otherwise fire before the flight arrived.
                // Suppression is scoped to this native hit, restored before damage/next hit.
                if (command.HitVfx is "vfx/vfx_attack_slash" or "vfx/vfx_bite")
                {
                    nativeSuppressed = true;
                    command.WithHitFx(null, command.HitSfx, command.TmpHitSfx);
                }
            });
            return hit.Effect;
        });
        return command.BeforeDamage(async () =>
        {
            // Native wait honors fast/instant and noninteractive/ending combats. Contact is
            // explicit even when that wait is skipped; no unbounded signal task can hang damage.
            try
            {
                if (hits.Any(hit => hit.Effect is not null)) await Cmd.CustomScaledWait(recipe.Fast, recipe.Normal);
                foreach (var hit in hits)
                {
                    MarkResult(card,hit.Target,hit.Identity);
                    LibrarianOrbVfx.Safely(() =>
                    {
                        var effect = hit.Effect;
                        bool aliveEffect = effect is not null && GodotObject.IsInstanceValid(effect)
                            && effect.IsInsideTree() && !effect.IsQueuedForDeletion();
                        bool contact = aliveEffect && LibrarianCardVfx050.Available(card.Owner) && effect!.Contact();
                        if (!contact && aliveEffect) effect!.QueueFree();
                        if (!nativeSuppressed)
                        {
                            if (!contact && nativeHitVfx is not null)
                                Observer?.Invoke(new(card.GetType().Name,"native-pass-through",hit.Target,play,null,HitIdentity:hit.Identity));
                            return; // Native Execute already played the unsuppressed VFX.
                        }
                        // Mixed AOE/late toggle: rejected recipients retain the original
                        // native contact at this hit's arrival, with no extra SFX or target RNG.
                        bool targetPresent = !hit.Target.IsDead && NCombatRoom.Instance?.GetCreatureNode(hit.Target) is { } node
                            && GodotObject.IsInstanceValid(node) && node.IsInsideTree()
                            && (effect is null || effect.TargetStillPresent());
                        if (targetPresent && nativeHitVfx is not null && (!contact || nativeHitVfx == "vfx/vfx_bite"))
                        {
                            if (contact && nativeHitVfx == "vfx/vfx_bite") effect!.AddBoundedBite();
                            else VfxCmd.PlayOnCreatureCenter(hit.Target,nativeHitVfx);
                            if (!contact) Observer?.Invoke(new(card.GetType().Name,"native-fallback",hit.Target,play,effect,
                                effect?.ObservationId ?? 0,effect?.HasContact,hit.Identity));
                        }
                    });
                }
            }
            finally
            {
                if (nativeSuppressed) command.WithHitFx(nativeHitVfx,command.HitSfx,command.TmpHitSfx);
                nativeSuppressed = false;
                hits.Clear();
            }
        });
    }

    internal static async Task Delayed(CardModel source, Creature target)
    {
        long hitIdentity = Interlocked.Increment(ref _nextHit);
        LibrarianCardFlightVfx? effect = null;
        LibrarianOrbVfx.Safely(() =>
        {
            if (!LibrarianCardVfx050.TryProfile(source, out var profile) || !LibrarianCardVfx050.Available(source.Owner)) return;
            var room = NCombatRoom.Instance;
            var targetBox = room?.GetCreatureNode(target)?.Hitbox;
            var ownerBox = room?.GetCreatureNode(source.Owner.Creature)?.Hitbox;
            if (room is null || targetBox is null || ownerBox is null) return;
            if (LibrarianCardVfx050.Active >= LibrarianPreferences050.Current.EffectLimit)
            { LibrarianCardVfx050.RecordDropped(); return; }
            Vector2 start = LibrarianSpineRuntime.TryGetSocket(source.Owner.Creature, LibrarianSpineSocket.Book, out var socket)
                ? socket.GlobalPosition : ownerBox.GetGlobalTransform() * (ownerBox.Size / 2);
            effect = new LibrarianCardFlightVfx
            {
                Name = "LibrarianDelayedFlightVfx", Card = source, Target = target, Profile = profile,
                HitIdentity = hitIdentity,
                Recipe = LibrarianRemasterRecipe.For(source.GetType().Name, profile), TargetDesignSize = targetBox.Size,
                StartGlobal = start, EndGlobal = targetBox.GetGlobalTransform() * (targetBox.Size * new Vector2(.5f,.46f)),
                DesignScale = targetBox.GetGlobalTransform().Scale.Abs().X,
                TravelDuration = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode
                    == MegaCrit.Sts2.Core.Settings.FastModeType.Fast ? .16f : .32f
            };
            room.CombatVfxContainer.AddChild(effect);
        });
        if (effect is null) { MarkResult(source,target,hitIdentity); return; }
        await Cmd.CustomScaledWait(effect.Recipe.Fast, effect.Recipe.Normal);
        LibrarianOrbVfx.Safely(() =>
        {
            if (GodotObject.IsInstanceValid(effect) && effect.IsInsideTree() && !effect.IsQueuedForDeletion()) effect.Contact();
        });
        MarkResult(source,target,hitIdentity);
    }

    internal static void Result(CardModel card, Creature target, Creature? receiver = null)
    {
        long identity = 0;
        if (PendingResults.TryGetValue(card,out var pending) && pending.Targets.TryGetValue(target,out var stamp))
        { identity = stamp.Identity; pending.Targets.Remove(target); }
        Observer?.Invoke(new(card.GetType().Name,"native-result",receiver ?? target,null,null,
            HitIdentity:identity,OriginalTarget:target));
    }
}

/// <summary>Original geometry with native-style preparation/flight/contact/residue lifetimes.</summary>
public partial class LibrarianCardFlightVfx : Node2D
{
    internal SpellProfile050 Profile { get; init; } = new(SpellElement050.Page, SpellShape050.Slash);
    internal CardModel Card { get; init; } = null!;
    internal CardPlay? Play { get; init; }
    internal long HitIdentity { get; init; }
    internal ulong ObservationId { get; private set; }
    internal Creature Target { get; init; } = null!;
    internal Vector2 StartGlobal { get; init; }
    internal Vector2 EndGlobal { get; init; }
    internal float DesignScale { get; init; } = 1;
    internal float TravelDuration { get; init; } = .20f;
    internal LibrarianRemasterRecipe Recipe { get; init; } = new(LibrarianRemasterMotion.PaperBlade,.17f,.085f);
    internal Vector2 TargetDesignSize { get; init; } = new(100,160);
    internal bool HasContact { get; private set; }
    internal float Progress => HasContact ? 1 : Math.Clamp(_age / TravelDuration, 0, 1);
    private readonly Vector2[] _path = new Vector2[18];
    private readonly Vector2[] _quad = new Vector2[4];
    private readonly Vector2[] _flameContour = new Vector2[24];
    private readonly Vector2[] _fluid = new Vector2[36];
    private Vector2 _end;
    private float _age, _contactAge;
    private float _contactLifetime = .38f;
    private LibrarianNativeCardLayersVfx? _nativeFlight;
    private LibrarianNativeCardLayersVfx? _nativeContact;
    private bool _counted;
    private bool _naturalFinish;
    private NCombatRoom? _room;
    private Node? _targetNode;
    private float Unit => Math.Max(.01f, DesignScale / Math.Max(.01f, GetGlobalTransform().Scale.Abs().X));

    public override void _EnterTree() { ObservationId = GetInstanceId(); LibrarianCardVfx050.Enter(); _counted = true; }
    public override void _Ready()
    {
        _room = NCombatRoom.Instance;
        _targetNode = _room?.GetCreatureNode(Target);
        GlobalPosition = StartGlobal;
        _end = ToLocal(EndGlobal);
        _nativeFlight = LibrarianNativeCardLayersVfx.Create(Recipe.Motion,TargetDesignSize,flight:true);
        if (_nativeFlight is not null) { _nativeFlight.Scale=Vector2.One*Unit; AddChild(_nativeFlight); }
        LibrarianCardRemasterVfx.Observer?.Invoke(new(Card.GetType().Name,"flight",Target,Play,this,ObservationId,HasContact,HitIdentity));
    }
    public override void _ExitTree()
    {
        if (_counted) { _counted = false; LibrarianCardVfx050.Exit(); }
        if (!_naturalFinish)
            LibrarianCardRemasterVfx.Observer?.Invoke(new(Card.GetType().Name,"cancel",Target,Play,this,ObservationId,HasContact,HitIdentity));
    }
    internal bool TargetStillPresent()
        => !Target.IsDead && _room is not null && ReferenceEquals(_room,NCombatRoom.Instance)
            && GodotObject.IsInstanceValid(_room) && _room.IsInsideTree()
            && Target.CombatState is not null && ReferenceEquals(Target.CombatState,Card.Owner.Creature.CombatState)
            && _targetNode is not null && GodotObject.IsInstanceValid(_targetNode) && _targetNode.IsInsideTree()
            && ReferenceEquals(_room.GetCreatureNode(Target),_targetNode);
    internal bool Contact()
    {
        if (HasContact) return true;
        if (!TargetStillPresent()) { QueueFree(); return false; }
        HasContact = true;
        _contactAge = 0;
        _nativeContact = LibrarianNativeCardLayersVfx.Create(Recipe.Motion,TargetDesignSize);
        if (_nativeContact is not null)
        {
            _nativeContact.Position = _end;
            if (Recipe.Motion is LibrarianRemasterMotion.WaterSurge or LibrarianRemasterMotion.PaperBlade or LibrarianRemasterMotion.ChargedRay)
                _nativeContact.Position += new Vector2(0,-Math.Clamp(TargetDesignSize.Y*
                    (Recipe.Motion==LibrarianRemasterMotion.WaterSurge?.25f:.10f),6,30)*Unit);
            _nativeContact.Scale = Vector2.One * Unit;
            if (Recipe.Motion is LibrarianRemasterMotion.PaperBlade or LibrarianRemasterMotion.Needle)
                _nativeContact.Rotation = _end.Angle();
            AddChild(_nativeContact);
            _contactLifetime = Math.Max(_contactLifetime,_nativeContact.Lifetime);
        }
        LibrarianCardRemasterVfx.Observer?.Invoke(new(Card.GetType().Name,"contact",Target,Play,this,ObservationId,HasContact,HitIdentity));
        QueueRedraw();
        return true;
    }
    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (HasContact) _contactAge += (float)delta;
        var prefs = LibrarianPreferences050.Current;
        if (!HasContact && !TargetStillPresent()) { QueueFree(); return; }
        // The final lethal contact may outlive combat by its short residue; toggles still
        // cancel it. No follow-up event, target lookup or gameplay task survives the hit.
        bool finishingContact = HasContact && prefs.CardEffects
            && MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode != MegaCrit.Sts2.Core.Settings.FastModeType.Instant
            && (prefs.TeammateEffects || MegaCrit.Sts2.Core.Context.LocalContext.IsMe(Card.Owner));
        if ((!finishingContact && !LibrarianCardVfx050.Available(Card.Owner)) || _age > 1.5f)
        { QueueFree(); return; }
        if (_nativeFlight is not null && GodotObject.IsInstanceValid(_nativeFlight))
            _nativeFlight.Position = Point(Mathf.SmoothStep(0,1,Progress));
        if (HasContact && _contactAge >= _contactLifetime) { _naturalFinish = true; QueueFree(); return; }
        QueueRedraw();
    }
    private Vector2 Point(float t)
    {
        // Native played-card presentation occupies the centre above the combat lane.
        // A shallow lower arc keeps paper/needle/fire bodies visible below that card.
        float arc = Recipe.Motion is LibrarianRemasterMotion.PaperBlade or LibrarianRemasterMotion.Seal ? 42
            : Recipe.Motion == LibrarianRemasterMotion.Needle ? 28
            : Recipe.Motion == LibrarianRemasterMotion.Firebolt ? 30 : -12;
        return _end * t + new Vector2(0, Mathf.Sin(t * Mathf.Pi) * arc * Unit);
    }
    internal void AddBoundedBite()
    {
        var bite = new LibrarianBoundedBiteVfx
        { Name="LibrarianBoundedBite",Position=_end+new Vector2(0,-TargetDesignSize.Y*.38f*Unit),Scale=Vector2.One*Unit,TargetSize=TargetDesignSize };
        AddChild(bite);
        _contactLifetime=Math.Max(_contactLifetime,.55f);
    }
    public override void _Draw()
    {
        var prefs = LibrarianPreferences050.Current;
        float alpha = prefs.EffectOpacity / 100f;
        if (alpha <= .002f) return;
        Color c = LibrarianCardVfx050.Tint(Profile.Element);
        Color light = c.Lightened(.65f);
        float u = Unit;
        float p = Mathf.SmoothStep(0, 1, Progress);
        if (HasContact)
        {
            float t = Math.Clamp(_contactAge / .28f, 0, 1);
            float fade = alpha * (1 - t) * (1 - t);
            float expansion = prefs.ReducedMotion ? .25f : 1 - Mathf.Pow(1 - t, 3);
            if (prefs.ReducedMotion)
                DrawContact(_end,14*u,c,light,fade);
            else if (Recipe.Motion == LibrarianRemasterMotion.GroundRoots)
                DrawGroundRoots(1,c,light,alpha*(1-Math.Clamp(_contactAge/.38f,0,1)),wrap:true);
            else if (!prefs.Particles || _nativeContact is null || !GodotObject.IsInstanceValid(_nativeContact)
                || _nativeContact.IsQueuedForDeletion() || !_nativeContact.HasCore)
                DrawContact(_end, (12 + expansion * 24) * u, c, light, fade);
            if (prefs.Particles && !prefs.ReducedMotion)
                for (int i = 0; i < 6; i++)
                {
                    var axis = Vector2.FromAngle(i * 2.399963f);
                    Vector2 center = _end + axis * (9 + t * 43) * u + new Vector2(0, t * t * 15 * u);
                    DrawLine(center, center - axis * 5 * u, A(c, fade * .65f), 1.6f * u, true);
                }
            return;
        }
        if (prefs.ReducedMotion)
        {
            // Fixed local hints only; the body never travels when motion is reduced.
            DrawContact(Vector2.Zero, 7 * u, c, light, alpha * .35f);
            return;
        }
        Vector2 head = Point(p);
        Vector2 direction = _end.LengthSquared() > .01f ? _end.Normalized() : Vector2.Right;
        if (Recipe.Motion == LibrarianRemasterMotion.GroundRoots)
        { DrawGroundRoots(p,c,light,alpha,wrap:false); return; }
        if (Recipe.Motion == LibrarianRemasterMotion.WaterSurge)
        { DrawWaterSurge(p,c,light,alpha); return; }
        if (Recipe.Motion == LibrarianRemasterMotion.SteamFront)
        {
            if (!prefs.Particles || _nativeFlight is null || !GodotObject.IsInstanceValid(_nativeFlight) || _nativeFlight.IsQueuedForDeletion())
                for(int i=0;i<3;i++)
                {
                    Vector2 center=head-direction*(i*10*u)+new Vector2(0,(i%2==0?-1:1)*7*u);
                    DrawCircle(center,(9+p*7)*u,A(c,alpha*.22f));
                    DrawArc(center,(8+p*6)*u,-2.8f,.4f,16,A(light,alpha*.5f),2*u,true);
                }
            return;
        }
        if (Recipe.Motion == LibrarianRemasterMotion.ChargedRay)
        {
            float release=Math.Clamp((p-.22f)/.78f,0,1);
            for(int i=0;i<3;i++)
            {
                Color tint=LibrarianCardVfx050.Tint((SpellElement050)(i+1));
                Vector2 offset=direction.Orthogonal()*(i-1)*5*u;
                DrawLine(offset,Point(release)+offset,A(tint,alpha*.28f),5*u,true);
                DrawLine(offset,Point(release)+offset,A(tint.Lightened(.45f),alpha*.85f),1.6f*u,true);
            }
            return;
        }
        if (prefs.Particles)
        {
            float tail = Math.Min(p, 88 * u / Math.Max(88 * u, _end.Length()));
            for (int i = 0; i < _path.Length; i++) _path[i] = Point(p - tail + tail * i / (_path.Length - 1));
            if (tail > .002f)
            {
                DrawPolyline(_path, A(c, alpha * .14f), 9 * u, true);
                DrawPolyline(_path, A(c, alpha * .55f), 3 * u, true);
            }
        }
        DrawBody(head, direction, Recipe.Motion == LibrarianRemasterMotion.PaperBlade ? u*1.4f : u, c, light, alpha);
    }
    private void DrawWaterSurge(float progress,Color color,Color light,float alpha)
    {
        float start=Math.Max(0,progress-.44f),u=Unit;
        if(progress<.006f) return;
        Vector2 side=(_end.LengthSquared()>.01f?_end.Normalized():Vector2.Right).Orthogonal();
        for(int i=0;i<_path.Length;i++)
        {
            float t=i/(float)(_path.Length-1),sample=Mathf.Lerp(start,progress,t);
            Vector2 center=Point(sample);
            float width=(2+Mathf.Sin(t*Mathf.Pi)*13)*u;
            float crest=Mathf.Sin(t*9-_age*15)*2.5f*u;
            _fluid[i]=center+side*Math.Max(u,width+crest);
            _fluid[_fluid.Length-1-i]=center-side*width*.65f;
            _path[i]=center-side*(width*.55f+crest*.6f);
        }
        DrawColoredPolygon(_fluid,A(color,alpha*.62f));
        DrawPolyline(_path,A(light,alpha*.85f),2.8f*u,true);
        for(int i=0;i<_path.Length;i++) _path[i]+=(i%2==0?1:-1)*side*2*u;
        DrawPolyline(_path,A(light,alpha*.25f),7*u,true);
    }
    private void DrawGroundRoots(float progress,Color color,Color light,float alpha,bool wrap)
    {
        if(progress<.005f || alpha<.003f) return;
        float u=Unit;
        Color wood=color.Lerp(new Color("786348"),.72f),edge=wood.Darkened(.42f);
        float groundAlpha=wrap?alpha*Math.Clamp(1-_contactAge/.18f,0,1):alpha;
        for(int branch=0;branch<3;branch++)
        {
            float reach=Math.Clamp((progress-branch*.07f)/(1-branch*.07f),0,1);
            if(reach<.01f || groundAlpha<.005f) continue;
            for(int i=0;i<_path.Length;i++)
            {
                float t=i/(float)(_path.Length-1)*reach;
                float bend=Mathf.Sin(t*Mathf.Pi)*(Mathf.Sin(t*(8+branch*2)+branch)*10+(branch-1)*9)*u;
                _path[i]=_end*t+new Vector2(0,bend);
            }
            RootRibbon((7-branch*1.5f)*u,edge,wood,groundAlpha);
            for(int i=5;i<_path.Length-2;i+=5)
            {
                Vector2 point=_path[i],tip=point+new Vector2(10+branch*3,-12-i%3*4)*u;
                Quad(point-new Vector2(3,0)*u,point+new Vector2(3,-5)*u,tip,point+new Vector2(5,2)*u,A(wood,groundAlpha));
            }
        }
        if(!wrap) return;
        float rise=.28f+.72f*(1-Mathf.Pow(1-Math.Clamp(_contactAge/.10f,0,1),3));
        for(int branch=0;branch<2;branch++)
        {
            for(int i=0;i<_path.Length;i++)
            {
                float t=i/(float)(_path.Length-1)*rise;
                _path[i]=_end+new Vector2(Mathf.Sin(t*9+branch*Mathf.Pi)*TargetDesignSize.X*.34f,-t*TargetDesignSize.Y*.72f)*u;
            }
            RootRibbon(5.5f*u,edge,wood.Lightened(.12f),alpha);
        }
    }
    private void RootRibbon(float width,Color edge,Color body,float alpha)
    {
        for(int i=0;i<_path.Length;i++)
        {
            Vector2 direction=(_path[Math.Min(i+1,_path.Length-1)]-_path[Math.Max(i-1,0)]).Normalized();
            float taper=(1-i/(float)_path.Length)*.75f+.25f;
            Vector2 side=direction.Orthogonal()*width*taper*.5f;
            _fluid[i]=_path[i]+side; _fluid[_fluid.Length-1-i]=_path[i]-side;
        }
        DrawColoredPolygon(_fluid,A(body,alpha));
        DrawPolyline(_path,A(edge,alpha*.48f),1.3f*Unit,true);
    }
    private void DrawBody(Vector2 p, Vector2 d, float u, Color c, Color light, float a)
    {
        Vector2 side = d.Orthogonal();
        if (Profile.Shape == SpellShape050.Needle)
        {
            Quad(p - d * 20 * u, p + side * 3 * u, p + d * 14 * u, p - side * 3 * u, A(light, a));
            return;
        }
        if (Profile.Element == SpellElement050.Prismatic)
        {
            for (int i = 0; i < 3; i++)
            {
                var point = p + Vector2.FromAngle(i * Mathf.Tau / 3 - Mathf.Pi / 2) * 6 * u;
                var tint = LibrarianCardVfx050.Tint((SpellElement050)(1 + i));
                Quad(point - d * 7 * u, point + side * 3 * u, point + d * 6 * u,
                    point - side * 3 * u, A(tint, a));
            }
            return;
        }
        if (Profile.Element == SpellElement050.Water)
        {
            Quad(p - d * 18 * u, p + side * 7 * u, p + d * 12 * u, p - side * 5 * u, A(c, a * .8f));
            DrawArc(p, 7 * u, d.Angle() - 1.4f, d.Angle() + 1.4f, 12, A(light, a), 2 * u, true);
        }
        else if (Profile.Element == SpellElement050.Fire)
        {
            float phase = LibrarianPreferences050.Current.ReducedMotion ? 0 : _age * 19;
            DrawCircle(p,17*u,A(c,a*.07f));
            FlameLayer(p,d,12*u,phase,new Color(.86f,.23f,.045f,a*.90f));
            FlameLayer(p+d*2*u,d,8.5f*u,phase+.7f,new Color(1,.57f,.11f,a));
            FlameLayer(p+d*4*u,d,4.4f*u,phase*.65f,new Color(1,.94f,.65f,a));
        }
        else if (Profile.Element == SpellElement050.Leaf)
        {
            Quad(p - d * 13 * u, p + side * 6 * u, p + d * 12 * u, p - side * 6 * u, A(c, a));
            DrawLine(p - d * 11 * u, p + d * 10 * u, A(light, a), 1.2f * u, true);
        }
        else
        {
            Quad(p - d * 9 * u - side * 7 * u, p + d * 10 * u - side * 6 * u,
                p + d * 7 * u + side * 7 * u, p - d * 10 * u + side * 6 * u, A(c, a));
            DrawLine(p - d * 6 * u, p + d * 6 * u, A(light, a), 1.5f * u, true);
        }
    }
    private void FlameLayer(Vector2 p,Vector2 direction,float radius,float phase,Color color)
    {
        if (radius < .6f || color.A < .003f) return;
        Vector2 side=direction.Orthogonal();
        for(int i=0;i<_flameContour.Length;i++)
        {
            float angle=i*Mathf.Tau/_flameContour.Length;
            float wave=1+.10f*Mathf.Sin(angle*3+phase)+.05f*Mathf.Sin(angle*5-phase*.7f);
            // Rearward elongation gives the flame a clear direction without a global flash.
            float x=Mathf.Cos(angle);
            _flameContour[i]=p+direction*x*radius*(x<0?1.6f:1)*wave
                +side*Mathf.Sin(angle)*radius*.66f*wave;
        }
        DrawColoredPolygon(_flameContour,color);
    }
    private void DrawContact(Vector2 p, float r, Color c, Color light, float a)
    {
        if (a < .003f) return;
        switch (Profile.Shape)
        {
            case SpellShape050.Seal:
                Quad(p+new Vector2(0,-r*.7f),p+new Vector2(r*.38f,0),
                    p+new Vector2(0,r*.7f),p+new Vector2(-r*.38f,0),A(c,a*.25f));
                DrawLine(p+new Vector2(0,-r*.55f),p+new Vector2(0,r*.55f),A(light,a),2*Unit,true);
                break;
            case SpellShape050.Ring:
                for (int i = 0; i < 3; i++)
                {
                    Vector2 dir = Vector2.FromAngle(i * Mathf.Tau / 3 - Mathf.Pi / 2);
                    Color tint = LibrarianCardVfx050.Tint((SpellElement050)(1+i));
                    DrawLine(p+dir*r*.2f,p+dir*r,A(tint,a),3*Unit,true);
                }
                break;
            case SpellShape050.Root:
                for (int i = 0; i < 3; i++)
                {
                    Vector2 basePoint = p + new Vector2((i - 1) * r * .4f, r * .35f);
                    DrawLine(basePoint, basePoint + new Vector2((i - 1) * r * .20f, -r), A(c, a), 3 * Unit, true);
                    DrawBody(basePoint + new Vector2(0, -r * .7f), Vector2.Up, Unit * .6f, c, light, a);
                }
                break;
            case SpellShape050.Ripple:
                DrawSetTransform(p, 0, new(1, .40f));
                DrawArc(Vector2.Zero, r, .15f, 5.8f, 28, A(c, a * .8f), 3 * Unit, true);
                DrawArc(Vector2.Zero, r * .65f, .4f, 5.6f, 24, A(light, a), 1.3f * Unit, true);
                DrawSetTransform(Vector2.Zero);
                break;
            case SpellShape050.Steam:
                for(int i=0;i<3;i++)
                {
                    Vector2 puff=p+new Vector2((i-1)*r*.48f,-r*(.18f+i%2*.30f));
                    DrawCircle(puff,r*.36f,A(c,a*.13f));
                    DrawArc(puff,r*.30f,-2.8f,.1f,18,A(light,a*.65f),2*Unit,true);
                }
                break;
            case SpellShape050.Stone:
                for (int i = 0; i < 4; i++)
                {
                    var v = p + Vector2.FromAngle(i * 2.4f) * r * .55f;
                    Quad(v + new Vector2(-5, 0) * Unit, v + new Vector2(-2, -8) * Unit,
                        v + new Vector2(5, -3) * Unit, v + new Vector2(4, 4) * Unit, A(c, a));
                }
                break;
            case SpellShape050.Ember:
                for (int i = 0; i < 3; i++)
                    DrawBody(p + new Vector2((i - 1) * r * .35f, -r * .15f), Vector2.Up, Unit * (.7f + i % 2 * .2f), c, light, a);
                break;
            default:
                var diagonal = new Vector2(.8f, -.6f) * r;
                DrawLine(p - diagonal, p + diagonal, A(c, a * .25f), 7 * Unit, true);
                DrawLine(p - diagonal, p + diagonal, A(light, a), 2 * Unit, true);
                break;
        }
    }
    private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    { _quad[0] = a; _quad[1] = b; _quad[2] = c; _quad[3] = d; DrawColoredPolygon(_quad, color); }
    private static Color A(Color color, float alpha) => new(color.R, color.G, color.B, alpha);
}

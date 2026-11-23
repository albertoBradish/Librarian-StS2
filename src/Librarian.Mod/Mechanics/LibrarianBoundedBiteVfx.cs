using Godot;

namespace Librarian.Mechanics;

/// <summary>Native bite animation fitted to its live skeleton bounds, never the global native factory.</summary>
public partial class LibrarianBoundedBiteVfx : Node2D
{
    private static PackedScene? _scene;
    private Node2D? _native;
    private Node2D? _sprite;
    private float _age;
    private bool _fitted;
    private readonly Vector2[] _tooth = new Vector2[3];
    internal Vector2 TargetSize { get; init; } = new(80,100);
    internal Rect2 LastNativeBounds { get; private set; }
    internal bool NativeFitted => _fitted;
    public override void _Ready()
    {
        Modulate=new(1,1,1,LibrarianPreferences050.Current.EffectOpacity/100f);
        if (!LibrarianPreferences050.Current.Particles || LibrarianPreferences050.Current.ReducedMotion) return;
        LibrarianOrbVfx.Safely(() =>
        {
            _scene ??= ResourceLoader.Load<PackedScene>("res://scenes/vfx/vfx_bite.tscn");
            if (_scene is null) return;
            _native=_scene.Instantiate<Node2D>();
            _native.Visible=false; // Never flash at the unbounded native size while Spine loads.
            _native.Position=Vector2.Zero;
            AddChild(_native);
            _sprite=_native.GetNodeOrNull<Node2D>("SpineSprite");
        });
    }
    public override void _Process(double delta)
    {
        _age+=(float)delta;
        var prefs=LibrarianPreferences050.Current;
        if (_age>.55f || !prefs.CardEffects) { QueueFree(); return; }
        Modulate=new(1,1,1,prefs.EffectOpacity/100f);
        if (_native is not null && GodotObject.IsInstanceValid(_native) && !_native.IsQueuedForDeletion())
        {
            _native.Visible=false;
            _fitted=false;
            if (prefs.Particles && !prefs.ReducedMotion && _sprite is not null && GodotObject.IsInstanceValid(_sprite)
                && _sprite.HasMethod("get_skeleton")) LibrarianOrbVfx.Safely(() =>
            {
                var skeleton=_sprite.Call("get_skeleton").AsGodotObject();
                if (skeleton is null || !skeleton.HasMethod("get_bounds")) return;
                Rect2 bounds=skeleton.Call("get_bounds").As<Rect2>();
                Vector2 size=bounds.Size.Abs()*_sprite.Scale.Abs();
                if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X<1 || size.Y<1) return;
                Vector2 budget=new(Math.Clamp(TargetSize.X*.95f,28,150),Math.Clamp(TargetSize.Y*.7f,22,110));
                float fit=Math.Min(1,Math.Min(budget.X/size.X,budget.Y/size.Y));
                _native.Scale=Vector2.One*fit;
                _native.Position=-(bounds.GetCenter()*_sprite.Scale+_sprite.Position)*fit;
                LastNativeBounds=new Rect2(-size*fit/2,size*fit);
                _native.Visible=true;
                _fitted=true;
            });
        }
        else _fitted=false;
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_fitted) return;
        float alpha=Math.Max(0,1-_age/.55f)*.8f;
        float width=Math.Clamp(TargetSize.X*.6f,22,76),height=Math.Clamp(TargetSize.Y*.12f,5,13);
        Color c=new(.92f,.86f,.69f,alpha);
        for(int i=0;i<3;i++)
        {
            float x=(i-1)*width/3;
            _tooth[0]=new(x-width/7,-height);_tooth[1]=new(x+width/7,-height);_tooth[2]=new(x,0);
            DrawColoredPolygon(_tooth,c);
            _tooth[0]=new(x-width/7,height);_tooth[1]=new(x+width/7,height);_tooth[2]=new(x,1);
            DrawColoredPolygon(_tooth,c);
        }
    }
}

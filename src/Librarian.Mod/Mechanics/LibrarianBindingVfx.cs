using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;

namespace Librarian.Mechanics;

internal sealed record LibrarianBindingObservation(long Token,string Phase,Player Owner,Player Target,decimal ActualGranted,bool Visible,ulong Frame);

/// <summary>Visual occurrences only; the rule-owned Bindings list and its payout stay untouched.</summary>
internal static class LibrarianBindingVfx
{
    private static long _next;
    internal static readonly Dictionary<long,LibrarianBindingTetherVfx> Live = [];
    internal static Action<LibrarianBindingObservation>? Observer;
    internal static void Register(LibrarianSession session,Player target,CardModel source)
        => LibrarianOrbVfx.Safely(() =>
        {
            if (!session.Bindings.Contains((session.Player,target))) return;
            var node=Create(session,session.Player,target);
            if(node is not null) Notify(node,"registered",0);
        });
    internal static long BeginPayout(LibrarianSession session,Player owner,Player target)
    {
        long token=0;
        LibrarianOrbVfx.Safely(() =>
        {
            var node=Live.Values.Where(n=>n.Pending && n.OwnerPlayer==owner && n.TargetPlayer==target
                && n.Session.TryGetTarget(out var current) && ReferenceEquals(current,session))
                .OrderBy(n=>n.Token).FirstOrDefault();
            node ??= Create(session,owner,target);
            if(node is null) return;
            node.Begin(); token=node.Token;
            Notify(node,"payout-start",0);
        });
        return token;
    }
    internal static void CompletePayout(long visualToken,decimal actualGranted)
        => LibrarianOrbVfx.Safely(() =>
        {
            if(!Live.TryGetValue(visualToken,out var node) || !GodotObject.IsInstanceValid(node)) return;
            node.Complete(actualGranted);
            Notify(node,"payout-complete",actualGranted);
        });
    private static LibrarianBindingTetherVfx? Create(LibrarianSession session,Player owner,Player target)
    {
        var room=NCombatRoom.Instance;
        if(TestMode.IsOn || DisplayServer.GetName()=="headless" || room is null || !room.IsInsideTree()
            || Live.Count>=128 || owner.Creature.IsDead || target.Creature.IsDead
            || owner.Creature.CombatState is null || !ReferenceEquals(owner.Creature.CombatState,target.Creature.CombatState)
            || room.GetCreatureNode(owner.Creature)?.IsInsideTree()!=true || room.GetCreatureNode(target.Creature)?.IsInsideTree()!=true) return null;
        var node=new LibrarianBindingTetherVfx
        {
            Name="LibrarianBindingTether",Token=++_next,OwnerPlayer=owner,TargetPlayer=target,
            Session=new(session),Room=room
        };
        try
        {
            room.CombatVfxContainer.AddChild(node);
            if(!node.IsInsideTree()) { node.Free(); return null; }
            Live.Add(node.Token,node);
            return node;
        }
        catch
        {
            Live.Remove(node.Token);
            if(GodotObject.IsInstanceValid(node)) { if(node.IsInsideTree())node.QueueFree();else node.Free(); }
            throw; // Register/BeginPayout contain this inside the cosmetic Safely boundary.
        }
    }
    internal static void Notify(LibrarianBindingTetherVfx node,string phase,decimal value)
        => LibrarianOrbVfx.Safely(()=>Observer?.Invoke(new(node.Token,phase,node.OwnerPlayer,node.TargetPlayer,value,node.Visible,Engine.GetProcessFrames())));
}

/// <summary>One small paper clasp and a restrained relation line; never a second block shield or HUD bar.</summary>
public partial class LibrarianBindingTetherVfx : Node2D
{
    internal long Token { get; init; }
    internal Player OwnerPlayer { get; init; }=null!;
    internal Player TargetPlayer { get; init; }=null!;
    internal WeakReference<LibrarianSession> Session { get; init; }=null!;
    internal NCombatRoom Room { get; init; }=null!;
    internal bool Pending => _state==0;
    private int _state; // 0 pending, 1 actual payout in progress, 2 paid residue, 3 cancelled.
    private float _age,_closingAge;
    private bool _counted;
    private Vector2 _target;
    private float _unit=1;
    private readonly Vector2[] _path=new Vector2[24];
    private readonly Vector2[] _page=new Vector2[4];
    public override void _Ready() { Visible=false; UpdateAnchors(); }
    internal void Begin() { if(_state<2) _state=1; }
    internal void Complete(decimal granted)
    { _state=granted>0?2:3; _closingAge=0; }
    private void Cancel()
    {
        if(_state>=2) return;
        _state=3;_closingAge=0;
        LibrarianBindingVfx.Notify(this,"cancel",0);
    }
    public override void _ExitTree()
    {
        if(_counted) { _counted=false;LibrarianCardVfx050.Exit(); }
        LibrarianBindingVfx.Live.Remove(Token);
        LibrarianBindingVfx.Notify(this,"unbound",0);
    }
    private bool UpdateAnchors()
    {
        if(!ReferenceEquals(Room,NCombatRoom.Instance) || !GodotObject.IsInstanceValid(Room) || !Room.IsInsideTree()) return false;
        var ownNode=Room.GetCreatureNode(OwnerPlayer.Creature);
        var otherNode=Room.GetCreatureNode(TargetPlayer.Creature);
        if(ownNode is null || otherNode is null || !GodotObject.IsInstanceValid(ownNode) || !GodotObject.IsInstanceValid(otherNode)) return false;
        var own=ownNode.Hitbox;var other=otherNode.Hitbox;
        if(own is null || other is null || !GodotObject.IsInstanceValid(own) || !GodotObject.IsInstanceValid(other)
            || !own.IsInsideTree() || !other.IsInsideTree()) return false;
        Vector2 start=LibrarianSpineRuntime.TryGetSocket(OwnerPlayer.Creature,LibrarianSpineSocket.ScreenLeft,out var socket)
            ?socket.GlobalPosition:own.GetGlobalTransform()*(own.Size*new Vector2(.5f,.45f));
        Vector2 end=other.GetGlobalTransform()*(other.Size*new Vector2(.5f,.45f));
        GlobalPosition=start;
        _target=ToLocal(end);
        _unit=own.GetGlobalTransform().Scale.Abs().X/Math.Max(.01f,GetGlobalTransform().Scale.Abs().X);
        return true;
    }
    public override void _Process(double delta)
    {
        _age+=(float)delta;
        bool anchors=UpdateAnchors();
        bool sameSession=Session.TryGetTarget(out var expected) && LibrarianRuntime.TryGet(OwnerPlayer,out var current)
            && ReferenceEquals(expected,current);
        if(_state<2 && (!anchors || !sameSession || OwnerPlayer.Creature.IsDead || TargetPlayer.Creature.IsDead
            || !ReferenceEquals(OwnerPlayer.Creature.CombatState,TargetPlayer.Creature.CombatState)
            || CombatManager.Instance.IsOverOrEnding || !CombatManager.Instance.IsInProgress)) Cancel();
        if(_state>=2) _closingAge+=(float)delta;
        if(_state>=2 && _closingAge>=(_state==2?.25f:.12f)) { QueueFree(); return; }
        bool primary=_state>=2 || !LibrarianBindingVfx.Live.Values.Any(n=>n.Token<Token && n._state<2
            && n.OwnerPlayer==OwnerPlayer && n.TargetPlayer==TargetPlayer);
        bool show=anchors && primary && LibrarianCardVfx050.Available(OwnerPlayer);
        if(show && !_counted && LibrarianCardVfx050.Active>=LibrarianPreferences050.Current.EffectLimit) show=false;
        if(show!=_counted) { if(show)LibrarianCardVfx050.Enter();else LibrarianCardVfx050.Exit();_counted=show; }
        Visible=show;
        QueueRedraw();
    }
    public override void _Draw()
    {
        if(!Visible) return;
        var prefs=LibrarianPreferences050.Current;
        float fade=_state>=2?Math.Clamp(1-_closingAge/(_state==2?.25f:.12f),0,1):Math.Min(1,_age/.12f);
        float alpha=fade*prefs.EffectOpacity/100f;
        Color gold=new(.84f,.72f,.48f,alpha*.75f);
        bool self=OwnerPlayer==TargetPlayer;
        if(!self && !prefs.ReducedMotion)
        {
            float length=_state==2?fade:1;
            for(int i=0;i<_path.Length;i++)
            {float t=i/(float)(_path.Length-1)*length;_path[i]=_target*t+new Vector2(0,-Mathf.Sin(t*Mathf.Pi)*12*_unit);}
            DrawPolyline(_path,new Color(gold,alpha*(_state==1?.45f:.23f)),1.4f*_unit,true);
            Clasp(_path[^1],4*_unit,gold);
        }
        // Self fallback and reduced motion are stationary local clasps, with no long link.
        Clasp(Vector2.Zero,(self?6:4)*_unit,gold);
    }
    private void Clasp(Vector2 p,float size,Color c)
    {
        for(int side=-1;side<=1;side+=2)
        {
            Vector2 centre=p+new Vector2(side*size*.55f,0);
            _page[0]=centre+new Vector2(-size*.48f,-size);_page[1]=centre+new Vector2(size*.48f,-size*.8f);
            _page[2]=centre+new Vector2(size*.48f,size);_page[3]=centre+new Vector2(-size*.48f,size*.8f);
            DrawColoredPolygon(_page,new Color(c,c.A*.65f));
        }
        DrawLine(p+new Vector2(-size,0),p+new Vector2(size,0),c,1.5f*_unit,true);
    }
}

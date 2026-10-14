using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Librarian.Mechanics;

internal enum LibrarianSpineSpellStage { Prepare, Release, Impact, Block, Status }

/// <summary>Bounded, deterministic local magic. Never selects targets or schedules gameplay.</summary>
public partial class LibrarianSpineSpellPulse : Node2D
{
    internal SpellProfile050 Profile { get; init; } = new(SpellElement050.Page, SpellShape050.Pages);
    internal LibrarianSpineSpellStage Stage { get; init; }
    internal Player? EffectOwner { get; init; }
    internal Creature? FollowCreature { get; init; }
    internal LibrarianSpineSocket Socket { get; init; }
    internal long Generation { get; init; }
    internal int EntryTag { get; init; }
    internal bool Major { get; init; }
    internal bool FollowSocket { get; init; }
    internal float Radius { get; init; } = 38;
    internal float Duration { get; init; } = .5f;
    internal ulong CreatedFrame { get; private set; }
    internal ulong SocketFrame { get; private set; }
    internal Vector2 AdoptedGlobalPosition { get; private set; }
    internal float Age { get; private set; }
    private float _fadeOutAt = float.PositiveInfinity;
    private bool _counted;
    private readonly Vector2[] _ribbon = new Vector2[20];
    private readonly Vector2[] _branch = new Vector2[12];
    // Godot copies point data into each draw command; one node can reuse these between shapes.
    private readonly Vector2[] _quad = new Vector2[4];
    private readonly Vector2[] _closedQuad = new Vector2[5];
    private readonly Vector2[] _openTriplet = new Vector2[3];
    private readonly Vector2[] _shield = new Vector2[6];
    private readonly Vector2[] _closedShield = new Vector2[7];

    internal void FadeOut() => _fadeOutAt = Math.Min(_fadeOutAt, Age);

    public override void _EnterTree()
    {
        CreatedFrame = Engine.GetProcessFrames();
        ProcessPriority = 10; // The native SpineSprite has already emitted post-world for this frame.
        LibrarianCardVfx050.Enter();
        _counted = true;
    }
    public override void _ExitTree()
    {
        if (_counted) { _counted = false; LibrarianCardVfx050.Exit(); }
    }
    public override void _Process(double delta)
    {
        Age += (float)delta;
        if (Age >= Duration || Age >= _fadeOutAt + .055f || !LibrarianCardVfx050.Available(EffectOwner))
        { QueueFree(); return; }
        if (FollowSocket && FollowCreature is { } creature)
        {
            if (LibrarianSpineRuntime.TryGetSocket(creature, Socket, out var sample)
                && sample.Generation == Generation && sample.TagBits == EntryTag)
            {
                GlobalPosition = sample.GlobalPosition;
                AdoptedGlobalPosition = sample.GlobalPosition;
                SocketFrame = sample.Frame;
                // Preparation remains centered on the hand; a free release no longer follows it.
            }
            else FadeOut();
        }
        QueueRedraw();
    }
    internal void ObserveOrigin(Vector2 position, ulong socketFrame)
    { AdoptedGlobalPosition = position; SocketFrame = socketFrame; }

    public override void _Draw()
    {
        var preferences = LibrarianPreferences050.Current;
        float t = Math.Clamp(Age / Math.Max(.01f, Duration), 0, 1);
        bool quiet = preferences.ReducedMotion;
        float fade = Stage == LibrarianSpineSpellStage.Prepare
            ? Math.Min(1, Age / .055f) * (1 - t * .35f)
            : Mathf.Pow(1 - t, 1.45f); // The confirmed release/result is visible on its creation frame.
        if (!float.IsPositiveInfinity(_fadeOutAt)) fade *= Math.Clamp(1 - (Age - _fadeOutAt) / .055f, 0, 1);
        fade *= preferences.EffectOpacity / 100f;
        if (fade < .002f) return;
        Color color = LibrarianCardVfx050.Tint(Profile.Element);
        Color light = color.Lightened(.62f);
        float progress = quiet ? .55f : 1 - Mathf.Pow(1 - t, 3);
        float r = Radius;
        Glow(Vector2.Zero, r * (Stage == LibrarianSpineSpellStage.Prepare ? .62f : .8f), color, fade * .65f);

        if (quiet)
        {
            DrawArc(Vector2.Zero, r * .34f, -.6f, 2.1f, 20, Alpha(color, fade * .45f), 1.6f, true);
            DrawArc(Vector2.Zero, r * .29f, 2.5f, 5.6f, 20, Alpha(light, fade * .30f), 1, true);
            return;
        }
        if (Stage == LibrarianSpineSpellStage.Prepare)
        {
            float angle = Age * (Major ? 2.8f : 1.8f);
            float ring = r * (.34f + .07f * Mathf.Sin(t * Mathf.Pi));
            DrawArc(Vector2.Zero, ring, angle, angle + 4.1f, 30, Alpha(color, fade * .72f), 2, true);
            DrawArc(Vector2.Zero, ring * .72f, -angle, -angle + 3.2f, 24, Alpha(light, fade * .6f), 1, true);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.Tau / 3 + angle;
                Paper(Vector2.FromAngle(a) * ring, 2.7f, a + .4f, color, fade * .75f);
            }
            DrawCircle(Vector2.Zero, 2.1f + 1.1f * t, Alpha(light, fade * .65f));
            return;
        }
        if (Stage == LibrarianSpineSpellStage.Block)
        {
            DrawSetTransform(Vector2.Zero, 0, new(1, 1.08f));
            _shield[0] = new(-r * .64f, -r * .35f);
            _shield[1] = new(0, -r * .57f);
            _shield[2] = new(r * .64f, -r * .35f);
            _shield[3] = new(r * .48f, r * .24f);
            _shield[4] = new(0, r * .7f);
            _shield[5] = new(-r * .48f, r * .24f);
            Array.Copy(_shield, _closedShield, _shield.Length);
            _closedShield[6] = _shield[0];
            DrawColoredPolygon(_shield, Alpha(color, fade * .09f));
            DrawPolyline(_closedShield, Alpha(color, fade * .28f), 8, true);
            DrawPolyline(_closedShield, Alpha(light, fade * .9f), 2, true);
            for (int i = 0; i < 3; i++)
                DrawArc(new(0,r*.04f),r*(.27f+i*.16f),-2.7f,-.45f,25,Alpha(color,fade*.45f),1.5f,true);
            DrawSetTransform(Vector2.Zero);
        }
        else if (Major || Profile.Element == SpellElement050.Prismatic || Stage == LibrarianSpineSpellStage.Status)
            Sigil(r, progress, color, light, fade);
        else if (Stage == LibrarianSpineSpellStage.Impact)
            Impact(r, progress, color, light, fade);
        else
            Release(r, progress, color, light, fade);

        if (preferences.Particles)
        {
            int count = Major ? 24 : 14;
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.399963f + (Stage == LibrarianSpineSpellStage.Release ? -.5f : 0);
                Vector2 axis = Vector2.FromAngle(a);
                Vector2 p = axis * r * (.12f + progress * (.5f + i % 5 * .065f));
                if (Stage == LibrarianSpineSpellStage.Release) p.X += progress * r * .30f;
                float opacity = fade * (.48f + i % 3 * .12f) * Math.Clamp((1 - t) * 2, 0, 1);
                if (Profile.Element == SpellElement050.Page || (Major && i % 3 == 0))
                    Paper(p, 1.8f + i % 3 * .75f, a + t * .7f, color, opacity);
                else if (Profile.Element == SpellElement050.Leaf)
                    Leaf(p, 3 + i % 3, a, color, opacity);
                else
                {
                    DrawLine(p - axis * (2 + (1 - t) * 5), p, Alpha(color, opacity * .5f), 3, true);
                    DrawCircle(p, 1.1f + i % 3 * .35f, Alpha(light, opacity));
                }
            }
        }
    }
    private void Release(float r, float p, Color c, Color light, float alpha)
    {
        switch (Profile.Element)
        {
            case SpellElement050.Water:
                Ribbon(r, p, -.35f, c, light, alpha, 7);
                Ribbon(r*.80f, p, .5f, c, light, alpha*.65f, 4);
                break;
            case SpellElement050.Leaf:
                for (int branch=0;branch<3;branch++)
                {
                    for(int i=0;i<_branch.Length;i++)
                    { float t=i/(float)(_branch.Length-1)*p; _branch[i]=new(t*r,Mathf.Sin(t*3.6f+branch)*r*.14f+(branch-1)*t*r*.30f); }
                    DrawPolyline(_branch,Alpha(c,alpha*.28f),6,true);
                    DrawPolyline(_branch,Alpha(light,alpha*.85f),1.8f,true);
                    for(int i=4;i<12;i+=3) Leaf(_branch[i],4.5f,i*.4f+branch,c,alpha*.8f);
                }
                break;
            case SpellElement050.Fire:
                for (int i=0;i<5;i++)
                {
                    float a=(i-2)*.20f;
                    Vector2 dir=Vector2.FromAngle(a), side=dir.Orthogonal();
                    float length=r*(.48f+i%3*.16f)*(.35f+p*.65f);
                    Vector2 origin=side*(i-2)*4;
                    SetQuad(origin-dir*5,origin+dir*length*.35f-side*7,origin+dir*length+side*Mathf.Sin(p*4+i)*7,origin+dir*length*.42f+side*6);
                    DrawColoredPolygon(_quad,Alpha(c,alpha*.32f));
                    Array.Copy(_quad, _openTriplet, _openTriplet.Length);
                    DrawPolyline(_openTriplet,Alpha(light,alpha*.65f),1.5f,true);
                }
                Glow(new(r*p*.3f,0),r*.28f,light,alpha*.7f);
                break;
            default:
                for(int i=0;i<3;i++)
                {
                    float a=(i-1)*.34f;
                    Vector2 dir=Vector2.FromAngle(a),side=dir.Orthogonal();
                    Vector2 center=dir*r*(.14f+p*.58f);
                    DrawLine(center-dir*r*.42f,center,Alpha(c,alpha*.20f),6,true);
                    SetQuad(center-dir*13-side*5,center+dir*17-side*2,center+dir*8+side*5,center-dir*14+side*3);
                    DrawColoredPolygon(_quad,Alpha(light,alpha*.75f));
                    DrawQuadOutline(Alpha(c,alpha*.9f),1.3f);
                    DrawLine(_quad[0],_quad[1],Alpha(Colors.White,alpha*.7f),1,true);
                }
                break;
        }
    }
    private void Impact(float r,float p,Color c,Color light,float a)
    {
        // A concise target bloom consumes an already confirmed native result.
        for(int i=0;i<3;i++)
        {
            float angle=i*2.1f-.7f;
            DrawArc(Vector2.Zero,r*(.20f+p*.62f),angle,angle+.9f,16,Alpha(c,a*.20f),7,true);
            DrawArc(Vector2.Zero,r*(.20f+p*.62f),angle,angle+.9f,16,Alpha(light,a*.75f),1.6f,true);
        }
        if(Profile.Element==SpellElement050.Water)
        { DrawSetTransform(Vector2.Zero,0,new(1,.4f));DrawArc(Vector2.Zero,r*(.3f+p*.6f),0,Mathf.Tau,40,Alpha(light,a*.7f),2,true);DrawSetTransform(Vector2.Zero); }
        else if(Profile.Element==SpellElement050.Leaf)
            for(int i=0;i<4;i++) Leaf(Vector2.FromAngle(i*1.6f)*r*p*.42f,7,i*1.6f,c,a);
        else if(Profile.Element==SpellElement050.Page)
            for(int i=0;i<3;i++) Paper(new((i-1)*r*p*.5f,-r*p*.18f),5.5f,i*.5f,c,a);
        else
            DrawCircle(Vector2.Zero,r*.11f*(1-p),Alpha(light,a*.7f));
    }
    private void Sigil(float r,float p,Color c,Color light,float a)
    {
        float radius=r*(.22f+p*.52f),angle=Age*.9f;
        DrawArc(Vector2.Zero,radius,angle,angle+Mathf.Tau*.8f,48,Alpha(c,a*.22f),8,true);
        DrawArc(Vector2.Zero,radius,angle,angle+Mathf.Tau*.8f,48,Alpha(light,a*.8f),1.8f,true);
        DrawArc(Vector2.Zero,radius*.72f,-angle,-angle+Mathf.Tau*.7f,40,Alpha(c,a*.75f),1.5f,true);
        for(int i=0;i<6;i++)
        {
            float arc=i*Mathf.Tau/6+angle;
            Vector2 axis=Vector2.FromAngle(arc),side=axis.Orthogonal(),point=axis*radius;
            SetQuad(point-axis*6,point+side*3,point+axis*6,point-side*3);
            DrawColoredPolygon(_quad,Alpha(light,a*.75f));
        }
        SetQuad(new(0,-radius*.40f),new(radius*.22f,0),new(0,radius*.40f),new(-radius*.22f,0));
        DrawQuadOutline(Alpha(light,a*.85f),2);
        Glow(Vector2.Zero,r*.4f,c,a*.65f);
    }
    private void Ribbon(float r,float progress,float bend,Color c,Color light,float alpha,float width)
    {
        for(int i=0;i<_ribbon.Length;i++)
        { float t=i/(float)(_ribbon.Length-1); _ribbon[i]=new(t*r*progress,Mathf.Sin(t*Mathf.Pi)*r*bend*progress); }
        DrawPolyline(_ribbon,Alpha(c,alpha*.13f),width*2.4f,true);
        DrawPolyline(_ribbon,Alpha(c,alpha*.58f),width,true);
        DrawPolyline(_ribbon,Alpha(light,alpha*.9f),1.5f,true);
    }
    private void Glow(Vector2 p,float r,Color c,float alpha)
    {
        for(int i=6;i>=1;i--)
            DrawCircle(p,r*i/6,Alpha(c,alpha*.025f*(1+(6-i)*.15f)));
    }
    private void Paper(Vector2 p,float size,float angle,Color c,float opacity)
    {
        Vector2 a=Vector2.FromAngle(angle)*size,b=a.Orthogonal()*1.4f;
        SetQuad(p-a-b,p+a-b,p+a+b,p-a+b);
        DrawColoredPolygon(_quad,Alpha(c.Lightened(.55f),opacity*.75f));
        DrawQuadOutline(Alpha(c,opacity),.8f);
    }
    private void Leaf(Vector2 p,float size,float angle,Color c,float opacity)
    {
        Vector2 a=Vector2.FromAngle(angle)*size,b=a.Orthogonal()*.46f;
        SetQuad(p-a,p+b,p+a,p-b);
        DrawColoredPolygon(_quad,Alpha(c,opacity*.8f));
        DrawLine(p-a,p+a,Alpha(c.Lightened(.55f),opacity),.8f,true);
    }
    private void SetQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    { _quad[0] = a; _quad[1] = b; _quad[2] = c; _quad[3] = d; }
    private void DrawQuadOutline(Color color, float width)
    {
        Array.Copy(_quad, _closedQuad, _quad.Length);
        _closedQuad[4] = _quad[0];
        DrawPolyline(_closedQuad, color, width, true);
    }
    private static Color Alpha(Color c,float a) => new(c.R,c.G,c.B,a);
}

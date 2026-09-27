using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Librarian.Mechanics;

/// <summary>Original layered animation; all transforms are cosmetic and interruptions blend from the current pose.</summary>
public partial class LibrarianCharacterRig050 : Node
{
    private Node2D _visual = null!;
    private readonly Dictionary<string, Sprite2D> _parts = new();
    private readonly Dictionary<string, Vector2> _scales = new();
    private readonly Dictionary<string, Polygon2D> _ribbons = new();
    private ShaderMaterial? _eye;
    private string _state = "Idle";
    private float _age, _clock, _unit;
    private bool _dead;
    internal string PerformanceState => _state;
    internal const string AtlasPath = "res://Librarian/images/character/v0.5.0/character_atlas.png";
    private static readonly Dictionary<string, (Rect2 Region, float Height, Vector2 Position)> Layout = new()
    {
        ["HaloLeft"] = (new(103,426,228,406),155,new(-43,-211)),
        ["HaloRight"] = (new(560,424,219,404),155,new(45,-211)),
        ["RibbonLeft"] = (new(83,848,208,341),101,new(-22,-58)),
        ["RibbonRight"] = (new(553,853,205,339),101,new(24,-57)),
        ["Book"] = (new(440,55,382,320),102,new(0,-124)),
        ["ClosedBook"] = (new(869,125,326,272),68,new(1,-35)),
        ["Pages"] = (new(905,468,295,332),68,new(3,-167)),
        ["Mask"] = (new(109,34,241,370),126,new(0,-216)),
        ["FallenMask"] = (new(930,919,276,285),67,new(38,-29))
    };
    internal void Initialize(Node2D visual, float height)
    {
        _visual=visual; _unit=height/280;
        var atlas=GD.Load<Texture2D>(AtlasPath);
        visual.GetNode<Sprite2D>("Body").Visible=false;
        foreach(var (name,info) in Layout)
        {
            var texture=new AtlasTexture{Atlas=atlas,Region=info.Region,FilterClip=true};
            var sprite=new Sprite2D{Name=name,Texture=texture,Position=info.Position*_unit,Scale=Vector2.One*(info.Height*_unit/texture.GetHeight())};
            visual.AddChild(sprite); _parts.Add(name,sprite);_scales.Add(name,sprite.Scale);
            if (name.StartsWith("Ribbon"))
            {
                var strip = new Polygon2D { Name=name+"Deform",Texture=atlas };
                var uv=new Vector2[22];var vertices=new Vector2[22];
                var faces=new Godot.Collections.Array();
                for(int i=0;i<=10;i++)
                {
                    float y=i/10f*info.Region.Size.Y;
                    uv[i*2]=info.Region.Position+new Vector2(0,y);uv[i*2+1]=info.Region.Position+new Vector2(info.Region.Size.X,y);
                    vertices[i*2]=new(-info.Region.Size.X/2,y-info.Region.Size.Y/2);vertices[i*2+1]=new(info.Region.Size.X/2,y-info.Region.Size.Y/2);
                    if(i<10)faces.Add(new int[]{i*2,i*2+1,i*2+3,i*2+2});
                }
                strip.Polygon=vertices;strip.UV=uv;strip.Polygons=faces;visual.AddChild(strip);_ribbons.Add(name,strip);sprite.Visible=false;
            }
        }
        _eye=new ShaderMaterial{Shader=new Shader{Code="shader_type canvas_item; uniform float eye_power = 1.0; void fragment(){ vec4 c=texture(TEXTURE,UV)*COLOR; float cyan=smoothstep(0.15,0.5,min(c.g,c.b)-c.r); c.rgb=mix(c.rgb,c.rgb*eye_power,cyan); COLOR=c;}"}};
        _parts["Mask"].Material=_eye;
        foreach(string name in new[]{"ClosedBook","FallenMask","Pages"})_parts[name].Modulate=Colors.Transparent;
        foreach(string name in new[]{"RightHand","LeftHand"})
        {var hand=visual.GetNode<Sprite2D>(name);visual.MoveChild(hand,-1);_parts.Add(name,hand);_scales.Add(name,hand.Scale);}
    }
    internal static string StateFor(string name)=>name.ToLowerInvariant() switch
    {"attack"=>"Attack","cast" or "skill"=>"Skill","power"=>"Power","hit" or "hurt"=>"Hit","dead" or "die"=>"Dead","revive"=>"Revive",_=>"Idle"};
    internal void Begin(string state)
    {
        if(_dead&&state is not "Revive" and not "Dead")return;
        if(_state==state&&_age<0.12f&&state!="Idle")return;
        // Native attack notifications must not replace a more specific skill/power preparation.
        if(state=="Attack"&&_state is "Skill" or "Power"&&_age<0.2f)return;
        _state=state;_age=0;
        if(state=="Dead")_dead=true;
        if(state=="Revive")_dead=false;
        if(state is "Hit" or "Dead")foreach(string hand in new[]{"LeftHand","RightHand"})_parts[hand].GetNode<LibrarianHandGlow>("SpellGlow").Clear();
    }
    internal void Cast(CardType type)
    {
        if(_dead||LibrarianPreferences050.Current.ReducedMotion)return;
        Begin(type switch{CardType.Attack=>"Attack",CardType.Power=>"Power",_=>"Skill"});
        var tint=new Color(type==CardType.Attack?"ffd88a":type==CardType.Skill?"b8edff":"ddd0ff");
        if(type is CardType.Attack or CardType.Power)_parts["LeftHand"].GetNode<LibrarianHandGlow>("SpellGlow").Pulse(tint);
        if(type is CardType.Skill or CardType.Power)_parts["RightHand"].GetNode<LibrarianHandGlow>("SpellGlow").Pulse(tint);
    }
    private void Pose(string name,Vector2 position,float angle,float dt,Vector2? scale=null,float alpha=1,Color? tint=null)
    {
        var part=_parts[name];float blend=1-Mathf.Exp(-dt*24);
        part.Position=part.Position.Lerp(position*_unit,blend);
        part.Rotation=Mathf.LerpAngle(part.Rotation,angle,blend);
        part.Scale=part.Scale.Lerp(_scales[name]*(scale??Vector2.One),blend);
        var color=tint??Colors.White;color.A=alpha;part.Modulate=part.Modulate.Lerp(color,blend);
    }
    private static float Smooth(float a,float b,float v){float t=Math.Clamp((v-a)/(b-a),0,1);return t*t*(3-2*t);}
    private static float Beat(float t,float prepare,float peak,float end)
        =>t<prepare?-0.2f*Smooth(0,prepare,t):t<peak?Mathf.Lerp(-0.2f,1,Smooth(prepare,peak,t)):1-Smooth(peak,end,t);
    public override void _Process(double delta)
    {
        if(_visual is null)return;
        float dt=(float)delta;_age+=dt;_clock+=dt;
        if(_state is not "Dead" and not "Idle"&&_age>1.35f){_state="Idle";_age=0;}
        bool reduced=LibrarianPreferences050.Current.ReducedMotion;
        float t=_age;bool dying=_state=="Dead";
        float fall=dying?Smooth(0.16f,1.22f,t):_state=="Revive"?1-Smooth(0.05f,1.05f,t):0;
        float bookFall=dying?Smooth(0.09f,0.88f,t):fall;
        float haloFall=dying?Smooth(0.32f,1.48f,t):fall;
        float maskFall=dying?Smooth(0.24f,1.08f,t):fall;
        if(reduced&&dying)fall=bookFall=haloFall=maskFall=1;
        float attack=!reduced&&_state=="Attack"?Beat(t,0.12f,0.25f,0.70f):0;
        float skill=!reduced&&_state=="Skill"?Beat(t,0.18f,0.38f,0.84f):0;
        float power=!reduced&&_state=="Power"?Beat(t,0.22f,0.48f,1.02f):0;
        float hit=_state=="Hit"?Mathf.Sin(Math.Min(1,t/0.44f)*Mathf.Pi)*Mathf.Exp(-t*3):0;
        float breath=reduced||fall>0.01f?0:Mathf.Sin(_clock*1.1f)*2.8f;
        var offset=new Vector2(attack*7-hit*11,breath-skill*4-power*8+hit*3);
        _visual.Rotation=0;_visual.Position=Vector2.Zero;_visual.Modulate=Colors.White;
        var shade=Colors.White.Lerp(new Color("737b82"),fall*0.4f);
        Vector2 mask=new Vector2(0,-216)+offset+new Vector2(attack*5-hit*4,-power*4);
        Pose("Mask",mask.Lerp(new(36,-31),maskFall),attack*0.055f-hit*0.08f+maskFall*0.30f,dt,alpha:1-Smooth(0.40f,0.75f,maskFall),tint:shade);
        Pose("FallenMask",new(36,-31),-0.05f,dt,alpha:Smooth(0.40f,0.75f,maskFall),tint:shade);
        Pose("Book",(new Vector2(0,-124)+offset).Lerp(new(0,-34),bookFall),attack*0.05f-hit*0.045f,dt,new(1+skill*0.035f+power*0.07f,1-bookFall*0.2f),1-Smooth(0.28f,0.72f,bookFall),shade);
        Pose("ClosedBook",new(1,-35),0,dt,alpha:Smooth(0.28f,0.72f,bookFall),tint:shade);
        float flutter=reduced?0:Mathf.Sin(_clock*1.5f)*0.025f;
        Pose("HaloLeft",(new Vector2(-43-power*7,-211)+offset*0.8f).Lerp(new(-55,-30),haloFall),-flutter-power*0.10f-haloFall*0.65f,dt,new(1,1-haloFall*0.5f),tint:shade);
        Pose("HaloRight",(new Vector2(45+power*7,-211)+offset*0.8f).Lerp(new(66,-28),haloFall),flutter+power*0.10f+haloFall*0.65f,dt,new(1,1-haloFall*0.5f),tint:shade);
        float ribbon=reduced?0:Mathf.Sin(_clock*1.2f-0.7f)*0.055f;
        Pose("RibbonLeft",(new Vector2(-22,-58)+offset*0.65f).Lerp(new(-25,-7),fall),ribbon-attack*0.10f+fall*0.35f,dt,new(1+fall*0.15f,1-fall*0.83f),tint:shade);
        Pose("RibbonRight",(new Vector2(24,-57)+offset*0.65f).Lerp(new(25,-6),fall),-ribbon+0.02f-skill*0.07f-fall*0.25f,dt,new(1+fall*0.2f,1-fall*0.83f),tint:shade);
        float pageBeat=Math.Max(0,Math.Max(attack*0.5f,Math.Max(skill,power)));
        float deathPages=dying?(1-Smooth(0.6f,1.45f,t))*Smooth(0.05f,0.3f,t):0;
        Pose("Pages",new Vector2(5,-164-pageBeat*10+deathPages*55)+offset,-0.12f+pageBeat*0.30f,dt,new(0.7f+pageBeat*0.4f,0.6f+pageBeat*0.5f),Math.Max(pageBeat*0.85f,deathPages*0.8f));
        var right=new Vector2(-78-skill*8-power*11,-140-skill*21-power*13)+offset*0.4f;
        var left=new Vector2(78+attack*28+power*11,-137-attack*8-power*13)+offset*0.4f;
        Pose("RightHand",right.Lerp(new(-84,-18),fall),skill*0.16f-power*0.12f-fall*0.7f,dt,tint:shade);
        Pose("LeftHand",left.Lerp(new(91,-15),fall),-attack*0.24f+power*0.12f+fall*0.65f,dt,tint:shade);
        _eye?.SetShaderParameter("eye_power", (0.94f+Math.Max(0,power)*0.25f+(reduced?0:Mathf.Sin(_clock*1.4f)*0.06f))*(dying?1-Smooth(0.02f,0.42f,t):1));
        foreach(var (name,strip) in _ribbons)
        {
            var sprite=_parts[name];var region=Layout[name].Region;var points=new Vector2[22];
            for(int i=0;i<=10;i++)
            {
                float p=i/10f;float sway=reduced?0:Mathf.Sin(_clock*1.7f-p*2.5f+(name=="RibbonLeft"?0:1.8f))*9*p*p*(1-fall);
                float y=p*region.Size.Y-region.Size.Y/2;
                points[i*2]=new(-region.Size.X/2+sway,y);points[i*2+1]=new(region.Size.X/2+sway,y);
            }
            strip.Polygon=points;strip.Position=sprite.Position;strip.Rotation=sprite.Rotation;strip.Scale=sprite.Scale;strip.Modulate=sprite.Modulate;
        }
    }
}

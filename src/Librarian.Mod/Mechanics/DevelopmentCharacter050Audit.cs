using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Librarian.Mechanics;

internal static class DevelopmentCharacter050Audit
{
    internal static async Task Run(Player player)
    {
        await DevelopmentRevision050Audit.Wait(2);
        var creature=NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        var motion=creature.Visuals.GetNode<LibrarianCharacterMotion>("Motion");
        var rig=motion.Rig;
        var visual=motion.GetParent().GetNode<Node2D>(motion.VisualPath);
        DevelopmentRevision050Audit.Check(rig is not null,"layered body mounted in native combat");
        string[] names={"Mask","Book","Pages","HaloLeft","HaloRight","RibbonLeft","RibbonRight","ClosedBook","FallenMask","RightHand","LeftHand"};
        DevelopmentRevision050Audit.Check(names.All(name=>visual.GetNodeOrNull<Sprite2D>(name)?.Texture is not null),"eleven complete textured parts");
        DevelopmentRevision050Audit.Check(!visual.GetNode<Sprite2D>("Body").Visible,"old flattened body hidden");
        var window=NGame.Instance!.GetWindow();var oldSize=window.Size;
        foreach(var resolution in new[]{new Vector2I(1280,720),new Vector2I(1920,1080)})
        {
            window.Size=resolution;await DevelopmentRevision050Audit.Wait(0.5);
            await DevelopmentRevision050Audit.Capture($"character-idle-{resolution.X}");
        }
        window.Size=oldSize;
        var mask=visual.GetNode<Sprite2D>("Mask");var halo=visual.GetNode<Sprite2D>("HaloLeft");
        var maskBefore=mask.Position;var haloBefore=halo.Rotation;
        // Sampling two points around a sine-wave extremum can report no motion.
        // Observe a window so this check is independent of loading/resize timing.
        float maskTravel=0,haloTravel=0;
        for(int i=0;i<12;i++)
        {
            await DevelopmentRevision050Audit.Wait(0.15);
            maskTravel=Math.Max(maskTravel,mask.Position.DistanceTo(maskBefore));
            haloTravel=Math.Max(haloTravel,Math.Abs(halo.Rotation-haloBefore));
        }
        Librarian.LibrarianCode.MainFile.Logger.Info($"V050_IDLE_SAMPLES maskTravel={maskTravel} haloTravel={haloTravel}");
        DevelopmentRevision050Audit.Check(maskTravel>0.1f&&haloTravel>0.001f,"independent idle float and halo follow-through");
        foreach(var type in new[]{CardType.Attack,CardType.Skill,CardType.Power})
        {
            motion.PlayCardGesture(type);
            await DevelopmentRevision050Audit.Wait(type==CardType.Power?0.43:0.3);
            DevelopmentRevision050Audit.Check(visual.GetNode<Sprite2D>("Pages").Modulate.A>0.1f,"page opening on "+type);
            await DevelopmentRevision050Audit.Capture("character-"+type);
            await DevelopmentRevision050Audit.Wait(1.2);
        }
        motion.PlayCardGesture(CardType.Power);await DevelopmentRevision050Audit.Wait(0.25);
        var hand=visual.GetNode<Sprite2D>("LeftHand");var position=hand.Position;
        motion.PlayCardGesture(CardType.Attack);
        DevelopmentRevision050Audit.Check(hand.Position.DistanceTo(position)<0.001f,"rapid action changes do not snap current pose");
        await DevelopmentRevision050Audit.Wait(0.1);
        creature.SetAnimationTrigger("Hit");await DevelopmentRevision050Audit.Wait(0.12);
        DevelopmentRevision050Audit.Check(rig!.PerformanceState=="Hit","native Hit interrupts cast");
        await DevelopmentRevision050Audit.Capture("character-hit");await DevelopmentRevision050Audit.Wait(0.6);
        var oldTask=creature.DeathAnimationTask;
        try
        {
            float duration=creature.StartDeathAnim(shouldRemove:false);
            DevelopmentRevision050Audit.Check(Math.Abs(duration-1.65f)<0.01f,"native death duration reports full staged animation");
            await DevelopmentRevision050Audit.Wait(0.45);await DevelopmentRevision050Audit.Capture("character-death-mid");
            await DevelopmentRevision050Audit.Wait(1.35);
            DevelopmentRevision050Audit.Check(rig.PerformanceState=="Dead"&&visual.Rotation==0,"death holds parts on ground without whole-body rotation");
            DevelopmentRevision050Audit.Check(visual.GetNode<Sprite2D>("ClosedBook").Modulate.A>0.99f&&visual.GetNode<Sprite2D>("FallenMask").Modulate.A>0.99f,"closed book and powerless mask hold as remains");
            DevelopmentRevision050Audit.Check(mask.Modulate.A<0.01f&&visual.GetNode<Sprite2D>("Book").Modulate.A<0.01f,"standing parts fully disappear on death");
            await DevelopmentRevision050Audit.Capture("character-dead");
            creature.StartReviveAnim();await DevelopmentRevision050Audit.Wait(0.6);await DevelopmentRevision050Audit.Capture("character-revive-mid");
            await DevelopmentRevision050Audit.Wait(0.95);
            DevelopmentRevision050Audit.Check(mask.Modulate.A>0.99f&&visual.GetNode<Sprite2D>("FallenMask").Modulate.A<0.01f,"native revival restores standing layered body");
            await DevelopmentRevision050Audit.Capture("character-revived");
        }
        finally{creature.DeathAnimationTask=oldTask;creature.StartReviveAnim();}
        await DevelopmentRevision050Audit.Wait(1.5);
        LibrarianPreferences050.Current.ReducedMotion=true;await DevelopmentRevision050Audit.Wait(0.5);
        maskBefore=mask.Position;await DevelopmentRevision050Audit.Wait(0.4);
        DevelopmentRevision050Audit.Check(mask.Position.DistanceTo(maskBefore)<0.05f,"reduced motion stops idle float");
        LibrarianPreferences050.Current.ReducedMotion=false;
        var scale=creature.Scale;creature.Scale=Vector2.One*0.65f;
        await DevelopmentRevision050Audit.Wait(0.2);await DevelopmentRevision050Audit.Capture("character-scaled-065");creature.Scale=scale;
        Librarian.LibrarianCode.MainFile.Logger.Info("V050_CHARACTER_AUDIT_PASS nativeHit=True nativeDeath=True nativeRevive=True resolutions=1280x720,1920x1080");
    }
}

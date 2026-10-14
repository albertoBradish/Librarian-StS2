using Godot;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.Stateful;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

internal enum LibrarianSpineSocket { ScreenLeft, ScreenRight, Book }
internal readonly record struct LibrarianSpineSocketSample(Vector2 GlobalPosition, Vector2 GlobalDirection,
    long Generation, int TagBits, ulong Frame);
internal sealed record LibrarianSpineMotionProfile(string Semantic, LibrarianSpineSocket Socket);
internal sealed record LibrarianSpineVisualCue(Creature Creature, CardPlay Play, LibrarianSpineMotionProfile Profile,
    LibrarianSpineCueStage Stage, long Generation, int TagBits, long OccurrenceId,
    LibrarianSpineSettings Settings, LibrarianSpineSocketSample? Socket);

/// <summary>Explicit primary gestures from the implemented OnPlay operations. They never imply a result.</summary>
internal static class LibrarianSpineMotionProfiles
{
    private static readonly LibrarianSpineMotionProfile Attack = new("attack", LibrarianSpineSocket.ScreenRight);
    private static readonly LibrarianSpineMotionProfile Cast = new("cast", LibrarianSpineSocket.ScreenLeft);
    private static readonly LibrarianSpineMotionProfile Guard = new("cast_guard", LibrarianSpineSocket.ScreenLeft);
    private static readonly LibrarianSpineMotionProfile Draw = new("cast_draw", LibrarianSpineSocket.ScreenLeft);
    private static readonly LibrarianSpineMotionProfile Transfer = new("cast_transfer", LibrarianSpineSocket.ScreenLeft);
    private static readonly LibrarianSpineMotionProfile Power = new("power", LibrarianSpineSocket.Book);

    internal static LibrarianSpineMotionProfile? Resolve(CardModel card) => card switch
    {
        // Immediate block is the primary operation. Conditional block (ResidualWarmth) stays generic.
        LibrarianDefend or QuietEmbers or SproutingSeed or VineShield or Cooldown or SproutingBulwark
            or ArchiveBulwark or SharedShelter => Guard,
        // Draw/reading is primary; secondary orb gains/losses do not restart the body.
        Nourish or ChannelFlow or DryBranchSearch or Calibrate or OutOfContext or ZeroSearch
            or CirculationNotes => Draw,
        // Chosen value consolidation and explicit draw-pile movement, never a guessed random destination.
        ThreefoldUnity or LonelyScroll or DrawBranch or Transcribe or ReadBackward => Transfer,
        _ => card.Type switch
        {
            CardType.Attack => Attack,
            CardType.Skill => Cast,
            CardType.Power => Power,
            _ => null
        }
    };

    internal static string BoneName(LibrarianSpineSocket socket) => socket switch
    {
        LibrarianSpineSocket.ScreenLeft => "spell_screen_left",
        LibrarianSpineSocket.ScreenRight => "spell_screen_right",
        _ => "spell_book"
    };
}

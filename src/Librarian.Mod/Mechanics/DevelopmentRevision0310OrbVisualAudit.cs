using MegaCrit.Sts2.Core.Entities.Players;
namespace Librarian.Mechanics;
// Historical reference-strip fixture is archived under .research; keep existing callers compilable.
internal static class DevelopmentRevision0310OrbVisualAudit
{
    internal static Task Run(Player player) => DevelopmentRevision0311OrbVisualAudit.Run(player);
}

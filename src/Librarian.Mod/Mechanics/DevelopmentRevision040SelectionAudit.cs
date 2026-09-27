using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Librarian.Mechanics;

// The V0.5.0 user reset supersedes the historical whole-painting drift assertions.
internal static class DevelopmentRevision040SelectionAudit
{
    internal static Task Capture(NCharacterSelectScreen screen)
        => DevelopmentSelection050Audit.Run(screen);
}

using Librarian.Core;
namespace Librarian.Mechanics;

internal static class LibrarianChannelEvents
{
    internal static bool IsSuccessfulChannel(LibrarianSession session, OrbEvent change)
        => change.Kind == OrbEventKind.Imbued;
}

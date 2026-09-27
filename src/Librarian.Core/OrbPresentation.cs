using System.Globalization;

namespace Librarian.Core;

/// <summary>Read-only formatting; a large finite lock is never converted to permanent state.</summary>
public static class OrbPresentation
{
    public static string CenterText(OrbView orb) => !orb.IsLocked
        ? orb.Value.ToString(CultureInfo.InvariantCulture)
        : orb.IsPermanentlyLocked || orb.LockedTurns > 99 ? "∞" : orb.LockedTurns.ToString(CultureInfo.InvariantCulture);
}

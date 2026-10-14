using Godot;

namespace Librarian.Mechanics;

internal static class LibrarianNativeAnimation
{
    internal static bool HasAnimationPlayer(Node root) => root is AnimationPlayer
        || root.GetChildren().Any(HasAnimationPlayer);

    // Used only by preserved historical audit fixtures; requires no foreign assembly reference.
    internal static bool HasBaseLibAncestor(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
            if (current.FullName == "BaseLib.Abstracts.CustomCardModel") return true;
        return false;
    }
}

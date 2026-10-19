using Godot;

namespace Librarian.Mechanics;

public partial class LibrarianSelection138Glimmer : Node2D
{
    internal LibrarianCharacterSelect138 OwnerController { get; set; } = null!;
    internal double Time { get; set; }
    private static readonly Vector2[] Stars = [new(115, 163), new(259, 257), new(459, 36), new(1514, 64), new(1401, 119), new(1481, 211), new(497, 759)];

    public override void _Draw()
    {
        var p = LibrarianPreferences050.Current;
        if (p.ReducedMotion || !p.OrbIdle) return;
        float opacity = p.EffectOpacity / 100f;
        if (p.Particles)
            for (int i = 0; i < Stars.Length; i++)
            {
                float alpha = (.045f + .055f * (float)Math.Sin(Time * .8 + i * 2.3)) * opacity;
                if (alpha <= 0) continue;
                DrawCircle(Stars[i], 7, new Color(.96f, .77f, .43f, alpha * .2f));
                DrawCircle(Stars[i], 2.1f, new Color(1, .84f, .56f, alpha));
            }
        // Compact eye light follows its actual authored socket, rather than a
        // fixed screen coordinate while the mask moves.
        Vector2 eye = ToLocal(OwnerController.BonePosition("eye_anchor"));
        float pulse = (.035f + .02f * (float)Math.Sin(Time * Math.Tau / 5)) * opacity;
        DrawCircle(eye, 26, new Color(.25f, .86f, .9f, pulse * .25f));
        DrawCircle(eye, 13, new Color(.45f, .95f, 1, pulse));
    }
}

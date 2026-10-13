using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace AutoShopper.Windows.Components;

// A short line that fades out a moment after a click, such as a copy, reports what it did. Keep it in a mutable field:
// a copy shows into itself.
internal struct TransientNotice
{
    private const int ShowMs = 2400;
    private const float FadeMs = 400f;

    private string? text;
    private Vector4 color;
    private long shownAtMs;

    public void Show(string message, Vector4 tint)
    {
        text = message;
        color = tint;
        shownAtMs = Environment.TickCount64;
    }

    public void Clear() => text = null;

    // The color carries the fade in its alpha.
    public bool TryGet(out string message, out Vector4 tint)
    {
        var age = Environment.TickCount64 - shownAtMs;
        if (text is null || age > ShowMs)
        {
            text = null;
            message = string.Empty;
            tint = default;
            return false;
        }

        message = text;
        tint = Styling.WithAlpha(color, Math.Clamp((ShowMs - age) / FadeMs, 0f, 1f));
        return true;
    }

    // Draws in the caption font, its right edge at rightX and centred on midY, cut to maxWidth. Returns the width used.
    public float DrawRight(float rightX, float midY, float maxWidth)
    {
        if (!TryGet(out var message, out var tint) || maxWidth <= 0f)
        {
            return 0f;
        }

        using (Fonts.PushCaption())
        {
            var shown = TextDraw.Truncate(message, maxWidth);
            var size = TextDraw.Measure(shown);
            TextDraw.At(shown, new Vector2(rightX - size.X, midY - size.Y * 0.5f), tint);
            return size.X;
        }
    }

    // Takes a caption line of the layout while it shows, so what follows moves down only then.
    public void DrawLine(float width)
    {
        if (!TryGet(out var message, out var tint))
        {
            return;
        }

        Styling.VSpace(6f);
        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(message, width), ImGui.GetCursorScreenPos(), tint);
            ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight()));
        }
    }
}

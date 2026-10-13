using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Components;

internal static class ActionCard
{
    private const float PadX = 20f;
    private const float PadY = 16f;
    private const float LineGap = 6f;
    private const float ButtonGap = 8f;
    private const float CardGap = 14f;

    internal struct Card
    {
        public Vector2 Origin;
        public float Width;
        public float Height;
        public float ButtonX;
        public float ButtonY;
    }

    public static Card Begin(string title, string body, string? warning, string? facts, Vector4 accent, int buttons, FontAwesomeIcon icon = FontAwesomeIcon.ExclamationTriangle)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = PadX * scale;
        var padY = PadY * scale;
        var iconWidth = TextDraw.IconSize(FontAwesomeIcon.ExclamationTriangle).X + 10f * scale;
        var wrapWidth = MathF.Max(1f, width - padX * 2f - iconWidth);
        var titleHeight = ImGui.GetTextLineHeight();
        var bodyHeight = TextDraw.MeasureWrapped(body, wrapWidth).Y;
        var factsHeight = facts is null ? 0f : TextDraw.MeasureWrapped(facts, wrapWidth).Y + LineGap * scale;
        var warningHeight = warning is null ? 0f : TextDraw.MeasureWrapped(warning, wrapWidth).Y + LineGap * scale;
        var buttonsHeight = buttons > 0 ? Layout.ActionPillHeight * scale + LineGap * 2f * scale : 0f;
        var height = padY * 2f + titleHeight + LineGap * scale + bodyHeight + factsHeight + warningHeight + buttonsHeight;
        var end = origin + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.Tint(Styling.WithAlpha(Styling.Surface1, 0.6f), accent, 0.10f), Styling.WithAlpha(accent, 0.45f), topLight: false);
        TextDraw.Icon(icon, new Vector2(origin.X + padX, origin.Y + padY), accent);

        var x = origin.X + padX + iconWidth;
        var y = origin.Y + padY;
        TextDraw.At(TextDraw.Truncate(title, wrapWidth), new Vector2(x, y), Styling.TextStrong);
        y += titleHeight + LineGap * scale;
        TextDraw.Wrapped(body, new Vector2(x, y), wrapWidth, Styling.TextSecondary);
        y += bodyHeight;
        if (facts is not null)
        {
            y += LineGap * scale;
            TextDraw.Wrapped(facts, new Vector2(x, y), wrapWidth, Styling.TextStrong);
            y += factsHeight - LineGap * scale;
        }

        if (warning is not null)
        {
            y += LineGap * scale;
            TextDraw.Wrapped(warning, new Vector2(x, y), wrapWidth, Styling.AccentRoseSoft);
            y += warningHeight - LineGap * scale;
        }

        return new Card
        {
            Origin = origin,
            Width = width,
            Height = height,
            ButtonX = x,
            ButtonY = y + LineGap * 2f * scale,
        };
    }

    public static bool Button(ref Card card, string id, string label, Vector4 accent, PillButton.Emphasis emphasis, bool enabled)
    {
        ImGui.SetCursorScreenPos(new Vector2(card.ButtonX, card.ButtonY));
        var clicked = PillButton.Draw(id, label, accent, emphasis, null, enabled, Layout.ActionPillHeight);
        card.ButtonX += PillButton.Width(label) + ButtonGap * ImGuiHelpers.GlobalScale;
        return clicked && enabled;
    }

    public static void End(in Card card)
    {
        ImGui.SetCursorScreenPos(card.Origin);
        ImGui.Dummy(new Vector2(card.Width, card.Height));
        Styling.VSpace(CardGap);
    }
}

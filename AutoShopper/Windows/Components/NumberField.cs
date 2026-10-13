using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoShopper.Windows.Components;

// A whole-number text field in the stepper's frame. The caller keeps the text between frames and parses it, so a
// value is formatted only when it changes.
internal static class NumberField
{
    private const float PadX = 9f;

    public static bool Draw(string id, ref string text, string hint, float width, int maxLength, string? tooltip = null, bool takeFocus = false)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = ImGui.GetFrameHeight();
        var size = new Vector2(width * scale, height);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var rounding = height * 0.5f;
        Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Surface0, 0.9f), rounding);

        var padX = PadX * scale;
        ImGui.SetCursorScreenPos(origin + new Vector2(padX, 0f));
        ImGui.SetNextItemWidth(MathF.Max(1f, size.X - padX * 2f));
        bool changed;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero)
            .Push(ImGuiCol.FrameBgHovered, Vector4.Zero)
            .Push(ImGuiCol.FrameBgActive, Vector4.Zero)
            .Push(ImGuiCol.Text, Styling.TextStrong)
            .Push(ImGuiCol.TextDisabled, Styling.TextMuted))
        {
            if (takeFocus)
            {
                ImGui.SetKeyboardFocusHere();
            }

            changed = ImGui.InputTextWithHint(id, hint, ref text, maxLength, ImGuiInputTextFlags.CharsDecimal);
        }

        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        var focus = Motion.Approach(Motion.Key(id, 1), active ? 1f : 0f, 16f);
        var border = Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, hovered ? 0.85f : 0.6f), Styling.WithAlpha(Styling.AccentGlowSoft, 0.85f), focus);
        Paint.Stroke(drawList, origin, end, border, rounding);

        if (tooltip is not null && hovered && !active)
        {
            Tooltip.Show(tooltip);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
        return changed;
    }
}

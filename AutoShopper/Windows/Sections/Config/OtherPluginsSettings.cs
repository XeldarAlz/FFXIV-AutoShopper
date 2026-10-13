using AutoShopper.Core.Api;
using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Sections.Config;

internal static class OtherPluginsSettings
{
    private const float BadgeGap = 8f;
    private const string ActionId = "##ashop_api_caller";

    private static IReadOnlyList<CallerConsent> rows = [];
    private static int rowIndex;
    private static int clickedIndex = -1;

    public static void Draw(BuyingApi api)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Api.Group));
        rows = api.Consents.Answers;
        if (rows.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Api.GroupNone));
            return;
        }

        clickedIndex = -1;
        for (var index = 0; index < rows.Count; index++)
        {
            var answer = rows[index];
            rowIndex = index;
            SettingsRow.Draw(answer.DisplayName,
                Loc.T(answer.Allowed ? L.Api.AllowedHelp : L.Api.DeniedHelp),
                ControlWidth(answer) / ImGuiHelpers.GlobalScale,
                DrawControl,
                Layout.ActionPillHeight);
        }

        if (clickedIndex < 0)
        {
            return;
        }

        var clicked = rows[clickedIndex];
        if (clicked.Allowed)
        {
            api.Revoke(clicked.Caller);
            return;
        }

        api.Allow(clicked.Caller, clicked.DisplayName);
    }

    private static void DrawControl()
    {
        var answer = rows[rowIndex];
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var midY = origin.Y + Layout.ActionPillHeight * scale * 0.5f;
        var badgeWidth = Badge.DrawLeft(ImGui.GetWindowDrawList(), StateLabel(answer), answer.Allowed ? Styling.AccentMint : Styling.AccentRose, origin.X, midY);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + badgeWidth + BadgeGap * scale, origin.Y));
        ImGui.PushID(rowIndex);
        var clicked = PillButton.Draw(ActionId, ActionLabel(answer), answer.Allowed ? Styling.AccentRose : Styling.AccentMint, PillButton.Emphasis.Tinted, null, true, Layout.ActionPillHeight);
        ImGui.PopID();
        if (clicked)
        {
            clickedIndex = rowIndex;
        }
    }

    private static float ControlWidth(CallerConsent answer)
        => Badge.Width(StateLabel(answer)) + BadgeGap * ImGuiHelpers.GlobalScale + PillButton.Width(ActionLabel(answer));

    private static string StateLabel(CallerConsent answer) => Loc.T(answer.Allowed ? L.Api.Allowed : L.Api.Denied);

    private static string ActionLabel(CallerConsent answer) => Loc.T(answer.Allowed ? L.Api.Revoke : L.Api.Allow);
}

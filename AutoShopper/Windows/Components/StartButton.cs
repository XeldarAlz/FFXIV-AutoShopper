using AutoShopper.Core.Localization;
using Dalamud.Interface;

namespace AutoShopper.Windows.Components;

internal static class StartButton
{
    public static bool Draw(string sublabel, bool enabled, string? disabledReason = null, float width = 0f)
        => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Shop.Start), sublabel, Styling.AccentGlow, enabled, disabledReason, width);
}

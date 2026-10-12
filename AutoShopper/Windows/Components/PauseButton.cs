using AutoShopper.Core.Localization;
using AutoShopper.Core.Tasks;
using Dalamud.Interface;

namespace AutoShopper.Windows.Components;

internal static class PauseButton
{
    public static bool Draw(PauseReason reason, float width = 0f) => reason switch
    {
        PauseReason.InContent => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Shop.ResumeCaps), null, Styling.AccentMint, false, Loc.T(L.Shop.InContent), width),
        PauseReason.Manual    => HeroButton.Draw(FontAwesomeIcon.Play, Loc.T(L.Shop.ResumeCaps), null, Styling.AccentMint, true, null, width),
        _                     => HeroButton.Draw(FontAwesomeIcon.Pause, Loc.T(L.Shop.PauseCaps), null, Styling.AccentAmber, true, null, width),
    };
}

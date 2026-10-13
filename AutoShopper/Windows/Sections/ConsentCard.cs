using AutoShopper.Core.Localization;
using AutoShopper.Windows.Components;
using Dalamud.Interface;

namespace AutoShopper.Windows.Sections;

internal static class ConsentCard
{
    private static CachedText titleText;

    public static void Draw(Plugin plugin)
    {
        var api = plugin.Api;
        var pending = api.Consents.Pending;
        if (pending.Count == 0)
        {
            return;
        }

        var request = pending[0];
        var title = titleText.Get(api.Consents.Revision, static _ => Title());
        var card = ActionCard.Begin(title, Loc.T(L.Api.ConsentBody), null, null, Styling.AccentGlow, 2, FontAwesomeIcon.Plug);
        var allow = ActionCard.Button(ref card, "##ashop_consent_allow", Loc.T(L.Api.Allow), Styling.AccentMint, PillButton.Emphasis.Filled, true);
        var deny = ActionCard.Button(ref card, "##ashop_consent_deny", Loc.T(L.Api.Deny), Styling.AccentRose, PillButton.Emphasis.Ghost, true);
        ActionCard.End(card);
        if (allow)
        {
            api.Allow(request.Caller, request.DisplayName);
        }
        else if (deny)
        {
            api.Deny(request.Caller, request.DisplayName);
        }
    }

    private static string Title()
    {
        var pending = Plugin.Instance.Api.Consents.Pending;
        return pending.Count == 0 ? string.Empty : Loc.T(L.Api.ConsentTitle, pending[0].DisplayName);
    }
}

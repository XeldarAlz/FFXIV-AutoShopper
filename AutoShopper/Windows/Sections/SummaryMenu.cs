using AutoShopper.Core.Localization;
using AutoShopper.Core.Stats;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace AutoShopper.Windows.Sections;

// The text or JSON choice every "copy summary" button opens, and what each outcome tells the player.
internal static class SummaryMenu
{
    public static bool Draw(string menuId, out SummaryFormat format)
    {
        format = SummaryFormat.Text;
        using var menu = ContextMenu.Begin(menuId);
        if (!menu.Open)
        {
            return false;
        }

        if (ImGui.MenuItem(Loc.T(L.Exports.SummaryAsText)))
        {
            return true;
        }

        if (!ImGui.MenuItem(Loc.T(L.Exports.SummaryAsJson)))
        {
            return false;
        }

        format = SummaryFormat.Json;
        return true;
    }

    // Copies a ready summary and says so; any other outcome says why there is nothing to copy.
    public static void Deliver(SummaryExport export, ref TransientNotice notice)
    {
        if (export.Status == SummaryStatus.Ready)
        {
            ImGui.SetClipboardText(export.Text);
            notice.Show(Loc.T(L.Exports.SummaryCopied), Styling.AccentMintSoft);
            return;
        }

        notice.Show(Reason(export.Status), Styling.AccentAmberSoft);
    }

    public static string Reason(SummaryStatus status) => status switch
    {
        SummaryStatus.NotRecorded => Loc.T(L.Exports.SummaryNotRecorded),
        SummaryStatus.DryRunGone => Loc.T(L.Exports.SummaryDryRunGone),
        SummaryStatus.JournalGone => Loc.T(L.Exports.SummaryJournalGone),
        SummaryStatus.ReadFailed => Loc.T(L.Exports.SummaryReadFailed),
        _ => Loc.T(L.Exports.SummaryHelp),
    };
}

using AutoShopper.Core.Localization;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Pricing;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Core.Tasks;
using AutoShopper.Windows.Components;
using AutoShopper.Windows.Shell;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

// Prices are fetched on request and plans are built off the UI thread; only Start acts in game, through the same run
// as the Shop page.
internal sealed partial class PlanPage
{
    private const float SubtitleGap = 5f;
    private const float StatusGap = 6f;
    private const float ProgressHeight = 4f;
    private const float SectionGap = 18f;
    private const float HeaderButtonGap = 8f;
    private const float EmptyCardHeight = 118f;
    private const float EmptyCardPadX = 24f;
    // Ages and the stale flags are rebuilt once a minute; the text would not change any faster.
    private const long ViewRefreshMs = 60_000;
    // Packs a snapshot generation and a minute bucket into one cache key.
    private const long ViewBucketStride = 1_000_000;

    private PlanView? view;
    private CachedText subtitleText;
    private CachedText pricesText;
    private CachedText progressText;
    private CachedText cooldownText;

    public void Draw(Plugin plugin, AppWindow window)
    {
        plugin.Items.EnsureLoading();
        var list = plugin.Lists.Selected;
        var preview = plugin.Preview;
        DrawHeader(plugin, list);
        if (list is null)
        {
            DrawOpenListsCard(window, FontAwesomeIcon.ClipboardList, Loc.T(L.Plan.NoList));
            return;
        }

        if (list.Lines.Count == 0)
        {
            DrawOpenListsCard(window, FontAwesomeIcon.ListUl, Loc.T(L.Plan.EmptyList));
            return;
        }

        if (preview.Scope is not { } scope)
        {
            EmptyCard(FontAwesomeIcon.UserClock, Loc.T(L.Plan.LogIn));
            return;
        }

        var result = preview.LatestFor(list);
        if (result is null)
        {
            EmptyCard(FontAwesomeIcon.Route, Loc.T(preview.Planning ? L.Plan.Planning : L.Plan.NoPricesYet));
            return;
        }

        if (result.Input.Demands.Demands.IsEmpty && result.Input.Demands.TargetsUnknown == 0)
        {
            EmptyCard(FontAwesomeIcon.CheckCircle, Loc.T(L.Plan.NothingToBuy));
            return;
        }

        if (result.Input.Prices is null)
        {
            EmptyCard(FontAwesomeIcon.Route, Loc.T(L.Plan.NoPricesYet));
            return;
        }

        var current = EnsureView(plugin, scope, result);
        DrawWarnings(plugin, current);
        DrawTotals(current);
        DrawBudget(plugin, current);
        DrawComparison(plugin, current);
        DrawVendors(current);
        DrawRoute(current);
        DrawMissing(current);
        DrawFreshness(current);
    }

    private PlanView EnsureView(Plugin plugin, ResolvedScope scope, PreviewResult result)
    {
        var bucket = Environment.TickCount64 / ViewRefreshMs;
        var staleHours = plugin.Configuration.StaleAfterHours;
        var withNames = plugin.Items.IsReady;
        if (view is not null
            && ReferenceEquals(view.Result, result)
            && ReferenceEquals(view.Language, Loc.Current)
            && view.WithNames == withNames
            && view.StaleHours == staleHours
            && view.Bucket == bucket)
        {
            return view;
        }

        view = BuildView(plugin.Items, plugin.Vendors, scope, result, staleHours, bucket);
        return view;
    }

    private void DrawHeader(Plugin plugin, ShoppingList? list)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var prices = plugin.Prices;
        var buttonLabel = prices.Fetching
            ? progressText.Get(prices.CompletedRequests * 100_000L + prices.TotalRequests, static key => Loc.T(L.Plan.GettingPrices, key / 100_000L, key % 100_000L))
            : Loc.T(L.Plan.GetPrices);
        var buttonWidth = PillButton.Width(buttonLabel, FontAwesomeIcon.CloudDownloadAlt);

        var title = Loc.T(L.Plan.Title);
        float titleHeight;
        using (Fonts.PushTitle())
        {
            titleHeight = TextDraw.Measure(title).Y;
            TextDraw.At(title, origin, Styling.TextStrong);
        }

        var buttonHeight = Layout.ActionPillHeight * scale;
        var buttonY = origin.Y + (titleHeight - buttonHeight) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonWidth, buttonY));
        DrawGetPrices(plugin, list, buttonLabel);
        if (list is not null)
        {
            var startLabel = Loc.T(L.Plan.StartPlan);
            var startWidth = PillButton.Width(startLabel, FontAwesomeIcon.Play);
            ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonWidth - HeaderButtonGap * scale - startWidth, buttonY));
            DrawStart(plugin, startLabel);
        }

        var subtitleY = origin.Y + titleHeight + SubtitleGap * scale;
        if (list is not null)
        {
            var subtitle = subtitleText.Get(plugin.Lists.Revision, static _ => Loc.T(L.Plan.Subtitle, Plugin.Instance.Lists.Selected?.Name ?? string.Empty));
            TextDraw.At(TextDraw.Truncate(subtitle, width), new Vector2(origin.X, subtitleY), Styling.TextDim);
        }

        var statusY = subtitleY + ImGui.GetTextLineHeight() + StatusGap * scale;
        var statusHeight = DrawPriceStatus(plugin, new Vector2(origin.X, statusY), width);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, statusY + statusHeight - origin.Y));
        Paint.Divider(12f);
    }

    // The same Start as the Shop page: it runs the plan of the selected list, never this page's copy of it.
    private static void DrawStart(Plugin plugin, string label)
    {
        var readiness = ShopLauncher.Assess();
        var enabled = readiness == Readiness.Ready && !plugin.Controller.Running;
        var tooltip = enabled ? ShopLauncher.Sublabel() : readiness == Readiness.Ready ? null : ShopLauncher.Reason(readiness);
        if (PillButton.Draw("##ashop_plan_start", label, Styling.AccentMint, PillButton.Emphasis.Tinted, FontAwesomeIcon.Play, enabled, Layout.ActionPillHeight, tooltip))
        {
            ShopLauncher.Start();
        }

        if (!enabled && tooltip is not null && Hit.HoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
        {
            Tooltip.Show(tooltip);
        }
    }

    private void DrawGetPrices(Plugin plugin, ShoppingList? list, string label)
    {
        var prices = plugin.Prices;
        var cooldown = prices.CooldownRemaining;
        var enabled = list is not null && plugin.Preview.Scope is not null && !prices.Fetching && cooldown <= TimeSpan.Zero;
        var tooltip = cooldown > TimeSpan.Zero && !prices.Fetching
            ? cooldownText.Get((long)Math.Ceiling(cooldown.TotalSeconds), static seconds => Loc.T(L.Plan.Cooldown, seconds))
            : Loc.T(L.Plan.GetPricesHelp);
        if (PillButton.Draw("##ashop_plan_get_prices", label, Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.CloudDownloadAlt, enabled, Layout.ActionPillHeight, tooltip))
        {
            plugin.Preview.RequestPrices();
        }

        if (!enabled && Hit.HoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
        {
            Tooltip.Show(tooltip);
        }
    }

    // Returns the height used, so the header reserves exactly the lines it drew.
    private float DrawPriceStatus(Plugin plugin, Vector2 origin, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var prices = plugin.Prices;
        var drawList = ImGui.GetWindowDrawList();
        float height;
        using (Fonts.PushCaption())
        {
            height = ImGui.GetTextLineHeight();
            var snapshot = prices.Latest;
            var request = plugin.Preview.LastPriceRequest;
            if (plugin.Preview.PriceRequestCount > 0 && request is PriceStartResult.NoWorlds or PriceStartResult.NothingToPrice)
            {
                TextDraw.At(Loc.T(request == PriceStartResult.NoWorlds ? L.Plan.StartNoWorlds : L.Plan.StartNothing), origin, Styling.AccentAmberSoft);
            }
            else if (snapshot is not null)
            {
                var key = snapshot.Generation * ViewBucketStride + Environment.TickCount64 / ViewRefreshMs % ViewBucketStride;
                var text = pricesText.Get(key, static _ => PricesSummary(Plugin.Instance.Prices.Latest));
                var color = snapshot.FailedRequestCount > 0 ? Styling.AccentAmberSoft : Styling.TextMuted;
                TextDraw.At(TextDraw.Truncate(text, width), origin, color);
            }
            else if (plugin.Preview.Planning)
            {
                TextDraw.At(Loc.T(L.Plan.Planning), origin, Styling.TextMuted);
            }
        }

        if (!prices.Fetching)
        {
            return height;
        }

        var barY = origin.Y + height + StatusGap * scale;
        var total = Math.Max(1, prices.TotalRequests);
        Paint.Bar(drawList, new Vector2(origin.X, barY), width, ProgressHeight * scale, (float)prices.CompletedRequests / total, Styling.AccentGlow);
        return height + StatusGap * scale + ProgressHeight * scale;
    }

    private static string PricesSummary(PriceSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return string.Empty;
        }

        var summary = Loc.T(L.Plan.PricesSummary, Formatting.RelativeTime(snapshot.FetchedAtUtc), snapshot.Items.Length, snapshot.Worlds.Length);
        return snapshot.FailedRequestCount == 0
            ? summary
            : string.Concat(summary, TextDraw.Separator, Loc.T(L.Plan.RequestsFailed, snapshot.FailedRequestCount, snapshot.RequestCount));
    }

    private static void SectionTitle(string text, string? note)
    {
        var scale = ImGuiHelpers.GlobalScale;
        Styling.VSpace(SectionGap);
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var size = TextDraw.SectionTitleSize(text);
        TextDraw.SectionTitle(text, origin, Styling.TextStrong);
        var height = size.Y + 4f * scale;
        if (note is not null)
        {
            using (Fonts.PushCaption())
            {
                var noteSize = TextDraw.MeasureWrapped(note, width);
                TextDraw.Wrapped(note, new Vector2(origin.X, origin.Y + height), width, Styling.TextMuted);
                height += noteSize.Y;
            }
        }

        ImGui.Dummy(new Vector2(width, height + 8f * scale));
    }

    private static void EmptyCard(FontAwesomeIcon icon, string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, EmptyCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var wrapWidth = size.X - EmptyCardPadX * 2f * scale;
        var textSize = TextDraw.MeasureWrapped(text, wrapWidth);
        var iconHeight = 26f * scale;
        var gap = 12f * scale;
        var top = origin.Y + (size.Y - iconHeight - gap - textSize.Y) * 0.5f;
        var center = new Vector2((origin.X + end.X) * 0.5f, top + iconHeight * 0.5f);
        ProgressRing.CenterIcon(center, icon, Styling.TextMuted, iconHeight);
        TextDraw.Wrapped(text, new Vector2(center.X - MathF.Min(textSize.X, wrapWidth) * 0.5f, top + iconHeight + gap), wrapWidth, Styling.TextMuted);
        ImGui.Dummy(size);
    }
}

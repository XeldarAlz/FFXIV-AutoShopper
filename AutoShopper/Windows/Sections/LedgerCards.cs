using System.Globalization;
using AutoShopper.Core;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Inventory;
using AutoShopper.Core.Ledger;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Travel;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Sections;

// The purchase journal's open questions on the Shop page, one card at a time: damage first, then a purchase without a
// clear answer, then a run that was interrupted. Buying stays off until the card is answered.
internal static class LedgerCards
{
    private const float PadX = 20f;
    private const float PadY = 16f;
    private const float LineGap = 6f;
    private const float ButtonGap = 8f;
    private const float CardGap = 14f;
    // The live gil and owned count shown on the reconciliation card are read again at most this often.
    private const long LiveRefreshMs = 1_000;

    private static CachedText factsText;
    private static CachedText resumeText;
    private static LiveInventory live;
    private static uint liveItemId;
    private static long liveReadAtMs;
    private static bool writeFailed;

    public static void Draw(Plugin plugin)
    {
        var ledger = plugin.Ledger;
        var review = ledger.Review;
        if (ledger.ReadError is not null || review.IsDamaged)
        {
            DrawDamaged(plugin, ledger.ReadError is not null);
            return;
        }

        for (var index = 0; index < review.Unresolved.Length; index++)
        {
            if (review.Unresolved[index].HasIntent)
            {
                DrawUnresolved(plugin, review.Unresolved[index]);
                return;
            }
        }

        if (review.Interrupted is { } interrupted)
        {
            DrawInterrupted(plugin, interrupted);
        }
    }

    private static void DrawDamaged(Plugin plugin, bool unreadable)
    {
        var body = Loc.T(unreadable ? L.Journal.ReadFailed : L.Journal.Body);
        var detail = writeFailed ? Loc.T(L.Journal.SetAsideFailed) : null;
        var card = BeginCard(Loc.T(L.Journal.Title), body, detail, null, Styling.AccentRose, 1);
        if (!unreadable && ActionButton(ref card, "##ashop_journal_aside", Loc.T(L.Journal.SetAside), Styling.AccentRose, PillButton.Emphasis.Tinted, true))
        {
            writeFailed = !plugin.Ledger.SetAside(DateTime.UtcNow, out var path);
            RunLog.Info(writeFailed ? $"Journal: setting the purchase journal aside failed: {plugin.Ledger.Journal.LastError}." : $"Journal: the purchase journal was set aside as {path}.");
        }

        EndCard(card);
    }

    private static void DrawUnresolved(Plugin plugin, in UnresolvedAttempt attempt)
    {
        RefreshLive(attempt.ItemId);
        var ledger = plugin.Ledger;
        var key = HashCode.Combine(ledger.Revision, attempt.AttemptId, Environment.TickCount64 / TimeUnits.MillisecondsPerMinute, live.Gil, live.Owned, live.Available);
        var facts = factsText.Get(key, static _ => string.Concat(DescribeAttempt(), "\n", DescribeEvidence()));
        var detail = writeFailed ? Loc.T(L.Reconcile.WriteFailed) : null;
        var card = BeginCard(Loc.T(L.Reconcile.Title), Loc.T(L.Reconcile.Body), detail, facts, Styling.AccentRose, 2);
        if (ActionButton(ref card, "##ashop_reconcile_yes", Loc.T(L.Reconcile.WentThrough), Styling.AccentRose, PillButton.Emphasis.Filled, true))
        {
            Answer(plugin, attempt, purchased: true);
        }

        if (ActionButton(ref card, "##ashop_reconcile_no", Loc.T(L.Reconcile.DidNotGoThrough), Styling.AccentGlow, PillButton.Emphasis.Tinted, true))
        {
            Answer(plugin, attempt, purchased: false);
        }

        EndCard(card);
    }

    private static void DrawInterrupted(Plugin plugin, JournalProgress interrupted)
    {
        var ownCharacter = interrupted.Authorization!.CharacterContentId == ECommons.DalamudServices.Svc.PlayerState.ContentId;
        var body = resumeText.Get(HashCode.Combine(plugin.Ledger.Revision, Environment.TickCount64 / TimeUnits.MillisecondsPerMinute), static _ => DescribeInterrupted());
        var detail = ownCharacter ? null : Loc.T(L.Resume.OtherCharacter);
        var running = plugin.Controller.Running;
        var card = BeginCard(Loc.T(L.Resume.Title), body, detail, null, Styling.AccentAmber, 2);
        if (ActionButton(ref card, "##ashop_resume_run", Loc.T(L.Resume.ResumeRun), Styling.AccentMint, PillButton.Emphasis.Filled, ownCharacter && !running))
        {
            plugin.Controller.ResumeInterrupted();
        }

        if (ActionButton(ref card, "##ashop_resume_discard", Loc.T(L.Resume.Discard), Styling.AccentRose, PillButton.Emphasis.Ghost, !running))
        {
            plugin.Controller.DiscardInterrupted();
        }

        EndCard(card);
    }

    private static void Answer(Plugin plugin, in UnresolvedAttempt attempt, bool purchased)
    {
        var answer = plugin.Ledger.Reconcile(attempt.AttemptId, purchased, DateTime.UtcNow);
        writeFailed = answer == LedgerAnswer.WriteFailed;
        RunLog.Info($"Journal: purchase {attempt.AttemptId} answered as {(purchased ? "went through" : "did not go through")}: {answer}.");
    }

    private static void RefreshLive(uint itemId)
    {
        var now = Environment.TickCount64;
        if (itemId == liveItemId && now - liveReadAtMs < LiveRefreshMs)
        {
            return;
        }

        live = InventoryReader.ReadLive(itemId);
        liveItemId = itemId;
        liveReadAtMs = now;
    }

    private static string DescribeAttempt()
    {
        var review = Plugin.Instance.Ledger.Review;
        for (var index = 0; index < review.Unresolved.Length; index++)
        {
            var attempt = review.Unresolved[index];
            if (!attempt.HasIntent)
            {
                continue;
            }

            var items = Plugin.Instance.Items;
            var name = items.TryGetName(attempt.ItemId, out var found) ? found : attempt.ItemId.ToString(CultureInfo.InvariantCulture);
            var what = Loc.T(L.Reconcile.Attempt, attempt.Quantity.ToString("N0", Loc.Culture), name, Loc.Gil(attempt.Payable));
            var world = Worlds.TryFindById(attempt.WorldId, out var info) ? info.Name : attempt.WorldId.ToString(CultureInfo.InvariantCulture);
            return string.Concat(what, TextDraw.Separator, Loc.T(L.Reconcile.When, world, Formatting.RelativeTime(attempt.AtUtc)));
        }

        return string.Empty;
    }

    private static string DescribeEvidence()
    {
        var review = Plugin.Instance.Ledger.Review;
        for (var index = 0; index < review.Unresolved.Length; index++)
        {
            var attempt = review.Unresolved[index];
            if (!attempt.HasIntent)
            {
                continue;
            }

            var unknown = Loc.T(L.Reconcile.Unknown);
            var gilBefore = attempt.GilBefore >= 0 ? Loc.Gil(attempt.GilBefore) : unknown;
            var gilNow = live.Available ? Loc.Gil(live.Gil) : unknown;
            var ownedBefore = attempt.OwnedBefore >= 0 ? attempt.OwnedBefore.ToString("N0", Loc.Culture) : unknown;
            var ownedNow = live.Available ? live.Owned.ToString("N0", Loc.Culture) : unknown;
            return string.Concat(Loc.T(L.Reconcile.GilEvidence, gilBefore, gilNow), TextDraw.Separator, Loc.T(L.Reconcile.OwnedEvidence, ownedBefore, ownedNow));
        }

        return string.Empty;
    }

    private static string DescribeInterrupted()
    {
        if (Plugin.Instance.Ledger.Review.Interrupted is not { Authorization: { } authorization } interrupted)
        {
            return string.Empty;
        }

        var bought = 0;
        var required = 0;
        for (var index = 0; index < interrupted.Demands.Length; index++)
        {
            bought += interrupted.Demands[index].ConfirmedQuantity;
            required += interrupted.Demands[index].Demand.RequiredQuantity;
        }

        return Loc.T(L.Resume.Body,
            Formatting.RelativeTime(authorization.CreatedAtUtc),
            bought.ToString("N0", Loc.Culture),
            required.ToString("N0", Loc.Culture),
            Loc.Gil(interrupted.ConfirmedSpend));
    }

    private struct Card
    {
        public Vector2 Origin;
        public float Width;
        public float Height;
        public float ButtonX;
        public float ButtonY;
    }

    private static Card BeginCard(string title, string body, string? warning, string? facts, Vector4 accent, int buttons)
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
        TextDraw.Icon(FontAwesomeIcon.ExclamationTriangle, new Vector2(origin.X + padX, origin.Y + padY), accent);

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

    private static bool ActionButton(ref Card card, string id, string label, Vector4 accent, PillButton.Emphasis emphasis, bool enabled)
    {
        ImGui.SetCursorScreenPos(new Vector2(card.ButtonX, card.ButtonY));
        var clicked = PillButton.Draw(id, label, accent, emphasis, null, enabled, Layout.ActionPillHeight);
        card.ButtonX += PillButton.Width(label) + ButtonGap * ImGuiHelpers.GlobalScale;
        return clicked && enabled;
    }

    private static void EndCard(in Card card)
    {
        ImGui.SetCursorScreenPos(card.Origin);
        ImGui.Dummy(new Vector2(card.Width, card.Height));
        Styling.VSpace(CardGap);
    }
}

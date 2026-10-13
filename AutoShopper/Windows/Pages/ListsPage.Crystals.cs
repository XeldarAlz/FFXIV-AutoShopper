using AutoShopper.Core;
using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;
using System.Text;

namespace AutoShopper.Windows.Pages;

internal sealed partial class ListsPage
{
    private const float CrystalTargetWidth = 96f;
    private const int CrystalTargetDigits = 4;
    private const string DefaultCrystalTarget = "999";
    private const string CrystalTooltipSeparator = ", ";

    private static readonly LocString[] elementNames =
    [
        L.Lists.ElementFire, L.Lists.ElementIce, L.Lists.ElementWind, L.Lists.ElementEarth, L.Lists.ElementLightning, L.Lists.ElementWater,
    ];

    private static readonly LocString[] kindNames = [L.Lists.KindShard, L.Lists.KindCrystal, L.Lists.KindCluster];

    private static readonly string[] elementChipIds =
    [
        "##ashop_crystal_fire", "##ashop_crystal_ice", "##ashop_crystal_wind", "##ashop_crystal_earth", "##ashop_crystal_lightning", "##ashop_crystal_water",
    ];

    private static readonly string[] kindChipIds = ["##ashop_crystal_shard", "##ashop_crystal_crystal", "##ashop_crystal_cluster"];

    private readonly string?[] elementTooltips = new string?[CrystalRestock.ElementCount];
    private readonly string?[] kindTooltips = new string?[CrystalRestock.KindCount];
    private LanguageInfo? crystalTooltipsLanguage;
    private bool crystalTooltipsWithItems;
    private bool crystalsOpen;
    private bool focusCrystalTarget;
    private CrystalElements crystalElements = CrystalElements.All;
    private CrystalKinds crystalKinds = CrystalKinds.Shard | CrystalKinds.Crystal;
    private string crystalTargetText = DefaultCrystalTarget;
    private CachedText crystalPreviewText;

    private void ToggleCrystals()
    {
        if (crystalsOpen)
        {
            CloseCrystals();
            return;
        }

        CloseImport();
        crystalsOpen = true;
        focusCrystalTarget = true;
    }

    private void CloseCrystals()
    {
        crystalsOpen = false;
        focusCrystalTarget = false;
    }

    private void DrawCrystalPanel(Plugin plugin, ShoppingList list)
    {
        EnsureCrystalTooltips(plugin.Items);
        using (SettingsGroup.Begin(string.Empty))
        {
            var width = SettingsGroup.ContentRightEdge - ImGui.GetCursorScreenPos().X;
            if (DrawPanelTitle("##ashop_crystals_close", Loc.T(L.Lists.CrystalsTitle), Loc.T(L.Lists.CrystalsBody), width))
            {
                CloseCrystals();
                return;
            }

            var labelColumn = CrystalLabelColumn();
            DrawElementChips(width, labelColumn);
            Styling.VSpace(6f);
            DrawKindChips(width, labelColumn);
            Styling.VSpace(8f);
            var target = DrawCrystalTarget(width, labelColumn);
            DrawCrystalActions(plugin, list, target);
        }

        if (crystalsOpen && ImGui.IsKeyPressed(ImGuiKey.Escape) && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            CloseCrystals();
        }
    }

    // Each chip's tooltip names the game's own items it stands for, so the player sees exactly what is added.
    private void EnsureCrystalTooltips(ItemCatalog catalog)
    {
        var withItems = catalog.IsReady;
        if (ReferenceEquals(crystalTooltipsLanguage, Loc.Current) && crystalTooltipsWithItems == withItems)
        {
            return;
        }

        crystalTooltipsLanguage = Loc.Current;
        crystalTooltipsWithItems = withItems;
        for (var elementIndex = 0; elementIndex < CrystalRestock.ElementCount; elementIndex++)
        {
            elementTooltips[elementIndex] = withItems ? CrystalNames(catalog, elementIndex, -1) : null;
        }

        for (var kindIndex = 0; kindIndex < CrystalRestock.KindCount; kindIndex++)
        {
            kindTooltips[kindIndex] = withItems ? CrystalNames(catalog, -1, kindIndex) : null;
        }
    }

    // A negative index stands for every element or every kind.
    private static string? CrystalNames(ItemCatalog catalog, int onlyElement, int onlyKind)
    {
        var builder = new StringBuilder();
        for (var kindIndex = 0; kindIndex < CrystalRestock.KindCount; kindIndex++)
        {
            for (var elementIndex = 0; elementIndex < CrystalRestock.ElementCount; elementIndex++)
            {
                if ((onlyElement >= 0 && elementIndex != onlyElement) || (onlyKind >= 0 && kindIndex != onlyKind))
                {
                    continue;
                }

                if (!catalog.TryGetName(CrystalRestock.ItemIdOf(elementIndex, kindIndex), out var name))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(CrystalTooltipSeparator);
                }

                builder.Append(name);
            }
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static float CrystalLabelColumn()
    {
        using (Fonts.PushCaption())
        {
            var widest = MathF.Max(TextDraw.Measure(Loc.T(L.Lists.CrystalsElements)).X, TextDraw.Measure(Loc.T(L.Lists.CrystalsKinds)).X);
            return MathF.Max(widest, TextDraw.Measure(Loc.T(L.Lists.CrystalsTarget)).X) + ImportLabelGap * ImGuiHelpers.GlobalScale;
        }
    }

    private void DrawElementChips(float width, float labelColumn)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = Layout.ConsoleChipHeight * scale;
        var gap = ToolbarGap * scale;
        var origin = ImGui.GetCursorScreenPos();
        DrawChipLabel(Loc.T(L.Lists.CrystalsElements), origin, height);
        var left = origin.X + labelColumn;
        var right = origin.X + width;
        var x = left;
        var y = origin.Y;

        var allLabel = Loc.T(L.Lists.CrystalsAll);
        var allWidth = FilterChip.Width(allLabel, null);
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var all = crystalElements == CrystalElements.All;
        if (FilterChip.Draw("##ashop_crystal_all", allLabel, null, Styling.AccentGlow, all, height, null, Loc.T(L.Lists.CrystalsAllHelp)))
        {
            crystalElements = all ? CrystalElements.None : CrystalElements.All;
        }

        x += allWidth + gap;
        for (var elementIndex = 0; elementIndex < CrystalRestock.ElementCount; elementIndex++)
        {
            var label = Loc.T(elementNames[elementIndex]);
            var chipWidth = FilterChip.Width(label, null);
            if (x + chipWidth > right && x > left)
            {
                x = left;
                y += height + gap;
            }

            ImGui.SetCursorScreenPos(new Vector2(x, y));
            var active = CrystalRestock.Has(crystalElements, elementIndex);
            if (FilterChip.Draw(elementChipIds[elementIndex], label, null, Styling.AccentBlue, active, height, null, elementTooltips[elementIndex]))
            {
                crystalElements ^= (CrystalElements)(1 << elementIndex);
            }

            x += chipWidth + gap;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, y - origin.Y + height));
    }

    private void DrawKindChips(float width, float labelColumn)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = Layout.ConsoleChipHeight * scale;
        var gap = ToolbarGap * scale;
        var origin = ImGui.GetCursorScreenPos();
        DrawChipLabel(Loc.T(L.Lists.CrystalsKinds), origin, height);
        var left = origin.X + labelColumn;
        var right = origin.X + width;
        var x = left;
        var y = origin.Y;
        for (var kindIndex = 0; kindIndex < CrystalRestock.KindCount; kindIndex++)
        {
            var label = Loc.T(kindNames[kindIndex]);
            var chipWidth = FilterChip.Width(label, null);
            if (x + chipWidth > right && x > left)
            {
                x = left;
                y += height + gap;
            }

            ImGui.SetCursorScreenPos(new Vector2(x, y));
            var active = CrystalRestock.Has(crystalKinds, kindIndex);
            if (FilterChip.Draw(kindChipIds[kindIndex], label, null, Styling.AccentNebula, active, height, null, kindTooltips[kindIndex]))
            {
                crystalKinds ^= (CrystalKinds)(1 << kindIndex);
            }

            x += chipWidth + gap;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, y - origin.Y + height));
    }

    private static void DrawChipLabel(string label, Vector2 origin, float height)
    {
        using (Fonts.PushCaption())
        {
            var size = TextDraw.Measure(label);
            TextDraw.At(label, new Vector2(origin.X, origin.Y + (height - size.Y) * 0.5f), Styling.TextDim);
        }
    }

    // Returns the target, or zero while the field holds no valid count.
    private int DrawCrystalTarget(float width, float labelColumn)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var frameHeight = ImGui.GetFrameHeight();
        DrawPickerLabel(Loc.T(L.Lists.CrystalsTarget), origin);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + labelColumn, origin.Y));
        NumberField.Draw("##ashop_crystal_target", ref crystalTargetText, DefaultCrystalTarget, CrystalTargetWidth,
            CrystalTargetDigits, Loc.T(L.Lists.CrystalsTargetHelp), focusCrystalTarget);
        focusCrystalTarget = false;

        var target = ImportFields.TryParseWhole(crystalTargetText, out var parsed) == NumberStatus.Valid && parsed is >= ShoppingLimits.MinimumQuantity and <= CrystalRestock.MaximumTarget
            ? (int)parsed
            : 0;
        var count = CrystalRestock.Count(crystalElements, crystalKinds);
        var previewX = origin.X + labelColumn + (CrystalTargetWidth + ImportLabelGap) * scale;
        using (Fonts.PushCaption())
        {
            var preview = count == 0 ? Loc.T(L.Lists.CrystalsNone) : crystalPreviewText.Get(((long)count << 32) | (uint)target, static key => CrystalPreview(key));
            var size = TextDraw.Measure(preview);
            var color = count == 0 || target == 0 ? Styling.AccentAmberSoft : Styling.TextMuted;
            TextDraw.At(TextDraw.Truncate(preview, origin.X + width - previewX), new Vector2(previewX, origin.Y + (frameHeight - size.Y) * 0.5f), color);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, frameHeight));
        return target;
    }

    private static string CrystalPreview(long key)
    {
        var count = (int)(key >> 32);
        var target = (int)(key & uint.MaxValue);
        return target == 0
            ? Loc.T(L.Lists.CrystalsTargetHelp)
            : Loc.Plural(L.Lists.CrystalsPreview, count, target.ToString("N0", Loc.Culture));
    }

    private void DrawCrystalActions(Plugin plugin, ShoppingList list, int target)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var count = CrystalRestock.Count(crystalElements, crystalKinds);
        var ready = plugin.Items.IsReady;
        var enabled = ready && count > 0 && target > 0;
        var tooltip = !ready ? Loc.T(plugin.Items.LoadFailed ? L.Lists.LoadFailed : L.Lists.Loading)
            : count == 0 ? Loc.T(L.Lists.CrystalsNone)
            : target == 0 ? Loc.T(L.Lists.CrystalsTargetHelp)
            : null;
        Styling.VSpace(10f);
        var origin = ImGui.GetCursorScreenPos();
        var addLabel = Loc.T(L.Lists.ImportAdd);
        if (PillButton.Draw("##ashop_crystals_add", addLabel, Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.Plus, enabled, Layout.ActionPillHeight))
        {
            AddCrystals(plugin, list, target);
            return;
        }

        if (!enabled && tooltip is not null && Hit.HoveringRect(origin, origin + new Vector2(PillButton.Width(addLabel, FontAwesomeIcon.Plus), Layout.ActionPillHeight * scale)))
        {
            Tooltip.Show(tooltip);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(PillButton.Width(addLabel, FontAwesomeIcon.Plus) + ToolbarGap * scale, 0f));
        if (PillButton.Draw("##ashop_crystals_cancel", Loc.T(L.Common.Cancel), Styling.TextSecondary, PillButton.Emphasis.Ghost, height: Layout.ActionPillHeight))
        {
            CloseCrystals();
            return;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(1f, Layout.ActionPillHeight * scale));
    }

    private void AddCrystals(Plugin plugin, ShoppingList list, int target)
    {
        var outcome = plugin.Lists.AddCrystals(list, crystalElements, crystalKinds, target);
        if (outcome.LeftOut > 0)
        {
            ShowNotice(Loc.Plural(L.Lists.CrystalsLeftOut, outcome.LeftOut), Styling.AccentAmberSoft);
        }
        else
        {
            ShowNotice(Loc.T(L.Lists.CrystalsDone, outcome.Added, outcome.Updated), Styling.AccentMintSoft);
        }

        RunLog.Info($"Crystals: {outcome.Added} added, {outcome.Updated} updated, {outcome.LeftOut} left out on '{list.Name}', topping up to {target}.");
        CloseCrystals();
    }
}

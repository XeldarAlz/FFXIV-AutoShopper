using AutoShopper.Core.Inventory;
using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Globalization;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

internal sealed partial class ListsPage
{
    private enum SortColumn : byte
    {
        None,
        Name,
        Quantity,
        Cap,
    }

    private enum NumberColumn : byte
    {
        UnitCap,
        Excess,
    }

    private const float LineRowHeight = 48f;
    private const float LineRowGap = 4f;
    private const float LineIconSize = 30f;
    private const float LinePadX = 10f;
    private const float CellGap = 8f;
    private const float RailWidth = 3f;
    private const float QualityWidth = 68f;
    private const float ModeWidth = 76f;
    private const float QuantityWidth = 112f;
    private const float OwnedWidth = 62f;
    private const float CapWidth = 94f;
    private const float ExcessWidth = 64f;
    private const float HeaderRowHeight = 26f;
    private const float EmptyLinesHeight = 70f;
    // 999,999,999 and 999,999, the largest cap and excess allowance, have nine and six digits.
    private const int CapDigits = 9;
    private const int ExcessDigits = 6;
    private const string NoValue = "-";
    private const string ZeroHint = "0";

    private static readonly Segmented.Item[] modeItems =
    [
        new(FontAwesomeIcon.ShoppingBasket, string.Empty),
        new(FontAwesomeIcon.Bullseye, string.Empty),
    ];

    private readonly string[] qualityLabels = new string[3];
    private readonly Dictionary<Guid, NumberText> capTexts = [];
    private readonly Dictionary<Guid, NumberText> excessTexts = [];
    private LanguageInfo? qualityLabelsLanguage;
    private CachedText[] ownedTexts = new CachedText[16];
    private CachedText[] needTexts = new CachedText[16];
    private CachedText[] idTexts = new CachedText[16];
    private int[] order = [];
    private Guid orderListId;
    private int orderCount = -1;
    private SortColumn orderColumn;
    private bool orderDescending;
    private bool orderWithNames;
    private SortColumn sortColumn;
    private bool sortDescending;
    private Guid removeLineId;

    private readonly record struct LineColumns(
        float Left, float Name, float NameWidth, float Quality, float Mode, float Quantity, float Owned, float Cap, float Excess, float Remove, float Right);

    private sealed class NumberText
    {
        public long Value = -1;
        public string Text = string.Empty;
    }

    private void DrawLines(Plugin plugin, ShoppingList list, ListAnalysis analysis)
    {
        var lines = list.Lines;
        if (lines.Count == 0)
        {
            DrawEmptyLines();
            return;
        }

        EnsureLineCaches(lines.Count);
        EnsureOrder(list, plugin.Items);
        EnsureQualityLabels();

        var origin = ImGui.GetCursorScreenPos();
        var columns = ColumnsFor(origin.X, ImGui.GetContentRegionAvail().X);
        DrawLineHeader(columns);
        for (var position = 0; position < order.Length; position++)
        {
            DrawLineRow(plugin, list, analysis, order[position], columns);
        }

        if (removeLineId != Guid.Empty)
        {
            plugin.Lists.RemoveLine(list, removeLineId);
            capTexts.Remove(removeLineId);
            excessTexts.Remove(removeLineId);
            removeLineId = Guid.Empty;
        }

        Styling.VSpace(8f);
        SettingsGroup.Footnote(Loc.T(L.Lists.InventoryNote));
    }

    private static void DrawEmptyLines()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, EmptyLinesHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, origin + size, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);
        var text = Loc.T(L.Lists.EmptyList);
        var wrapWidth = size.X - LinePadX * 4f * scale;
        var textSize = TextDraw.MeasureWrapped(text, wrapWidth);
        TextDraw.Wrapped(text, new Vector2(origin.X + (size.X - MathF.Min(textSize.X, wrapWidth)) * 0.5f, origin.Y + (size.Y - textSize.Y) * 0.5f), wrapWidth, Styling.TextMuted);
        ImGui.Dummy(size);
    }

    private void EnsureLineCaches(int count)
    {
        if (ownedTexts.Length >= count)
        {
            return;
        }

        var size = Math.Max(count, ownedTexts.Length * 2);
        ownedTexts = new CachedText[size];
        needTexts = new CachedText[size];
        idTexts = new CachedText[size];
    }

    private void EnsureQualityLabels()
    {
        if (ReferenceEquals(qualityLabelsLanguage, Loc.Current))
        {
            return;
        }

        qualityLabelsLanguage = Loc.Current;
        qualityLabels[(int)ItemQuality.Any] = Loc.T(L.Lists.QualityAny);
        qualityLabels[(int)ItemQuality.HighQuality] = Loc.T(L.Lists.QualityHigh);
        qualityLabels[(int)ItemQuality.NormalQuality] = Loc.T(L.Lists.QualityNormal);
    }

    // Sorting is a view: it reorders rows on screen and never the list itself. The order is rebuilt when lines come
    // or go, not while a value is edited, so a row does not jump away from the control under the cursor.
    private void EnsureOrder(ShoppingList list, ItemCatalog catalog)
    {
        var lines = list.Lines;
        var withNames = catalog.IsReady;
        if (orderListId == list.Id && orderCount == lines.Count && order.Length == lines.Count
            && orderColumn == sortColumn && orderDescending == sortDescending && orderWithNames == withNames)
        {
            return;
        }

        orderListId = list.Id;
        orderCount = lines.Count;
        orderColumn = sortColumn;
        orderDescending = sortDescending;
        orderWithNames = withNames;
        if (order.Length != lines.Count)
        {
            order = new int[lines.Count];
        }

        for (var index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }

        if (sortColumn == SortColumn.None)
        {
            return;
        }

        var column = sortColumn;
        var direction = sortDescending ? -1 : 1;
        Array.Sort(order, (left, right) =>
        {
            var compared = CompareLines(lines[left], lines[right], column, catalog) * direction;
            return compared != 0 ? compared : left.CompareTo(right);
        });
    }

    private static int CompareLines(ShoppingLine left, ShoppingLine right, SortColumn column, ItemCatalog catalog) => column switch
    {
        SortColumn.Name => string.Compare(SortName(left.ItemId, catalog), SortName(right.ItemId, catalog), StringComparison.OrdinalIgnoreCase),
        SortColumn.Quantity => left.Quantity.CompareTo(right.Quantity),
        _ => CapSortKey(left.MaxUnitPrice).CompareTo(CapSortKey(right.MaxUnitPrice)),
    };

    private static string SortName(uint itemId, ItemCatalog catalog)
        => catalog.TryGetName(itemId, out var name) ? name : string.Empty;

    // No cap sorts above every cap.
    private static long CapSortKey(long maxUnitPrice) => maxUnitPrice == 0 ? long.MaxValue : maxUnitPrice;

    private static LineColumns ColumnsFor(float left, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = CellGap * scale;
        var right = left + width;
        var remove = right - LinePadX * scale - ImGui.GetFrameHeight();
        var excess = remove - gap - ExcessWidth * scale;
        var cap = excess - gap - CapWidth * scale;
        var owned = cap - gap - OwnedWidth * scale;
        var quantity = owned - gap - QuantityWidth * scale;
        var mode = quantity - gap - ModeWidth * scale;
        var quality = mode - gap - QualityWidth * scale;
        var name = left + (LinePadX + RailWidth + LineIconSize) * scale + gap;
        return new LineColumns(left, name, MathF.Max(0f, quality - gap - name), quality, mode, quantity, owned, cap, excess, remove, right);
    }

    private void DrawLineHeader(LineColumns columns)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var height = HeaderRowHeight * scale;
        var itemX = columns.Left + (LinePadX + RailWidth) * scale;
        SortHeader("##ashop_sort_name", L.Lists.ColumnItem, SortColumn.Name, itemX, origin.Y, columns.Quality - CellGap * scale - itemX, height);
        HeaderLabel(L.Lists.ColumnQuality, columns.Quality, origin.Y, QualityWidth * scale, height, L.Lists.QualityHelp);
        HeaderLabel(L.Lists.ColumnMode, columns.Mode, origin.Y, ModeWidth * scale, height, null);
        SortHeader("##ashop_sort_quantity", L.Lists.ColumnQuantity, SortColumn.Quantity, columns.Quantity, origin.Y, QuantityWidth * scale, height);
        HeaderLabel(L.Lists.ColumnOwned, columns.Owned, origin.Y, OwnedWidth * scale, height, L.Lists.OwnedHelp);
        SortHeader("##ashop_sort_cap", L.Lists.ColumnCap, SortColumn.Cap, columns.Cap, origin.Y, CapWidth * scale, height);
        HeaderLabel(L.Lists.ColumnExcess, columns.Excess, origin.Y, ExcessWidth * scale, height, L.Lists.ExcessHelp);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(columns.Right - columns.Left, height));
    }

    private static void HeaderLabel(LocString label, float x, float y, float width, float height, LocString? help)
    {
        var text = TextDraw.Upper(Loc.T(label));
        using (Fonts.PushCaption())
        {
            var shown = TextDraw.Truncate(text, width);
            var size = TextDraw.Measure(shown);
            var min = new Vector2(x, y + (height - size.Y) * 0.5f);
            TextDraw.At(shown, min, Styling.TextMuted);
            if (help is { } helpText && Hit.HoveringRect(min, min + size))
            {
                Tooltip.Show(Loc.T(helpText));
            }
        }
    }

    private void SortHeader(string id, LocString label, SortColumn column, float x, float y, float width, float height)
    {
        var active = sortColumn == column;
        var icon = !active ? FontAwesomeIcon.Sort : sortDescending ? FontAwesomeIcon.SortDown : FontAwesomeIcon.SortUp;
        var iconSize = TextDraw.IconSize(icon);
        var scale = ImGuiHelpers.GlobalScale;
        var text = TextDraw.Upper(Loc.T(label));
        Vector2 textSize;
        string shown;
        using (Fonts.PushCaption())
        {
            shown = TextDraw.Truncate(text, MathF.Max(0f, width - iconSize.X - 6f * scale));
            textSize = TextDraw.Measure(shown);
        }

        var hitWidth = MathF.Max(1f, MathF.Min(width, textSize.X + 6f * scale + iconSize.X));
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var hit = Hit.Area(id, new Vector2(hitWidth, height));
        var hover = Motion.Hover(Motion.Key(id), hit.Hovered);
        var color = active ? Styling.AccentGlowSoft : Vector4.Lerp(Styling.TextMuted, Styling.TextSecondary, hover);
        using (Fonts.PushCaption())
        {
            TextDraw.At(shown, new Vector2(x, y + (height - textSize.Y) * 0.5f), color);
        }

        TextDraw.Icon(icon, new Vector2(x + textSize.X + 6f * scale, y + (height - iconSize.Y) * 0.5f), active ? color : Styling.WithAlpha(color, 0.6f));
        if (hit.Hovered)
        {
            Tooltip.Show(Loc.T(L.Lists.SortHint));
        }

        if (!hit.Clicked)
        {
            return;
        }

        if (!active)
        {
            sortColumn = column;
            sortDescending = false;
        }
        else if (!sortDescending)
        {
            sortDescending = true;
        }
        else
        {
            sortColumn = SortColumn.None;
            sortDescending = false;
        }
    }

    private void DrawLineRow(Plugin plugin, ShoppingList list, ListAnalysis analysis, int lineIndex, LineColumns columns)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(columns.Right - columns.Left, LineRowHeight * scale);
        if (!ImGui.IsRectVisible(size))
        {
            ImGui.Dummy(size);
            Styling.VSpace(LineRowGap);
            return;
        }

        var line = list.Lines[lineIndex];
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var midY = origin.Y + size.Y * 0.5f;
        var problems = analysis.Validation.At(lineIndex);
        var drawList = ImGui.GetWindowDrawList();
        DrawRowSurface(drawList, origin, end, problems);

        var known = plugin.Items.TryGet(line.ItemId, out var item);
        var iconSize = LineIconSize * scale;
        var iconX = columns.Left + (LinePadX + RailWidth) * scale;
        ItemIcon.Draw(drawList, known ? item.IconId : 0, new Vector2(iconX, midY - iconSize * 0.5f), iconSize,
            line.Quality == ItemQuality.HighQuality && known && item.CanBeHighQuality);

        ImGui.PushID(line.Id.GetHashCode());
        DrawLineName(line, lineIndex, known, item, problems, columns, origin.Y, size.Y);
        var frameTop = midY - ImGui.GetFrameHeight() * 0.5f;
        DrawQualityCell(plugin.Lists, list, line, known && !item.CanBeHighQuality, new Vector2(columns.Quality, frameTop));
        DrawModeCell(plugin.Lists, list, line, new Vector2(columns.Mode, frameTop));
        DrawQuantityCell(plugin.Lists, list, line, new Vector2(columns.Quantity, frameTop));
        DrawOwnedCell(plugin.Inventory, analysis, line, lineIndex, columns.Owned, OwnedWidth * scale, origin.Y, size.Y);
        DrawNumberCell(plugin.Lists, list, line, NumberColumn.UnitCap, new Vector2(columns.Cap, frameTop));
        DrawNumberCell(plugin.Lists, list, line, NumberColumn.Excess, new Vector2(columns.Excess, frameTop));

        ImGui.SetCursorScreenPos(new Vector2(columns.Remove, frameTop));
        if (IconButton.Draw(FontAwesomeIcon.Times, "##remove", ImGui.GetFrameHeight(), Styling.AccentRose, Loc.T(L.Lists.RemoveLine)))
        {
            removeLineId = line.Id;
        }

        ImGui.PopID();
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
        Styling.VSpace(LineRowGap);
    }

    private static void DrawRowSurface(ImDrawListPtr drawList, Vector2 origin, Vector2 end, LineProblems problems)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var hovered = Hit.HoveringRect(origin, end);
        var accent = ListValidation.IsError(problems) ? Styling.AccentRose
            : problems != LineProblems.None ? Styling.AccentAmber
            : Styling.AccentGlow;
        var tint = problems == LineProblems.None ? 0f : 0.06f;
        var fill = Styling.Tint(Styling.WithAlpha(hovered ? Styling.Surface2 : Styling.Surface1, hovered ? 0.7f : 0.45f), accent, tint);
        var rounding = 8f * scale;
        Paint.Fill(drawList, origin, end, fill, rounding);
        if (problems == LineProblems.None)
        {
            return;
        }

        var inset = 6f * scale;
        Paint.Fill(drawList, new Vector2(origin.X + 2f * scale, origin.Y + inset), new Vector2(origin.X + (2f + RailWidth) * scale, end.Y - inset), accent, RailWidth * scale * 0.5f);
    }

    private void DrawLineName(ShoppingLine line, int lineIndex, bool known, CatalogItem item, LineProblems problems, LineColumns columns, float top, float height)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var name = known ? item.Name : idTexts[lineIndex].Get(line.ItemId, static itemId => Loc.T(L.Lists.ItemFallback, (uint)itemId));
        var caption = ProblemCaption(problems) ?? string.Empty;
        var captionColor = ListValidation.IsError(problems) ? Styling.AccentRoseSoft : Styling.AccentAmberSoft;
        var nameHeight = ImGui.GetTextLineHeight();
        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
        }

        var hasCaption = caption.Length > 0;
        var blockHeight = hasCaption ? nameHeight + 2f * scale + captionHeight : nameHeight;
        var y = top + (height - blockHeight) * 0.5f;
        var nameMin = new Vector2(columns.Name, y);
        TextDraw.At(TextDraw.Truncate(name, columns.NameWidth), nameMin, Styling.TextStrong);
        if (hasCaption)
        {
            using (Fonts.PushCaption())
            {
                TextDraw.At(TextDraw.Truncate(caption, columns.NameWidth), new Vector2(columns.Name, y + nameHeight + 2f * scale), captionColor);
            }
        }

        if (Hit.HoveringRect(nameMin, new Vector2(columns.Name + columns.NameWidth, y + blockHeight)))
        {
            Tooltip.Show(NameTooltip(name, line.ItemId, problems));
        }
    }

    private static string? ProblemCaption(LineProblems problems)
    {
        if ((problems & LineProblems.NotMarketable) != 0)
        {
            return Loc.T(L.Lists.ProblemNotMarketable);
        }

        if ((problems & LineProblems.InvalidQuantity) != 0)
        {
            return Loc.T(L.Lists.ProblemQuantity);
        }

        if ((problems & LineProblems.InvalidUnitCap) != 0)
        {
            return Loc.T(L.Lists.ProblemCap);
        }

        if ((problems & LineProblems.InvalidExcess) != 0)
        {
            return Loc.T(L.Lists.ProblemExcess);
        }

        if ((problems & LineProblems.QualityUnavailable) != 0)
        {
            return Loc.T(L.Lists.ProblemQuality);
        }

        return (problems & LineProblems.Duplicate) != 0 ? Loc.T(L.Lists.ProblemDuplicate) : null;
    }

    // Built only while the pointer rests on the name, so the allocation never happens on an idle frame.
    private static string NameTooltip(string name, uint itemId, LineProblems problems)
    {
        var text = string.Concat(name, "\n#", itemId.ToString(CultureInfo.InvariantCulture));
        if ((problems & LineProblems.Duplicate) != 0)
        {
            text = string.Concat(text, "\n", Loc.T(L.Lists.MergeNote));
        }

        return text;
    }

    private void DrawQualityCell(ShoppingListStore store, ShoppingList list, ShoppingLine line, bool noHighQuality, Vector2 position)
    {
        ImGui.SetCursorScreenPos(position);
        var selected = (int)line.Quality;
        var mask = noHighQuality ? 1u << (int)ItemQuality.HighQuality : 0u;
        if (Dropdown.Draw("##quality", qualityLabels, ref selected, QualityWidth, 0f, false, null, mask))
        {
            line.Quality = (ItemQuality)selected;
            store.LineEdited(list);
        }

        var size = new Vector2(QualityWidth * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        if (Hit.HoveringRect(position, position + size) && !ImGui.IsAnyItemActive())
        {
            Tooltip.Show(Loc.T(noHighQuality ? L.Lists.NoHighQuality : L.Lists.QualityHelp));
        }
    }

    private static void DrawModeCell(ShoppingListStore store, ShoppingList list, ShoppingLine line, Vector2 position)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = ImGui.GetFrameHeight();
        var width = ModeWidth * scale;
        ImGui.SetCursorScreenPos(position);
        var selected = (int)line.Mode;
        if (Segmented.Draw("##mode", modeItems, ref selected, true, height / scale, width))
        {
            line.Mode = (QuantityMode)selected;
            store.LineEdited(list);
        }

        if (!Hit.HoveringRect(position, position + new Vector2(width, height)))
        {
            return;
        }

        var reach = ImGui.GetMousePos().X >= position.X + width * 0.5f;
        Tooltip.Show(reach
            ? string.Concat(Loc.T(L.Lists.ModeReach), "\n", Loc.T(L.Lists.ModeReachHelp))
            : string.Concat(Loc.T(L.Lists.ModeBuy), "\n", Loc.T(L.Lists.ModeBuyHelp)));
    }

    private static void DrawQuantityCell(ShoppingListStore store, ShoppingList list, ShoppingLine line, Vector2 position)
    {
        ImGui.SetCursorScreenPos(position);
        var quantity = line.Quantity;
        if (Stepper.Draw("##quantity", ref quantity, 1, ShoppingLimits.MinimumQuantity, ShoppingLimits.MaximumQuantity, "%d", QuantityWidth))
        {
            line.Quantity = quantity;
            store.LineEdited(list);
        }
    }

    private void DrawOwnedCell(InventoryReader inventory, ListAnalysis analysis, ShoppingLine line, int lineIndex, float x, float width, float top, float height)
    {
        var midY = top + height * 0.5f;
        var min = new Vector2(x, top);
        var max = new Vector2(x + width, top + height);
        if (!inventory.TryGetOwned(line.ItemId, out var owned))
        {
            var size = TextDraw.Measure(NoValue);
            TextDraw.At(NoValue, new Vector2(x + (width - size.X) * 0.5f, midY - size.Y * 0.5f), Styling.TextMuted);
            if (Hit.HoveringRect(min, max))
            {
                Tooltip.Show(Loc.T(L.Lists.OwnedUnknown));
            }

            return;
        }

        var ownedText = ownedTexts[lineIndex].Get(owned.Of(line.Quality), static value => ((int)value).ToString("N0", Loc.Culture));
        var ownedSize = TextDraw.Measure(ownedText);
        if (line.Mode == QuantityMode.Buy)
        {
            TextDraw.At(ownedText, new Vector2(x + (width - ownedSize.X) * 0.5f, midY - ownedSize.Y * 0.5f), Styling.TextSecondary);
        }
        else
        {
            var needed = analysis.Normalized.StillNeeded(lineIndex, owned);
            var needText = needTexts[lineIndex].Get(needed, static value => value == 0
                ? Loc.T(L.Lists.Done)
                : Loc.T(L.Lists.Need, ((int)value).ToString("N0", Loc.Culture)));
            using (Fonts.PushCaption())
            {
                var needSize = TextDraw.Measure(needText);
                var blockTop = midY - (ownedSize.Y + needSize.Y) * 0.5f;
                TextDraw.At(ownedText, new Vector2(x + (width - ownedSize.X) * 0.5f, blockTop), Styling.TextSecondary);
                TextDraw.At(TextDraw.Truncate(needText, width), new Vector2(x + (width - MathF.Min(needSize.X, width)) * 0.5f, blockTop + ownedSize.Y),
                    needed == 0 ? Styling.AccentMintSoft : Styling.AccentAmberSoft);
            }
        }

        if (Hit.HoveringRect(min, max))
        {
            Tooltip.Show(Loc.T(L.Lists.OwnedHelp));
        }
    }

    private void DrawNumberCell(ShoppingListStore store, ShoppingList list, ShoppingLine line, NumberColumn column, Vector2 position)
    {
        var unitCap = column == NumberColumn.UnitCap;
        var texts = unitCap ? capTexts : excessTexts;
        var value = unitCap ? line.MaxUnitPrice : line.ExcessAllowance;
        if (!texts.TryGetValue(line.Id, out var buffer))
        {
            buffer = new NumberText();
            texts[line.Id] = buffer;
        }

        if (buffer.Value != value)
        {
            buffer.Value = value;
            buffer.Text = FormatField(value);
        }

        ImGui.SetCursorScreenPos(position);
        var changed = unitCap
            ? NumberField.Draw("##cap", ref buffer.Text, Loc.T(L.Lists.CapHint), CapWidth, CapDigits, Loc.T(L.Lists.CapHelp))
            : NumberField.Draw("##excess", ref buffer.Text, ZeroHint, ExcessWidth, ExcessDigits, Loc.T(L.Lists.ExcessHelp));
        if (!changed)
        {
            return;
        }

        var maximum = unitCap ? ShoppingLimits.MaximumUnitPrice : ShoppingLimits.MaximumExcessAllowance;
        var status = ImportFields.TryParseWhole(buffer.Text, out var parsed);
        if (status == NumberStatus.Empty)
        {
            parsed = 0;
        }
        else if (status != NumberStatus.Valid || parsed > maximum)
        {
            buffer.Text = FormatField(buffer.Value);
            return;
        }

        if (parsed == value)
        {
            return;
        }

        buffer.Value = parsed;
        if (unitCap)
        {
            line.MaxUnitPrice = parsed;
        }
        else
        {
            line.ExcessAllowance = (int)parsed;
        }

        store.LineEdited(list);
    }

    private static string FormatField(long value) => value == 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
}

using System.Collections.Immutable;

namespace AutoShopper.Core.Planning;

internal static class PlanningProblemBuilder
{
    public static PlanningProblem Build(
        PlanDemand[] openLines,
        ImmutableArray<PlanOffer> offers,
        ImmutableArray<PlanWorld> worlds,
        ImmutableArray<string> excludedListingKeys,
        int offersPerWorldLimit,
        uint currentWorldId)
    {
        var limits = PlanSearchLimits.None;
        var scope = BuildScope(worlds);
        var priced = CollectOffers(offers, openLines, scope, BuildExclusions(excludedListingKeys), ref limits);
        var drafts = BuildDrafts(openLines, priced, offersPerWorldLimit, ref limits);
        var candidates = CollectCandidates(drafts, scope, ref limits);
        var items = new ItemProblem[drafts.Count];
        var usableOfferCount = 0;
        for (var index = 0; index < drafts.Count; index++)
        {
            var item = Finish(drafts[index], candidates);
            items[index] = item;
            usableOfferCount += item.Offers.Length;
        }

        return new PlanningProblem
        {
            Candidates = candidates,
            Items = items,
            CurrentWorldIndex = IndexOf(candidates, currentWorldId),
            Limits = limits,
            UsableOfferCount = usableOfferCount,
        };
    }

    public static bool IsEligible(PlanDemand line, PlanOffer offer, long payable)
    {
        return line.Key.Accepts(offer.IsHq)
            && offer.Quantity <= line.MaximumTotal
            && line.AllowsStackCost(payable, offer.Quantity);
    }

    public static int IndexOf(PlanWorld[] candidates, uint worldId)
    {
        var low = 0;
        var high = candidates.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var middleId = candidates[middle].WorldId;
            if (middleId == worldId)
            {
                return middle;
            }

            if (middleId < worldId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    private static Dictionary<uint, PlanWorld> BuildScope(ImmutableArray<PlanWorld> worlds)
    {
        var scope = new Dictionary<uint, PlanWorld>();
        if (worlds.IsDefault)
        {
            return scope;
        }

        for (var index = 0; index < worlds.Length; index++)
        {
            var world = worlds[index];
            if (world.WorldId == 0)
            {
                continue;
            }

            if (scope.TryGetValue(world.WorldId, out var existing) && CompareWorldRecords(existing, world) <= 0)
            {
                continue;
            }

            scope[world.WorldId] = world;
        }

        return scope;
    }

    private static int CompareWorldRecords(PlanWorld left, PlanWorld right)
    {
        var order = left.DataCenterId.CompareTo(right.DataCenterId);
        if (order != 0)
        {
            return order;
        }

        order = left.RegionId.CompareTo(right.RegionId);
        if (order != 0)
        {
            return order;
        }

        return string.CompareOrdinal(left.Name, right.Name);
    }

    private static HashSet<string> BuildExclusions(ImmutableArray<string> excludedListingKeys)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        if (excludedListingKeys.IsDefault)
        {
            return excluded;
        }

        for (var index = 0; index < excludedListingKeys.Length; index++)
        {
            var key = excludedListingKeys[index];
            if (!string.IsNullOrEmpty(key))
            {
                excluded.Add(key);
            }
        }

        return excluded;
    }

    private static List<PricedOffer> CollectOffers(
        ImmutableArray<PlanOffer> offers,
        PlanDemand[] openLines,
        Dictionary<uint, PlanWorld> scope,
        HashSet<string> excluded,
        ref PlanSearchLimits limits)
    {
        var collected = new List<PricedOffer>();
        if (offers.IsDefault)
        {
            return collected;
        }

        var demandedItems = new HashSet<uint>();
        for (var index = 0; index < openLines.Length; index++)
        {
            demandedItems.Add(openLines[index].ItemId);
        }

        for (var index = 0; index < offers.Length; index++)
        {
            var offer = offers[index];
            if (string.IsNullOrEmpty(offer.ListingKey))
            {
                limits |= PlanSearchLimits.InvalidOffersDropped;
                continue;
            }

            if (excluded.Contains(offer.ListingKey) || !demandedItems.Contains(offer.ItemId) || !scope.TryGetValue(offer.WorldId, out var world))
            {
                continue;
            }

            if (!offer.TryGetPayable(out var payable))
            {
                limits |= HasValidParts(offer) ? PlanSearchLimits.ArithmeticOverflow : PlanSearchLimits.InvalidOffersDropped;
                continue;
            }

            collected.Add(new PricedOffer(offer with { DataCenterId = world.DataCenterId }, payable));
        }

        collected.Sort(OfferOrder.CompareFreshestFirst);
        RemoveDuplicateListings(collected);
        collected.Sort(OfferOrder.Compare);
        return collected;
    }

    private static bool HasValidParts(PlanOffer offer)
    {
        return offer.Quantity > 0 && offer.UnitPrice >= 0 && offer.Fee >= 0;
    }

    private static void RemoveDuplicateListings(List<PricedOffer> sorted)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var write = 0;
        for (var read = 0; read < sorted.Count; read++)
        {
            var candidate = sorted[read];
            if (!seen.Add(candidate.Offer.ListingKey))
            {
                continue;
            }

            sorted[write] = candidate;
            write++;
        }

        sorted.RemoveRange(write, sorted.Count - write);
    }

    private static List<ItemDraft> BuildDrafts(PlanDemand[] openLines, List<PricedOffer> priced, int offersPerWorldLimit, ref PlanSearchLimits limits)
    {
        var drafts = new List<ItemDraft>();
        var offerStart = 0;
        var lineStart = 0;
        while (lineStart < openLines.Length)
        {
            var itemId = openLines[lineStart].ItemId;
            var lineEnd = lineStart + 1;
            while (lineEnd < openLines.Length && openLines[lineEnd].ItemId == itemId)
            {
                lineEnd++;
            }

            while (offerStart < priced.Count && priced[offerStart].Offer.ItemId < itemId)
            {
                offerStart++;
            }

            var offerEnd = offerStart;
            while (offerEnd < priced.Count && priced[offerEnd].Offer.ItemId == itemId)
            {
                offerEnd++;
            }

            var lines = InAllocationOrder(openLines, lineStart, lineEnd);
            var scopes = MeasureScopes(lines, priced, offerStart, offerEnd);
            var kept = KeepUsefulOffers(lines, priced, offerStart, offerEnd, offersPerWorldLimit, ref limits);
            drafts.Add(new ItemDraft(itemId, lines, scopes, kept));
            offerStart = offerEnd;
            lineStart = lineEnd;
        }

        return drafts;
    }

    private static PlanDemand[] InAllocationOrder(PlanDemand[] openLines, int start, int end)
    {
        var lines = new PlanDemand[end - start];
        Array.Copy(openLines, start, lines, 0, lines.Length);
        Array.Sort(lines, static (left, right) => AllocationRank(left.Quality).CompareTo(AllocationRank(right.Quality)));
        return lines;
    }

    private static int AllocationRank(DemandQuality quality)
    {
        return quality switch
        {
            DemandQuality.HighQuality => 0,
            DemandQuality.NormalQuality => 1,
            _ => 2,
        };
    }

    private static LineScope[] MeasureScopes(PlanDemand[] lines, List<PricedOffer> priced, int start, int end)
    {
        var scopes = new LineScope[lines.Length];
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            long matching = 0;
            long withinCap = 0;
            for (var index = start; index < end; index++)
            {
                var candidate = priced[index];
                var offer = candidate.Offer;
                if (!line.Key.Accepts(offer.IsHq))
                {
                    continue;
                }

                matching += offer.Quantity;
                if (!line.AllowsStackCost(candidate.Payable, offer.Quantity))
                {
                    continue;
                }

                withinCap += offer.Quantity;
            }

            scopes[lineIndex] = new LineScope(matching, withinCap);
        }

        return scopes;
    }

    private static List<PricedOffer> KeepUsefulOffers(
        PlanDemand[] lines,
        List<PricedOffer> priced,
        int start,
        int end,
        int offersPerWorldLimit,
        ref PlanSearchLimits limits)
    {
        var kept = new List<PricedOffer>();
        var groupStart = start;
        while (groupStart < end)
        {
            var first = priced[groupStart].Offer;
            var groupEnd = groupStart + 1;
            while (groupEnd < end && priced[groupEnd].Offer.WorldId == first.WorldId && priced[groupEnd].Offer.IsHq == first.IsHq)
            {
                groupEnd++;
            }

            var need = QuantityNeededFrom(lines, first.IsHq);
            var keptCount = 0;
            long keptQuantity = 0;
            for (var index = groupStart; index < groupEnd; index++)
            {
                var candidate = priced[index];
                if (!IsUsefulForAnyLine(lines, candidate))
                {
                    continue;
                }

                if (keptCount >= offersPerWorldLimit && keptQuantity >= need)
                {
                    limits |= PlanSearchLimits.OffersPruned;
                    continue;
                }

                kept.Add(candidate);
                keptCount++;
                keptQuantity += candidate.Offer.Quantity;
            }

            groupStart = groupEnd;
        }

        return kept;
    }

    private static long QuantityNeededFrom(PlanDemand[] lines, bool isHq)
    {
        long need = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Key.Accepts(isHq))
            {
                need += lines[index].MaximumTotal;
            }
        }

        return need;
    }

    private static bool IsUsefulForAnyLine(PlanDemand[] lines, PricedOffer candidate)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            if (IsEligible(lines[index], candidate.Offer, candidate.Payable))
            {
                return true;
            }
        }

        return false;
    }

    private static PlanWorld[] CollectCandidates(List<ItemDraft> drafts, Dictionary<uint, PlanWorld> scope, ref PlanSearchLimits limits)
    {
        var worldIds = new List<uint>();
        for (var draftIndex = 0; draftIndex < drafts.Count; draftIndex++)
        {
            var kept = drafts[draftIndex].Kept;
            for (var index = 0; index < kept.Count; index++)
            {
                worldIds.Add(kept[index].Offer.WorldId);
            }
        }

        worldIds.Sort();
        var distinct = new List<uint>();
        for (var index = 0; index < worldIds.Count; index++)
        {
            if (distinct.Count == 0 || distinct[^1] != worldIds[index])
            {
                distinct.Add(worldIds[index]);
            }
        }

        if (distinct.Count > WorldMasks.Capacity)
        {
            limits |= PlanSearchLimits.CandidateWorldsTruncated;
            distinct.RemoveRange(WorldMasks.Capacity, distinct.Count - WorldMasks.Capacity);
        }

        var candidates = new PlanWorld[distinct.Count];
        for (var index = 0; index < distinct.Count; index++)
        {
            candidates[index] = scope[distinct[index]];
        }

        return candidates;
    }

    private static ItemProblem Finish(ItemDraft draft, PlanWorld[] candidates)
    {
        var kept = draft.Kept;
        var offers = new List<PlanOffer>(kept.Count);
        var payables = new List<long>(kept.Count);
        var worldIndexes = new List<int>(kept.Count);
        var mask = UInt128.Zero;
        for (var index = 0; index < kept.Count; index++)
        {
            var worldIndex = IndexOf(candidates, kept[index].Offer.WorldId);
            if (worldIndex < 0)
            {
                continue;
            }

            offers.Add(kept[index].Offer);
            payables.Add(kept[index].Payable);
            worldIndexes.Add(worldIndex);
            mask |= WorldMasks.Bit(worldIndex);
        }

        var lines = draft.Lines;
        var eligible = new bool[lines.Length][];
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var flags = new bool[offers.Count];
            for (var offerIndex = 0; offerIndex < offers.Count; offerIndex++)
            {
                flags[offerIndex] = IsEligible(lines[lineIndex], offers[offerIndex], payables[offerIndex]);
            }

            eligible[lineIndex] = flags;
        }

        return new ItemProblem
        {
            ItemId = draft.ItemId,
            Lines = lines,
            Scopes = draft.Scopes,
            Offers = offers.ToArray(),
            Payables = payables.ToArray(),
            WorldIndexes = worldIndexes.ToArray(),
            Eligible = eligible,
            WorldMask = mask,
        };
    }

    private sealed record ItemDraft(uint ItemId, PlanDemand[] Lines, LineScope[] Scopes, List<PricedOffer> Kept);
}

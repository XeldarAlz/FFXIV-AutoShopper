namespace AutoShopper.Core.Planning;

internal readonly record struct PricedOffer(PlanOffer Offer, long Payable);

internal static class OfferOrder
{
    public static int CompareUnitCost(long leftPayable, int leftQuantity, long rightPayable, int rightQuantity)
    {
        return ((Int128)leftPayable * rightQuantity).CompareTo((Int128)rightPayable * leftQuantity);
    }

    public static int Compare(PricedOffer left, PricedOffer right)
    {
        var leftOffer = left.Offer;
        var rightOffer = right.Offer;
        var order = leftOffer.ItemId.CompareTo(rightOffer.ItemId);
        if (order != 0)
        {
            return order;
        }

        order = leftOffer.WorldId.CompareTo(rightOffer.WorldId);
        if (order != 0)
        {
            return order;
        }

        order = leftOffer.IsHq.CompareTo(rightOffer.IsHq);
        if (order != 0)
        {
            return order;
        }

        order = CompareUnitCost(left.Payable, leftOffer.Quantity, right.Payable, rightOffer.Quantity);
        if (order != 0)
        {
            return order;
        }

        order = leftOffer.Quantity.CompareTo(rightOffer.Quantity);
        if (order != 0)
        {
            return order;
        }

        order = string.CompareOrdinal(leftOffer.ListingKey, rightOffer.ListingKey);
        if (order != 0)
        {
            return order;
        }

        order = rightOffer.ObservedAtUtc.CompareTo(leftOffer.ObservedAtUtc);
        if (order != 0)
        {
            return order;
        }

        order = leftOffer.UnitPrice.CompareTo(rightOffer.UnitPrice);
        if (order != 0)
        {
            return order;
        }

        return leftOffer.IsEstimate.CompareTo(rightOffer.IsEstimate);
    }

    public static int CompareFreshestFirst(PricedOffer left, PricedOffer right)
    {
        var order = string.CompareOrdinal(left.Offer.ListingKey, right.Offer.ListingKey);
        if (order != 0)
        {
            return order;
        }

        order = right.Offer.ObservedAtUtc.CompareTo(left.Offer.ObservedAtUtc);
        if (order != 0)
        {
            return order;
        }

        order = left.Offer.IsEstimate.CompareTo(right.Offer.IsEstimate);
        if (order != 0)
        {
            return order;
        }

        return Compare(left, right);
    }

    public static int CompareLines(PlanLine left, PlanLine right)
    {
        var order = left.Demand.CompareTo(right.Demand);
        if (order != 0)
        {
            return order;
        }

        order = CompareUnitCost(left.Payable, left.Offer.Quantity, right.Payable, right.Offer.Quantity);
        if (order != 0)
        {
            return order;
        }

        order = left.Offer.Quantity.CompareTo(right.Offer.Quantity);
        if (order != 0)
        {
            return order;
        }

        return string.CompareOrdinal(left.Offer.ListingKey, right.Offer.ListingKey);
    }
}

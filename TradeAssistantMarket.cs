using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Components.Store.Internal;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using System.Collections.Generic;
using System.Linq;

namespace TradeAssistant
{
    /// <summary>Aggregated market (exchange) statistics for an item across all player stores.</summary>
    public readonly struct MarketStats
    {
        public int OfferCount { get; }
        public float TotalQuantity { get; }
        public float TotalValue { get; }
        public float MinPrice { get; }
        public float MaxPrice { get; }

        public MarketStats(int offerCount, float totalQuantity, float totalValue, float minPrice, float maxPrice)
        {
            OfferCount = offerCount;
            TotalQuantity = totalQuantity;
            TotalValue = totalValue;
            MinPrice = minPrice;
            MaxPrice = maxPrice;
        }

        /// <summary>Quantity-weighted average price of all offers, or the plain average when no quantities are available.</summary>
        public float AveragePrice => TotalQuantity > 0f ? TotalValue / TotalQuantity : (OfferCount > 0 ? TotalValue / OfferCount : 0f);
    }

    /// <summary>
    /// Reads the current market (exchange) by aggregating the sell/buy orders of every player store in the world.
    /// These aggregated prices are used as the pricing baseline for the exchange integration commands.
    /// </summary>
    public static class TradeAssistantMarket
    {
        public static Dictionary<int, MarketStats> ComputeMarket(bool sellOffers)
        {
            var running = new Dictionary<int, (int count, float qty, float value, float min, float max)>();
            foreach (var store in WorldObjectUtil.AllObjsWithComponent<StoreComponent>())
            {
                var offers = sellOffers ? store.StoreData.SellOffers : store.StoreData.BuyOffers;
                foreach (var offer in offers)
                {
                    var quantity = offer.Stack?.Quantity ?? 0;
                    foreach (var typeID in offer.OfferedItemTypeIDs())
                    {
                        if (!running.TryGetValue(typeID, out var stats))
                            stats = (0, 0f, 0f, float.PositiveInfinity, float.NegativeInfinity);
                        stats.count++;
                        stats.qty += quantity;
                        stats.value += offer.Price * quantity;
                        if (offer.Price < stats.min) stats.min = offer.Price;
                        if (offer.Price > stats.max) stats.max = offer.Price;
                        running[typeID] = stats;
                    }
                }
            }

            return running.ToDictionary(
                kvp => kvp.Key,
                kvp => new MarketStats(kvp.Value.count, kvp.Value.qty, kvp.Value.value, kvp.Value.min, kvp.Value.max));
        }

        public static LocString Describe(MarketStats stats)
        {
            return Localizer.Do($"{Text.StyledNum(stats.AveragePrice)} avg / {Text.StyledNum(stats.MinPrice)} min / {Text.StyledNum(stats.MaxPrice)} max ({Text.StyledNum(stats.OfferCount)} offer(s))");
        }
    }
}

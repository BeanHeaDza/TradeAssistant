using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Components.Store.Internal;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using System;
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
        // 1.3.2: cache the world component scan for a short TTL instead of re-scanning
        // AllObjsWithComponent on every user's every calculation.
        private static readonly object StoreCacheLock = new();
        private static List<StoreComponent>? _storeCache;
        private static DateTime _storeCacheUtc = DateTime.MinValue;
        private static readonly TimeSpan StoreCacheTtl = TimeSpan.FromSeconds(30);

        /// <summary>
        /// All player stores that are usable for market statistics: enabled, with a currency and a
        /// StoreData component. Disabled / unpowered / currency-less stores are excluded (1.3.1).
        /// </summary>
        private static List<StoreComponent> GetEnabledStores()
        {
            var now = DateTime.UtcNow;
            if (_storeCache == null || now - _storeCacheUtc > StoreCacheTtl)
            {
                lock (StoreCacheLock)
                {
                    if (_storeCache == null || now - _storeCacheUtc > StoreCacheTtl)
                    {
                        _storeCache = WorldObjectUtil.AllObjsWithComponent<StoreComponent>()
                            .Where(s => s.StoreData != null && s.Enabled && s.Currency != null)
                            .ToList();
                        _storeCacheUtc = now;
                    }
                }
            }
            return _storeCache;
        }

        /// <summary>Aggregates the sell/buy orders of every enabled player store (optionally excluding
        /// one store, 1.1.1, so competitive pricing never follows the store's own orders).</summary>
        public static Dictionary<int, MarketStats> ComputeMarket(bool sellOffers, StoreComponent? excludeStore = null)
        {
            var running = new Dictionary<int, (int count, float qty, float value, float min, float max)>();
            foreach (var store in GetEnabledStores())
            {
                if (excludeStore != null && ReferenceEquals(store, excludeStore)) continue;

                var offers = sellOffers ? store.StoreData.SellOffers : store.StoreData.BuyOffers;
                foreach (var offer in offers)
                {
                    // 1.2.4: offers without a stack (special-request / tag offers) have no
                    // observable quantity; skipping them keeps the average from being distorted
                    // by 0-quantity rows.
                    var quantity = offer.Stack?.Quantity ?? 0;
                    if (quantity <= 0f) continue;
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

        /// <summary>Returns every observed price per item (one entry per offer), sorted ascending.
        /// Used to compute the median sell price for the automatic buy price feature.</summary>
        public static Dictionary<int, List<float>> ComputeMarketPriceLists(bool sellOffers)
        {
            var result = new Dictionary<int, List<float>>();
            foreach (var store in GetEnabledStores())
            {
                var offers = sellOffers ? store.StoreData.SellOffers : store.StoreData.BuyOffers;
                foreach (var offer in offers)
                {
                    var price = offer.Price;
                    foreach (var typeID in offer.OfferedItemTypeIDs())
                    {
                        if (!result.TryGetValue(typeID, out var list))
                        {
                            list = new List<float>();
                            result[typeID] = list;
                        }
                        list.Add(price);
                    }
                }
            }
            foreach (var kvp in result)
                kvp.Value.Sort();
            return result;
        }

        /// <summary>Median of a sorted price list, or 0 when the list is empty.</summary>
        public static float MedianPrice(List<float> prices)
        {
            if (prices == null || prices.Count == 0)
                return 0f;
            var mid = prices.Count / 2;
            return prices.Count % 2 == 1 ? prices[mid] : (prices[mid - 1] + prices[mid]) / 2f;
        }
    }
}

using Eco.Core.Plugins;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Economy;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Gameplay.StrangeCloudGameplay;
using Eco.Gameplay.Systems;
using Eco.Gameplay.Systems.EcoMarketplace;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Gameplay.Systems.TextLinks;
using Eco.Gameplay.Utils;
using Eco.Shared;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TradeAssistant
{
    /// <summary>Result of one re-pricing pass, used by both the /ta update command and the automatic re-pricer.</summary>
    public sealed record RepricingResult(int SellUpdates, int BuyUpdates, int DiscountedUpdates)
    {
        public int TotalUpdates => SellUpdates + BuyUpdates + DiscountedUpdates;
    }

    public static class TradeAssistantRepricing
    {
        /// <summary>
        /// Core re-pricing logic shared by the /ta update command and the automatic re-pricer.
        /// When <paramref name="output"/> is provided, the detailed per-order messages are written to it
        /// (matching the previous /ta update behaviour). When it is null the pass runs silently and only
        /// the result counts are returned.
        /// </summary>
        public static RepricingResult RunUpdate(TradeAssistantCalculator calc, StringBuilder? output)
        {
            var storeSellItemIds = calc.Store.StoreData.SellOffers
                .SelectMany(o => o.OfferedItemTypeIDs())
                .Where(typeID => !calc.Config.FrozenSellPrices.Contains(typeID))
                .Distinct()
                .ToList();
            if (storeSellItemIds.Count == 0)
            {
                output?.AppendLineLoc($"{calc.Store.Parent.UILink()} has no sell orders.");
                return new RepricingResult(0, 0, 0);
            }
            var buyProductsToUpdate = calc.Store.StoreData.BuyOffers
                .SelectMany(o => o.OfferedItemTypeIDs())
                .Distinct()
                .Where(storeSellItemIds.Contains)
                .ToList();

            // Items placed in a store category named "SOON TO EXPIRE" are updated separately:
            // they get the player-configured discount percentage applied on top of the normal price.
            // Only offers whose freshness qualifiers (min/max durability, 0..100) fall inside the
            // configured [min, max] freshness window get the discount; fresh or unrestricted offers
            // in that category are priced normally.
            var soonToExpireItemIds = calc.Store.StoreData.SellCategories
                .Where(c => !c.IsBuy && string.Equals(c.Name?.Trim(), "SOON TO EXPIRE", StringComparison.OrdinalIgnoreCase))
                .SelectMany(c => c.Offers)
                .Where(o => o.HasQualifiers && o.MinDurability >= calc.Config.SoonToExpireMinFreshness && o.MaxDurability <= calc.Config.SoonToExpireMaxFreshness)
                .SelectMany(o => o.OfferedItemTypeIDs())
                .Where(storeSellItemIds.Contains)
                .ToHashSet();
            var normalItemIds = storeSellItemIds.Where(id => !soonToExpireItemIds.Contains(id)).ToList();

            var byProducts = calc.Config.ByProducts.ToHashSet();
            var craftableItems = calc.CraftableItems.SelectMany(x => x.Value).Select(x => x.TypeID).Distinct().ToHashSet();
            var updates = new List<LocString>();
            var discountedUpdates = new List<LocString>();
            var warnings = new List<LocString>();
            var buyUpdates = 0;

            void UpdateSellItem(int itemID, float discountPercent)
            {
                if (calc.TryGetCostPrice(itemID, out var price, out var reason, out var itemWarnings))
                {
                    // SellPrice = Tax + CostPrice * (1 + Profit)
                    // SellPrice = SellPrice * TaxRate + CostPrice * (1 + Profit)
                    // SellPrice - SellPrice * TaxRate = CostPrice * (1 + Profit)
                    // SellPrice * (1 - TaxRate) = CostPrice * (1 + Profit)
                    // SellPrice = CostPrice * (1 + Profit) / (1 - TaxRate)
                    var normalPrice = price * (1 + calc.Config.Profit / 100f) / (1 - calc.Store.GetTax());
                    var discount = Math.Max(discountPercent, calc.DiscountPercentFor(itemID));
                    var newPrice = Mathf.RoundToAcceptedDigits(normalPrice * (1 - discount / 100f));
                    if (calc.Config.DurabilityPricingEnabled)
                    {
                        foreach (var offer in calc.Store.StoreData.SellOffers.Where(o => o.MatchesTypeID(itemID)).ToList())
                            (discount > 0 ? discountedUpdates : updates).AddRange(calc.SetSellOfferPrice(offer, newPrice));
                    }
                    else
                        (discount > 0 ? discountedUpdates : updates).AddRange(calc.SetSellPrice(itemID, newPrice));
                    if (itemWarnings != null)
                        warnings.AddRange(itemWarnings);
                }
                else if (!byProducts.Contains(itemID) && craftableItems.Contains(itemID))
                    output?.AppendLineLoc($"Failed to get the cost price of {Item.Get(itemID).UILink()} ({TextLoc.FoldoutLoc($"Why?", $"Why {Item.Get(itemID).UILink()}", reason.ToStringLoc())}).");
            }

            // 1) Regular items: priced with the configured profit percentage.
            foreach (var itemID in normalItemIds.OrderBy(p => byProducts.Contains(p) ? 0 : 1))
                UpdateSellItem(itemID, 0f);

            // 2) "SOON TO EXPIRE" items: same cost basis, then the configured discount is applied.
            foreach (var itemID in soonToExpireItemIds.OrderBy(p => byProducts.Contains(p) ? 0 : 1))
                UpdateSellItem(itemID, calc.Config.SoonToExpireDiscount);

            foreach (var itemID in buyProductsToUpdate)
                if (calc.TryGetCostPrice(itemID, out var price, out var _, out var _))
                {
                    updates.AddRange(calc.SetBuyPrice(itemID, price));
                    buyUpdates++;
                }

            // TODO: I'm not happy with how warnings are working at the moment. Removing them for now.
            //if (warnings.Any())
            //    output.AppendLine(warnings.Select(w => "- " + w).Distinct().FoldoutListLoc("warning", Eco.Shared.Items.TooltipOrigin.None));

            if (output != null)
            {
                if (updates.Count == 0 && discountedUpdates.Count == 0 && output.Length == 0)
                    output.AppendLineLoc($"All prices are up to date!");
                else
                {
                    output.Insert(0, Localizer.Do($"Updating the sell prices at {calc.Store.Parent.UILink()}\n"));
                    updates
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .ForEach(u => output.AppendLine(u));
                    if (discountedUpdates.Count > 0)
                    {
                        output.AppendLineLoc($"Applied the \"SOON TO EXPIRE\" discount of {Text.StyledNum(calc.Config.SoonToExpireDiscount)}% to {Text.StyledNum(discountedUpdates.Count)} order(s):");
                        discountedUpdates
                            .Where(u => !string.IsNullOrWhiteSpace(u))
                            .ForEach(u => output.AppendLine(u));
                    }
                    if (updates.Count + discountedUpdates.Count > 0)
                        output.AppendLineLoc($"Updated the price(s) of {Text.StyledNum(updates.Count + discountedUpdates.Count)} order(s)");
                }
            }

            return new RepricingResult(updates.Count, buyUpdates, discountedUpdates.Count);
        }
    }
}

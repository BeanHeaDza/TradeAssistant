using Eco.Core.Plugins;
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
using System.Text;

namespace TradeAssistant
{
    [ChatCommandHandler]
    public static class TradeAssistantCommandHandler
    {
        [ChatCommand("Shows commands available from the trade assistant mod", "ta")]
        public static void TradeAssistant() { }


        [ChatSubCommand(nameof(TradeAssistant), "READ ME FIRST!", ChatAuthorizationLevel.User)]
        public static void Help(User user)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Localizer.DoStr("Welcome to the Trade Assistant mod!"));
            sb.AppendLine(Localizer.DoStr("This mod will help you setup your store by adding buy and sell orders onto it and updating the sell order prices based on your profit configurations you've setup."));
            sb.Append(Localizer.DoStr("- The first step is to run ")).Append(Text.Name("/ta setupsell")).AppendLine(Localizer.DoStr(", this will add all the items you're able to craft from the workbenches on this property to the shop sell orders. Go through this list and remove the items you're not interested in selling. Don't worry about updating the prices, the mod will do this for you later!"));
            sb.Append(Localizer.DoStr("- Next up run the ")).Append(Text.Name("/ta setupbuy")).AppendLine(" command. This will go through the list of sell orders and add all the possible input items you need to craft these in the buy orders.");
            sb.AppendLine(Localizer.DoStr("- Now go through your buy orders and set the buy prices as well as the buy limits for each item.")); // TODO: Mention how you can remove Tagged items
            sb.Append(Localizer.DoStr("- Run ")).Append(Text.Name("/ta config")).AppendLine(Localizer.DoStr(" to set your desired profit percentage and cost per calory."));
            sb.AppendLine(Localizer.DoStr("- OPTIONAL: create a category called \"SOON TO EXPIRE\" in your store and add items to it, and set a freshness qualifier (min/max) on those offers so only expiring food qualifies. Set the discount percentage with /ta config, and /ta update will apply that discount to those items on top of the normal price."));
            sb.Append(Localizer.DoStr("- Once you're done with that the last step is to run ")).Append(Text.Name("/ta update")).AppendLine(Localizer.DoStr(", this will update the prices of all your sell orders based on your configured profit percentage and labor cost."));
            sb.AppendLine(Localizer.DoStr("- Optional: run /ta explainbuy <itemName> to see exactly how the buy price of an item is calculated, including the cost price breakdown."));
            sb.AppendLine(Localizer.DoStr("- Optional: use /ta discount <categoryName> <percent> to apply a discount to a whole sell category (e.g. /ta discount Tools 10). Use /ta discount list to review and /ta discount remove <categoryName> to clear one."));
            sb.AppendLine(Localizer.DoStr("- Optional: run /ta autoreprice on to re-price your store automatically every N minutes (default 30) and whenever market activity or store offers change. Use /ta autoreprice interval <minutes> to change the interval, /ta autoreprice status to check and /ta autoreprice off to disable."));
            sb.AppendLine(Localizer.DoStr("- Optional: run /ta autobuyprices to recalculate your buy order prices from your sell prices and profit, so you never buy ingredients that eat your profit. Pass an item name to only recalculate that item: /ta autobuyprices <itemName>."));

            user.TempServerMessage(sb);
        }

        [ChatSubCommand(nameof(TradeAssistant), "Add sell orders for all the products you are able to craft in this property's Crafting Tables.", ChatAuthorizationLevel.User)]
        public static async Task SetupSell(User user)
        {
            var calc =await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null)
                return;
            
            var sb = new StringBuilder();

            var soldItems = calc.Store.StoreData.SellOffers.SelectMany(o => o.OfferedItemTypeIDs()).ToHashSet();
            var plannedAdds = new HashSet<int>();
            var tableToItemsToAdd = new List<(LocString table, List<int> itemIds)>();

            foreach (var (table, tableItems) in calc.CraftableItems)
            {
                var uniqueForThisTable = tableItems
                    .Select(i => i.TypeID)
                    .Where(i => StrangeWorldsPlugin.Obj.Config.AllowPaidItemsInPlayerStores || !i.IsPaidItem())
                    .Where(i => !soldItems.Contains(i))
                    .Where(plannedAdds.Add)
                    .ToList();

                if (uniqueForThisTable.Count > 0)
                    tableToItemsToAdd.Add((table, uniqueForThisTable));
            }

            var itemsToAdd = plannedAdds.ToList();

            if (!itemsToAdd.Any())
            {
                sb.AppendLine(Localizer.DoStr($"All the items you can craft are already added to this store."));
                user.TempServerMessage(sb);
                return;
            }

            foreach (var (table, itemIds) in tableToItemsToAdd)
            {
                calc.Store.CreateCategoryWithOffers(user.Player, itemIds, false);
                var category = calc.Store.StoreData.SellCategories.Last();
                category.Name = table;
            }
            foreach (var offer in calc.Store.StoreData.SellOffers.Where(o => itemsToAdd.Any(o.MatchesTypeID)))
                offer.Price = 999999;

            sb.AppendLine(Localizer.DoStr($"Added {Text.Info(Text.Num(itemsToAdd.Count))} sell orders. Open the store and remove the items you're not interested in selling at your shop."));
            user.TempServerMessage(sb);
        }

        [ChatSubCommand(nameof(TradeAssistant), "Add buy orders for all the ingredients you need to make the products you're selling", ChatAuthorizationLevel.User)]
        public static async Task SetupBuy(User user)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null)
                return;

            var sb = new StringBuilder();

            var items = new HashSet<int>();

            // Build up a lookup of product to required ingredients
            var allRecipes = calc.ProductToRequiredItemsLookup();

            // Limit the products to only the ones we're selling
            var sellOfferTypeIds = calc.Store.StoreData.SellOffers.SelectMany(o => o.OfferedItemTypeIDs()).ToHashSet();

            // Work through the list of craftable items and find the product we need to make them
            var todo = new Queue<int>(calc.CraftableItems.SelectMany(x => x.Value).Select(p => p.TypeID).Distinct().Where(sellOfferTypeIds.Contains));
            var done = new HashSet<int>();
            while (todo.TryDequeue(out var productId))
            {
                if (!done.Add(productId)) continue;
                if (!allRecipes.TryGetValue(productId, out var recipeIngredients))
                    continue;

                var itemsToQueue = recipeIngredients.Where(allRecipes.ContainsKey).ToList();
                var itemsToBuy = recipeIngredients.Where(i => !itemsToQueue.Contains(i));
                items.AddRange(itemsToBuy);
                itemsToQueue.ForEach(todo.Enqueue);
            }

            var existingBuyOrderTypeIds = calc.Store.StoreData.BuyOffers.SelectMany(o => o.OfferedItemTypeIDs()).ToHashSet();
            var existingOrderTypeIds = existingBuyOrderTypeIds.Union(sellOfferTypeIds).ToHashSet();
            var duplicateOrderCount = items.RemoveWhere(existingOrderTypeIds.Contains);
            if (items.Count == 0)
            {
                if (duplicateOrderCount > 0)
                    user.TempServerMessage(Localizer.DoStr($"No new buy orders were added because all required ingredients already exist as store orders."));
                else
                    user.TempServerMessage(Localizer.DoStr($"All buy orders to make the current sell order products are already listed in this shop."));
                return;
            }


            calc.Store.CreateCategoryWithOffers(user.Player, items.ToList(), true);
            foreach (var offer in calc.Store.StoreData.BuyOffers.Where(o => items.Any(o.MatchesTypeID)))
            {
                offer.Limit = 1;
                offer.Price = 0;
            }
            user.TempServerMessage(Localizer.DoStr($"{Text.Info(Text.Num(items.Count))} buy orders were added, please open the shop and set the Price and Limit for each of them."));
        }


        [ChatSubCommand(nameof(TradeAssistant), "Update the sell price of everything in the store based on the configured profit and cost per calory", ChatAuthorizationLevel.User)]
        public static async Task Update(User user)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var output = new StringBuilder();
            TradeAssistantRepricing.RunUpdate(calc, output);
            user.TempServerMessage(output.ToStringLoc());
        }

        [ChatSubCommand(nameof(TradeAssistant), "Enables, disables or shows the status of automatic re-pricing. Usage: /ta autoreprice <on|off|status|interval <minutes>>", ChatAuthorizationLevel.User)]
        public static async Task AutoReprice(User user, string action, string? value = null)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var config = calc.Config;
            if (action.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                config.AutoRepricingEnabled = true;
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                user.TempServerMessage(Localizer.Do($"Automatic re-pricing is now ON. Prices will be re-calculated every {Text.StyledNum(config.AutoRepricingIntervalMinutes)} minute(s) and after market changes. Run /ta update to apply it immediately."));
            }
            else if (action.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                config.AutoRepricingEnabled = false;
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                user.TempServerMessage(Localizer.DoStr("Automatic re-pricing is now OFF."));
            }
            else if (action.Equals("interval", StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes) || minutes < 1f || minutes > 1440f)
                {
                    user.TempServerMessage(Localizer.DoStr("Please provide an interval between 1 and 1440 minutes."));
                    return;
                }
                config.AutoRepricingIntervalMinutes = minutes;
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                user.TempServerMessage(Localizer.Do($"Automatic re-pricing will now run every {Text.StyledNum(minutes)} minute(s)."));
            }
            else
                user.TempServerMessage(Localizer.Do($"Automatic re-pricing is {(config.AutoRepricingEnabled ? "ON" : "OFF")}, running every {Text.StyledNum(config.AutoRepricingIntervalMinutes)} minute(s). Usage: /ta autoreprice <on|off|status|interval <minutes>>"));
        }

        [ChatSubCommand(nameof(TradeAssistant), "Automatically calculates the buy price of each buy order from your sell prices and configured profit, so you never buy ingredients that eat your profit. Usage: /ta autobuyprices [itemName]", ChatAuthorizationLevel.User)]
        public static async Task AutoBuyPrices(User user, string? itemName = null)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var itemIDs = calc.Store.StoreData.BuyOffers.SelectMany(o => o.OfferedItemTypeIDs()).Distinct().ToList();
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                var match = itemIDs.Select(Item.Get).Where(i => i != null)
                    .FirstOrDefault(i => i.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase) || i.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    user.TempServerMessage(Localizer.Do($"No buy order matches '{Text.Info(itemName)}'."));
                    return;
                }
                itemIDs = new List<int> { match.TypeID };
            }

            if (itemIDs.Count == 0)
            {
                user.TempServerMessage(Localizer.DoStr("Your store has no buy orders to price. Run /ta setupbuy first."));
                return;
            }

            var output = new StringBuilder();
            output.AppendLineLoc($"Calculating maximum buy prices for {Text.StyledNum(itemIDs.Count)} item(s)...");
            var updated = 0;
            foreach (var itemID in itemIDs)
            {
                var item = Item.Get(itemID);
                if (item == null) continue;
                if (!calc.TryGetMaxBuyPriceForIngredient(item, out var maxPrice, out var reason))
                {
                    output.AppendLine($"- {item.UILink()}: {reason}");
                    continue;
                }
                calc.SetBuyPrice(itemID, maxPrice);
                updated++;
                output.AppendLine($"- {item.UILink()}: max buy price set to {Text.StyledNum(maxPrice)}. {TextLoc.FoldoutLoc($"Why?", $"Why {item.UILink()}", reason.ToStringLoc())}");
            }
            output.AppendLineLoc($"Updated the buy price(s) of {Text.StyledNum(updated)} item(s).");
            user.TempServerMessage(output.ToStringLoc());
        }
        [ChatSubCommand(nameof(TradeAssistant), "Explains how the sell price is calculated for a product", ChatAuthorizationLevel.User)]
        public static async Task Explain(User user, string itemName, User? whoToSendTo = null)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var item = CommandsUtil.ClosestMatchingEntity(user, itemName, Item.AllItemsExceptHidden, x => x.GetType().Name, x => x.DisplayName);
            if (item == null) return;
            if (calc.TryGetCostPrice(item, out var price, out var reason, out var _))
            {
                var msg = new StringBuilder();
                if (whoToSendTo != null)
                    msg.AppendLineLoc($"{user.UILink()} shared this price explanation with you:");
                else
                    whoToSendTo = user;
                var newPrice = Mathf.RoundToAcceptedDigits(price * (1 + calc.Config.Profit / 100f) / (1 - calc.Store.GetTax()));
                var costPriceLink = TextLoc.FoldoutLoc($"CostPrice", $"Cost price of {item.UILink()}", reason.ToStringLoc());
                msg.AppendLineLoc($"Sell price for {item.UILink()}");
                msg.AppendLineLoc($"SellPrice = {costPriceLink} * (1 + Profit) / (1 - TaxRate)");
                msg.AppendLineLoc($"          = {Text.StyledNum(price)} * (1 + {Text.StyledNum(calc.Config.Profit / 100f)}) / (1 - {Text.StyledNum(calc.Store.GetTax())})");
                msg.AppendLineLoc($"          = {Text.StyledNum(newPrice)}");
                whoToSendTo.TempServerMessage(msg.ToStringLoc());
            }
            else
                user.TempServerMessage(reason.ToStringLoc());
        }


        [ChatSubCommand(nameof(TradeAssistant), "Suggests and sets buy limits based on crafting demand. Usage: /ta autobuylimits [multiplier]", ChatAuthorizationLevel.User)]
        public static async Task AutoBuyLimits(User user) => await AutoBuyLimits(user, "1");

        [ChatSubCommand(nameof(TradeAssistant), "Suggests and sets buy limits based on crafting demand. Usage: /ta autobuylimits [multiplier]", ChatAuthorizationLevel.User)]
        public static async Task AutoBuyLimits(User user, string multiplierText)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            if (!float.TryParse(multiplierText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var multiplier) || multiplier <= 0f)
            {
                user.TempServerMessage(Localizer.DoStr("Please provide a positive multiplier (e.g. /ta autobuylimits 2)."));
                return;
            }

            var buyOffers = calc.Store.StoreData.BuyOffers.ToList();
            if (buyOffers.Count == 0)
            {
                user.TempServerMessage(Localizer.DoStr("There are no buy offers to update."));
                return;
            }

            var demand = calc.CraftingDemandPerItem();
            var sb = new StringBuilder();
            var updated = 0;
            var noDemand = 0;
            foreach (var offer in buyOffers)
            {
                var demandSum = offer.OfferedItemTypeIDs().Sum(id => demand.GetValueOrDefault(id, 0f));
                if (demandSum <= 0f)
                {
                    noDemand++;
                    sb.AppendLineLoc($"- {offer.Stack?.Item?.UILink() ?? Localizer.DoStr("Offer")}: no crafting demand found, limit left unchanged.");
                    continue;
                }

                var suggested = (int)Math.Ceiling(demandSum * multiplier);
                offer.Limit = suggested;
                updated++;
                sb.AppendLineLoc($"- {offer.Stack?.Item?.UILink() ?? Localizer.DoStr("Offer")}: buy limit set to {Text.StyledNum(suggested)}.");
            }

            user.TempServerMessage(Localizer.Do($"Updated buy limits for {updated} of {buyOffers.Count} buy offers ({noDemand} had no crafting demand, multiplier {Text.StyledNum(multiplier)}).{Environment.NewLine}{sb.ToStringLoc()}"));

        }



        [ChatSubCommand(nameof(TradeAssistant), "Automatically creates sell categories based on the item categories of your sell offers. Usage: /ta autocategories", ChatAuthorizationLevel.User)]
        public static async Task AutoCategories(User user)


        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var existingNames = calc.Store.StoreData.SellCategories.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var grouped = calc.Store.StoreData.SellOffers
                .SelectMany(o => o.OfferedItemTypeIDs())
                .Select(Item.Get)
                .Where(i => i != null)
                .GroupBy(i => string.IsNullOrEmpty(i.Category) ? "Other" : i.Category)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (grouped.Count == 0)
            {
                user.TempServerMessage(Localizer.DoStr("There are no sell offers to categorize."));
                return;
            }

            var sb = new StringBuilder();
            var created = 0;
            var skipped = 0;
            foreach (var group in grouped)
            {
                if (existingNames.Contains(group.Key))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    calc.Store.CreateCategoryWithMixedOffers(user.Player, group.Select(i => i.TypeID).Distinct().ToList(), new List<string> { group.Key }, false);
                    created++;
                    sb.AppendLineLoc($"- Created the '{group.Key}' category ({group.Count()} items).");
                }
                catch (Exception e)
                {
                    sb.AppendLineLoc($"- Failed to create the '{group.Key}' category: {e.Message}");
                }
            }

            if (created == 0 && skipped > 0)
                user.TempServerMessage(Localizer.Do($"No new categories were created ({skipped} already exist)."));
            else
                user.TempServerMessage(Localizer.Do($"Auto-created {created} sell categor{(created == 1 ? "y" : "ies")} ({skipped} already existed).{Environment.NewLine}{sb.ToStringLoc()}"));
        }

        [ChatSubCommand(nameof(TradeAssistant), "Sets a discount percentage for a sell category. Usage: /ta discount <categoryName> <percent>", ChatAuthorizationLevel.User)]
        public static async Task Discount(User user, string categoryName, string percent)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            if (!float.TryParse(percent, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0f || value > 100f)
            {
                user.TempServerMessage(Localizer.DoStr("Please provide a discount percentage between 0 and 100."));
                return;
            }

            var name = calc.FindSellCategoryName(categoryName);
            if (name == null)
            {
                user.TempServerMessage(Localizer.DoStr("Could not find a sell category with that name."));
                return;
            }

            calc.Config.CategoryDiscounts[name] = value;
            StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
            user.TempServerMessage(Localizer.Do($"Discount of {Text.StyledNum(value)}% set for the '{name}' category. Run /ta update to apply it."));
        }

        [ChatSubCommand(nameof(TradeAssistant), "Removes the discount percentage for a sell category. Usage: /ta discount remove <categoryName>", ChatAuthorizationLevel.User)]
        public static async Task DiscountRemove(User user, string categoryName)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var name = calc.FindSellCategoryName(categoryName);
            if (name == null || !calc.Config.CategoryDiscounts.Remove(name))
            {
                user.TempServerMessage(Localizer.DoStr("There is no discount configured for a category with that name."));
                return;
            }

            StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
            user.TempServerMessage(Localizer.Do($"Removed the discount for the '{name}' category."));
        }



        [ChatSubCommand(nameof(TradeAssistant), "Lists all configured per-category discounts", ChatAuthorizationLevel.User)]
        public static async Task DiscountList(User user)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            if (calc.Config.CategoryDiscounts.Count == 0)
            {
                user.TempServerMessage(Localizer.DoStr("There are no per-category discounts configured."));
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLineLoc($"Configured category discounts:");
            foreach (var pair in calc.Config.CategoryDiscounts.OrderBy(p => p.Key))
                sb.AppendLineLoc($"- {pair.Key}: {Text.StyledNum(pair.Value)}%");
            user.TempServerMessage(sb.ToStringLoc());
        }




        [ChatSubCommand(nameof(TradeAssistant), "Explains how the buy price is calculated for a product", ChatAuthorizationLevel.User)]
        public static async Task ExplainBuy(User user, string itemName, User? whoToSendTo = null)

        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var item = CommandsUtil.ClosestMatchingEntity(user, itemName, Item.AllItemsExceptHidden, x => x.GetType().Name, x => x.DisplayName);
            if (item == null) return;
            if (calc.TryGetCostPrice(item, out var price, out var reason, out var _))
            {
                var msg = new StringBuilder();
                if (whoToSendTo != null)
                    msg.AppendLineLoc($"{user.UILink()} shared this buy price explanation with you:");
                else
                    whoToSendTo = user;
                var costPriceLink = TextLoc.FoldoutLoc($"CostPrice", $"Cost price of {item.UILink()}", reason.ToStringLoc());
                msg.AppendLineLoc($"Buy price for {item.UILink()}");
                msg.AppendLineLoc($"BuyPrice = {costPriceLink}");
                msg.AppendLineLoc($"         = {Text.StyledNum(price)}");
                whoToSendTo.TempServerMessage(msg.ToStringLoc());
            }
            else
                user.TempServerMessage(reason.ToStringLoc());

        }

        [ChatSubCommand(nameof(TradeAssistant), "Enables or disables durability/quality-based pricing. Usage: /ta durpricing <on|off>", ChatAuthorizationLevel.User)]
        public static async Task DurabilityPricing(User user, string state)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            if (state.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                calc.Config.DurabilityPricingEnabled = true;
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                user.TempServerMessage(Localizer.DoStr("Durability/quality-based pricing is now ON. Run /ta update to apply it."));
            }
            else if (state.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                calc.Config.DurabilityPricingEnabled = false;
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                user.TempServerMessage(Localizer.DoStr("Durability/quality-based pricing is now OFF."));
            }
            else
                user.TempServerMessage(Localizer.DoStr("Usage: /ta durpricing <on|off>"));
        }

        [ChatSubCommand(nameof(TradeAssistant), "Configures durability/quality-based pricing. Usage: /ta durpricingconfig <min|premium> <value>", ChatAuthorizationLevel.User)]
        public static async Task DurabilityPricingConfig(User user, string key, string value)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            if (key.Equals("min", StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var min) || min < 0f || min > 100f)
                {
                    user.TempServerMessage(Localizer.DoStr("Please provide a minimum price percentage between 0 and 100."));
                    return;
                }
                calc.Config.DurabilityMinPricePercent = min;
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                user.TempServerMessage(Localizer.Do($"Worn items will be priced at no less than {Text.StyledNum(min)}% of the normal price."));
            }
            else if (key.Equals("premium", StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var premium) || premium < 0f)
                {
                    user.TempServerMessage(Localizer.DoStr("Please provide a non-negative quality premium percentage."));
                    return;
                }
                calc.Config.QualityPremiumPercent = premium;
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                user.TempServerMessage(Localizer.Do($"Quality premium set to {Text.StyledNum(premium)}% per quality group."));
            }
            else
                user.TempServerMessage(Localizer.DoStr("Usage: /ta durpricingconfig <min|premium> <value>"));
        }

        [ChatSubCommand(nameof(TradeAssistant), "Shows the current durability/quality-based pricing configuration", ChatAuthorizationLevel.User)]
        public static async Task DurabilityPricingShow(User user)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            user.TempServerMessage(Localizer.Do($"Durability/quality-based pricing: {(calc.Config.DurabilityPricingEnabled ? "ON" : "OFF")}. Worn items priced at no less than {Text.StyledNum(calc.Config.DurabilityMinPricePercent)}% of normal price. Quality premium {Text.StyledNum(calc.Config.QualityPremiumPercent)}% per quality group."));
        }



        [ChatSubCommand(nameof(TradeAssistant), "Opens up a window to change your configurations", ChatAuthorizationLevel.User)]
        public static void Config(User user)
        {
            if (!TradeAssistantData.Obj.UserConfiguration.TryGetValue(user.Id, out var config))
            {
                config = new UserConfig();
                TradeAssistantData.Obj.UserConfiguration.Add(user.Id, config);
            }

            ViewEditorUtils.PopupUserEditValue(user, typeof(UserConfigUI), Localizer.DoStr("Trade Assistant Configuration"), config.ToUI(), null, OnSubmit);
            void OnSubmit(object entry)
            {
                if (entry is UserConfigUI uiConfig)
                    config.UpdateFromUI(uiConfig);
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
            }
        }
    }
}

using Eco.Core.Plugins;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Economy;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Property;
using Eco.Gameplay.StrangeCloudGameplay;
using Eco.Gameplay.Systems;
using Eco.Gameplay.Systems.EcoMarketplace;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Gameplay.Systems.TextLinks;
using Eco.Gameplay.Utils;
using Eco.Shared;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Utils;
using Eco.Shared.Voxel;
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
            sb.AppendLine(Localizer.DoStr("- Optional: use /ta exchange status to compare your store prices with the current market (exchange) average/min/max across all player stores, and /ta exchange price <avg|min> [sell|buy] to sync your orders to that market baseline."));
            sb.AppendLine(Localizer.DoStr("- Optional: use /ta competitive on [floorPercent] to follow the lowest competing market price while keeping prices inside your profit range (default floor: 85% of the normal price). Restrict it to categories with /ta competitive category add <name>, review with /ta competitive status and disable with /ta competitive off."));
            sb.AppendLine(Localizer.DoStr("- Optional: use /ta exportconfig to get your whole configuration as a copy/paste payload and /ta importconfig <payload> to restore it on another server. If you own several stores, manage which one /ta commands apply to with /ta store list, /ta store select <name> and /ta store clear."));
            sb.AppendLine(Localizer.DoStr("- Optional: use /ta recipeqty set <item> <quantity> to configure how many of each product you produce per batch. Quantities are tracked separately for craft and cook recipes (/ta recipeqty craft <item> <qty> and /ta recipeqty cook <item> <qty> force a type, /ta recipeqty status lists everything). /ta setupbuy and /ta autobuylimits use these quantities to size your buy limits."));

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


            var demand = calc.CraftingDemandPerItem();
            calc.Store.CreateCategoryWithOffers(user.Player, items.ToList(), true);
            foreach (var offer in calc.Store.StoreData.BuyOffers.Where(o => items.Any(o.MatchesTypeID)))
            {
                var demandSum = offer.OfferedItemTypeIDs().Sum(id => demand.GetValueOrDefault(id, 0f));
                offer.Limit = demandSum > 0f ? (int)Math.Ceiling(demandSum) : 1;
                offer.Price = 0;
            }
            user.TempServerMessage(Localizer.DoStr($"{Text.Info(Text.Num(items.Count))} buy orders were added with buy limits automatically set from recipe quantities (price left at 0 for you to set)."));
        }


        [ChatSubCommand(nameof(TradeAssistant), "Update the sell price of everything in the store based on the configured profit and cost per calory", ChatAuthorizationLevel.User)]
        public static async Task Update(User user)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var output = new StringBuilder();
            TradeAssistantRepricing.RunUpdate(calc, output, competitivePricing: true);
            user.TempServerMessage(output.ToStringLoc());
        }

        [ChatSubCommand(nameof(TradeAssistant), "Manages competitive pricing rules: follow the lowest competing market price within your configured profit range, with floor/ceiling protection. Usage: /ta competitive <on|off|status|category <add|remove> <name>> [floorPercent] [category...]", ChatAuthorizationLevel.User)]
        public static async Task Competitive(User user, string action, string? value = null)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var config = calc.Config;
            if (action.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                config.CompetitivePricingEnabled = true;
                if (!string.IsNullOrWhiteSpace(value) && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var floor))
                    config.CompetitiveFloorPercent = Math.Clamp(floor, 0f, 100f);
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                user.TempServerMessage(Localizer.DoStr($"Competitive pricing enabled. Prices will follow the lowest market price down to {Text.StyledNum(config.CompetitiveFloorPercent)}% of the normal price. Run /ta update to apply."));
            }
            else if (action.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                config.CompetitivePricingEnabled = false;
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                user.TempServerMessage(Localizer.DoStr("Competitive pricing is now OFF. Run /ta update to restore normal prices."));
            }
            else if (action.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                var msg = new StringBuilder();
                msg.AppendLineLoc($"Competitive pricing: {(config.CompetitivePricingEnabled ? "ON" : "OFF")}");
                msg.AppendLineLoc($"Floor protection: {Text.StyledNum(config.CompetitiveFloorPercent)}% of the normal price (0 disables the floor).");
                msg.AppendLineLoc($"Ceiling protection: {Text.StyledNum(config.CompetitiveCeilingPercent)}% above the normal price (0 disables the ceiling).");
                var cats = config.CompetitiveCategories;
                if (cats.Count == 0)
                    msg.AppendLine(Localizer.DoStr("Enabled categories: all categories"));
                else
                    msg.AppendLine(Localizer.Do($"Enabled categories: {string.Join(", ", cats)}"));
                user.TempServerMessage(msg);
            }
            else if (action.Equals("category", StringComparison.OrdinalIgnoreCase))
            {
                var parts = (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    user.TempServerMessage(Localizer.DoStr("Usage: /ta competitive category <add|remove> <categoryName>"));
                    return;
                }
                var name = string.Join(" ", parts.Skip(1));
                if (parts[0].Equals("add", StringComparison.OrdinalIgnoreCase))
                {
                    if (!config.CompetitiveCategories.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
                        config.CompetitiveCategories.Add(name);
                    user.TempServerMessage(Localizer.Do($"Category '{Text.Info(name)}' added to competitive pricing."));
                }
                else if (parts[0].Equals("remove", StringComparison.OrdinalIgnoreCase))
                {
                    var removed = config.CompetitiveCategories.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
                    if (removed != null)
                        config.CompetitiveCategories.Remove(removed);
                    user.TempServerMessage(Localizer.Do($"Category '{Text.Info(name)}' removed from competitive pricing."));
                }
                else
                    user.TempServerMessage(Localizer.DoStr("Usage: /ta competitive category <add|remove> <categoryName>"));
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
            }
            else
                user.TempServerMessage(Localizer.DoStr("Usage: /ta competitive <on|off|status|category <add|remove> <name>>"));
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

        [ChatSubCommand(nameof(TradeAssistant), "Shows the current market (exchange) average, min and max prices for your store's items compared to your own prices. Usage: /ta exchange status", ChatAuthorizationLevel.User)]
        public static async Task ExchangeStatus(User user)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var sellStats = TradeAssistantMarket.ComputeMarket(sellOffers: true);
            var buyStats = TradeAssistantMarket.ComputeMarket(sellOffers: false);

            var output = new StringBuilder();
            output.AppendLineLoc($"Market (exchange) comparison for {calc.Store.Parent.UILink()}:");
            output.AppendLine(Localizer.DoStr("SELL OFFERS:"));
            var sellItems = calc.Store.StoreData.SellOffers.SelectMany(o => o.OfferedItemTypeIDs()).Distinct().ToList();
            foreach (var itemID in sellItems)
            {
                var item = Item.Get(itemID);
                if (item == null) continue;
                var mine = calc.Store.StoreData.SellOffers.SelectMany(o => o.OfferedItemTypeIDs().Where(id => id == itemID).Select(_ => o.Price)).DefaultIfEmpty(0f).Min();
                if (!sellStats.TryGetValue(itemID, out var market))
                {
                    output.AppendLine($"- {item.UILink()}: mine {Text.StyledNum(mine)} - no other market orders");
                    continue;
                }
                output.AppendLine($"- {item.UILink()}: mine {Text.StyledNum(mine)} | market {TradeAssistantMarket.Describe(market)}");
            }
            output.AppendLine(Localizer.DoStr("BUY OFFERS:"));
            var buyItems = calc.Store.StoreData.BuyOffers.SelectMany(o => o.OfferedItemTypeIDs()).Distinct().ToList();
            foreach (var itemID in buyItems)
            {
                var item = Item.Get(itemID);
                if (item == null) continue;
                var mine = calc.Store.StoreData.BuyOffers.SelectMany(o => o.OfferedItemTypeIDs().Where(id => id == itemID).Select(_ => o.Price)).DefaultIfEmpty(0f).Min();
                if (!buyStats.TryGetValue(itemID, out var market))
                {
                    output.AppendLine($"- {item.UILink()}: mine {Text.StyledNum(mine)} - no other market orders");
                    continue;
                }
                output.AppendLine($"- {item.UILink()}: mine {Text.StyledNum(mine)} | market {TradeAssistantMarket.Describe(market)}");
            }
            user.TempServerMessage(output.ToStringLoc());
        }

        [ChatSubCommand(nameof(TradeAssistant), "Sets your store prices to the market (exchange) baseline. Usage: /ta exchange price <avg|min> [sell|buy]", ChatAuthorizationLevel.User)]
        public static async Task ExchangePrice(User user, string basis, string side = "sell")
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var useAverage = basis.Equals("avg", StringComparison.OrdinalIgnoreCase);
            var useMin = basis.Equals("min", StringComparison.OrdinalIgnoreCase);
            if (!useAverage && !useMin)
            {
                user.TempServerMessage(Localizer.DoStr("Usage: /ta exchange price <avg|min> [sell|buy]"));
                return;
            }
            var buySide = side.Equals("buy", StringComparison.OrdinalIgnoreCase);
            if (!buySide && !side.Equals("sell", StringComparison.OrdinalIgnoreCase))
            {
                user.TempServerMessage(Localizer.DoStr("Usage: /ta exchange price <avg|min> [sell|buy]"));
                return;
            }

            var market = TradeAssistantMarket.ComputeMarket(sellOffers: !buySide);
            var offers = (buySide ? calc.Store.StoreData.BuyOffers : calc.Store.StoreData.SellOffers).ToList();
            var output = new StringBuilder();
            output.AppendLineLoc($"Syncing {(buySide ? "buy" : "sell")} orders to the market {(useAverage ? "average" : "minimum")} price...");
            var updated = 0;
            var missing = 0;
            foreach (var typeID in offers.SelectMany(o => o.OfferedItemTypeIDs()).Distinct().ToList())
            {
                var item = Item.Get(typeID);
                if (item == null) continue;
                if (!market.TryGetValue(typeID, out var stats) || stats.OfferCount == 0)
                {
                    missing++;
                    output.AppendLine($"- {item.UILink()}: no market orders, skipped");
                    continue;
                }
                var newPrice = useAverage ? stats.AveragePrice : stats.MinPrice;
                if (buySide) calc.SetBuyPrice(typeID, newPrice);
                else calc.SetSellPrice(typeID, newPrice);
                updated++;
                output.AppendLine($"- {item.UILink()}: {Text.StyledNum(newPrice)} ({TradeAssistantMarket.Describe(stats)})");
            }
            output.AppendLineLoc($"Updated {Text.StyledNum(updated)} order(s), skipped {Text.StyledNum(missing)} item(s) without market data.");
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

        [ChatSubCommand(nameof(TradeAssistant), "Configure per-recipe production quantities, split between craft and cook recipes. Usage: /ta recipeqty <set|craft|cook|clear|status> [itemName] [quantity]", ChatAuthorizationLevel.User)]
        public static async Task RecipeQty(User user, string action, string? rest = null)
        {
            var calc = await TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var parts = (rest ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var config = calc.Config;

            if (action.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                if (config.RecipeQuantities.Count == 0)
                {
                    user.TempServerMessage(Localizer.DoStr("No per-recipe production quantities are configured yet. Use /ta recipeqty set <item> <quantity> to add one."));
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLineLoc($"Configured recipe production quantities:");
                foreach (var pair in config.RecipeQuantities.OrderBy(p => Item.Get(p.Key)?.DisplayName ?? p.Key.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    var item = Item.Get(pair.Key);
                    if (item == null) continue;
                    var type = calc.FindRecipeTypeForItem(pair.Key);
                    var active = type == RecipeType.Cook ? pair.Value.CookQty : pair.Value.CraftQty;
                    sb.AppendLineLoc($"- {item.UILink()}: craft {Text.StyledNum(pair.Value.CraftQty)}, cook {Text.StyledNum(pair.Value.CookQty)} ({(type == null ? "no craftable recipe" : type == RecipeType.Cook ? "cook" : "craft")} active: {Text.StyledNum(active)})");
                }
                user.TempServerMessage(sb.ToStringLoc());
                return;
            }

            if (parts.Length == 0)
            {
                user.TempServerMessage(Localizer.DoStr("Usage: /ta recipeqty <set|craft|cook|clear|status> [itemName] [quantity]"));
                return;
            }

            if (action.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = string.Join(" ", parts);
                var item = CommandsUtil.ClosestMatchingEntity(user, itemName, Item.AllItemsExceptHidden, x => x.GetType().Name, x => x.DisplayName);
                if (item == null) return;
                if (config.RecipeQuantities.Remove(item.TypeID))
                {
                    StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                    user.TempServerMessage(Localizer.Do($"Removed recipe quantity configuration for {item.UILink()}."));
                }
                else
                    user.TempServerMessage(Localizer.Do($"No recipe quantity configuration found for {item.UILink()}."));
                return;
            }

            var isSet = action.Equals("set", StringComparison.OrdinalIgnoreCase);
            var isCraft = action.Equals("craft", StringComparison.OrdinalIgnoreCase);
            var isCook = action.Equals("cook", StringComparison.OrdinalIgnoreCase);
            if (!isSet && !isCraft && !isCook)
            {
                user.TempServerMessage(Localizer.DoStr("Usage: /ta recipeqty <set|craft|cook|clear|status> [itemName] [quantity]"));
                return;
            }

            if (parts.Length < 2 || !int.TryParse(parts[^1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var qty) || qty < 0)
            {
                user.TempServerMessage(Localizer.DoStr("Please provide an item name followed by a non-negative integer quantity (e.g. /ta recipeqty set Campfire Stew 3)."));
                return;
            }

            var name = string.Join(" ", parts.Take(parts.Length - 1));
            var target = CommandsUtil.ClosestMatchingEntity(user, name, Item.AllItemsExceptHidden, x => x.GetType().Name, x => x.DisplayName);
            if (target == null) return;

            var forcedType = isCraft ? (RecipeType?)RecipeType.Craft : isCook ? (RecipeType?)RecipeType.Cook : null;
            if (isSet && forcedType == null)
            {
                forcedType = calc.FindRecipeTypeForItem(target.TypeID);
                if (forcedType == null)
                {
                    user.TempServerMessage(Localizer.Do($"There is no craftable recipe producing {target.UILink()} on this property's crafting tables. Use /ta recipeqty craft <item> <quantity> or /ta recipeqty cook <item> <quantity> to force a type."));
                    return;
                }
            }

            if (!config.RecipeQuantities.TryGetValue(target.TypeID, out var setting))
            {
                setting = new RecipeQuantitySetting();
                config.RecipeQuantities[target.TypeID] = setting;
            }
            if (forcedType == RecipeType.Cook)
                setting.CookQty = qty;
            else
                setting.CraftQty = qty;

            StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
            user.TempServerMessage(Localizer.Do($"Recipe quantity for {target.UILink()} set to {Text.StyledNum(qty)} per batch ({(forcedType == RecipeType.Cook ? "cook" : "craft")} recipes). /ta setupbuy and /ta autobuylimits will use it to size buy limits."));
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

        [ChatSubCommand(nameof(TradeAssistant), "Exports your whole Trade Assistant configuration as a JSON payload you can restore on another server with /ta importconfig. Usage: /ta exportconfig", ChatAuthorizationLevel.User)]
        public static async Task ExportConfig(User user)
        {
            var config = user.Config();
            var (json, base64) = TradeAssistantConfigIO.Export(config);
            var sb = new StringBuilder();
            sb.AppendLineLoc($"Your Trade Assistant configuration has been exported. Run /ta importconfig with the payload below on the target server.");
            sb.AppendLineLoc($"PAYLOAD (copy everything between the quotes):");
            sb.AppendLine(base64);
            sb.AppendLineLoc($"Human readable JSON:");
            sb.AppendLine(json);
            user.TempServerMessage(sb);
        }

        [ChatSubCommand(nameof(TradeAssistant), "Imports a Trade Assistant configuration exported with /ta exportconfig (paste the PAYLOAD). Usage: /ta importconfig <payload>", ChatAuthorizationLevel.User)]
        public static async Task ImportConfig(User user, string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                user.TempServerMessage(Localizer.DoStr("Usage: /ta importconfig <payload> (get one with /ta exportconfig)"));
                return;
            }

            try
            {
                var (config, warnings) = TradeAssistantConfigIO.Import(payload);
                TradeAssistantData.Obj.UserConfiguration[user.Id] = config;
                StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
                var sb = new StringBuilder();
                sb.AppendLineLoc($"Configuration imported successfully. Run /ta update to apply it to your store.");
                if (warnings.Count > 0)
                {
                    sb.AppendLineLoc($"Warnings ({warnings.Count}):");
                    foreach (var warning in warnings)
                        sb.AppendLine($"- {Localizer.DoStr(warning)}");
                }
                user.TempServerMessage(sb);
            }
            catch (Exception e)
            {
                user.TempServerMessage(Localizer.Do($"Could not import the configuration: {e.Message}. Make sure you copied the full PAYLOAD from /ta exportconfig."));
            }
        }

        [ChatSubCommand(nameof(TradeAssistant), "Lists all the stores you have owner access to, so you can manage which one /ta commands apply to. Usage: /ta store list", ChatAuthorizationLevel.User)]
        public static async Task StoreList(User user)
        {
            var stores = WorldObjectUtil.AllObjsWithComponent<StoreComponent>()
                .Where(store => store.IsRPCAuthorized(user.Player, AccessType.FullAccess, Array.Empty<object>()))
                .OrderBy(store => World.WrappedDistance(user.Player.WorldPos(), store.Parent.WorldPos()))
                .ToList();
            if (stores.Count == 0)
            {
                user.TempServerMessage(Localizer.DoStr("You don't have any store you have owner access to."));
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLineLoc($"Your stores ({stores.Count}):");
            foreach (var store in stores)
            {
                var distance = (int)Math.Round(World.WrappedDistance(user.Player.WorldPos(), store.Parent.WorldPos()));
                sb.AppendLineLoc($"- {store.Parent.UILink()} ({Text.StyledNum(distance)}m away)");
            }
            var selected = user.Config().SelectedStoreName;
            sb.AppendLineLoc($"Currently selected: {(string.IsNullOrWhiteSpace(selected) ? "none - the nearest store is used" : selected)}");
            user.TempServerMessage(sb);
        }

        [ChatSubCommand(nameof(TradeAssistant), "Selects the store that /ta commands should manage. Stand in its deed afterwards and the selection is remembered. Usage: /ta store select <name>", ChatAuthorizationLevel.User)]
        public static async Task StoreSelect(User user, string name)
        {
            var store = WorldObjectUtil.AllObjsWithComponent<StoreComponent>()
                .Where(s => s.IsRPCAuthorized(user.Player, AccessType.FullAccess, Array.Empty<object>()))
                .FirstOrDefault(s =>
                    string.Equals(s.Parent.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Parent.DisplayName, name, StringComparison.OrdinalIgnoreCase) ||
                    s.Parent.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (store == null)
            {
                user.TempServerMessage(Localizer.Do($"No store named '{Text.Info(name)}' found. Run /ta store list to see your stores."));
                return;
            }

            user.Config().SelectedStoreName = store.Parent.Name;
            StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
            user.TempServerMessage(Localizer.Do($"Selected store: {store.Parent.UILink()}. /ta commands will use it when you stand in its deed."));
        }

        [ChatSubCommand(nameof(TradeAssistant), "Clears the selected store so /ta commands fall back to the nearest store again. Usage: /ta store clear", ChatAuthorizationLevel.User)]
        public static async Task StoreClear(User user)
        {
            user.Config().SelectedStoreName = null;
            StorageManager.Obj.MarkDirty(TradeAssistantData.Obj);
            user.TempServerMessage(Localizer.DoStr("Store selection cleared. The nearest store will be used again."));
        }
    }
}

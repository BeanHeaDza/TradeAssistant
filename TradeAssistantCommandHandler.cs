using Eco.Core.Plugins;
using Eco.Gameplay.Economy;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Gameplay.Systems.TextLinks;
using Eco.Gameplay.Systems.Tooltip;
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
            sb.Append(Localizer.DoStr("- Once you're done with that the last step is to run ")).Append(Text.Name("/ta update")).AppendLine(Localizer.DoStr(", this will update the prices of all your sell orders based on your configured profit percentage and labor cost."));

            user.TempServerMessage(sb);
        }

        [ChatSubCommand(nameof(TradeAssistant), "Add sell orders for all the products you are able to craft in this property's Crafting Tables.", ChatAuthorizationLevel.User)]
        public static void SetupSell(User user)
        {
            var calc = TradeAssistantCalculator.TryInitialize(user);
            if (calc == null)
                return;

            var sb = new StringBuilder();

            var items = calc.CraftableItems.SelectMany(x => x.Value).Select(p => p.TypeID).ToHashSet();
            var soldItems = calc.Store.StoreData.SellOffers.Select(o => o.Stack.Item.TypeID).ToHashSet();
            var itemsToAdd = items.Where(i => !soldItems.Contains(i)).ToList();

            if (!itemsToAdd.Any())
            {
                sb.AppendLine(Localizer.DoStr($"All the items you can craft is already added to the store."));
                user.TempServerMessage(sb);
                return;
            }

            foreach (var (table, tableItems) in calc.CraftableItems)
            {
                var addedForThisTable = tableItems.Select(i => i.TypeID).Where(itemsToAdd.Contains);
                if (!addedForThisTable.Any()) { continue; }

                calc.Store.CreateCategoryWithOffers(addedForThisTable.ToList(), false);
                var category = calc.Store.StoreData.SellCategories.Last();
                category.Name = table;
            }
            foreach (var offer in calc.Store.StoreData.SellOffers.Where(o => itemsToAdd.Contains(o.Stack.Item.TypeID)))
                offer.Price = 999999;

            sb.AppendLine(Localizer.DoStr($"Added {Text.Info(Text.Num(itemsToAdd.Count))} sell orders. Open the store and remove the items you're not interested in selling at your shop."));
            user.TempServerMessage(sb);
        }

        [ChatSubCommand(nameof(TradeAssistant), "Add buy orders for all the ingredients you need to make the products you're selling", ChatAuthorizationLevel.User)]
        public static void SetupBuy(User user)
        {
            var calc = TradeAssistantCalculator.TryInitialize(user);
            if (calc == null)
                return;

            var sb = new StringBuilder();

            var items = new HashSet<int>();

            // Build up a lookup of product to required ingredients
            var allRecipes = calc.ProductToRequiredItemsLookup();

            // Limit the products to only the ones we're selling
            var sellOfferTypeIds = calc.Store.StoreData.SellOffers.Select(o => o.Stack.Item.TypeID).ToList();

            // Work through the list of craftable items and find the product we need to make them
            var todo = new Queue<int>(calc.CraftableItems.SelectMany(x => x.Value).Select(p => p.TypeID).Distinct().Where(sellOfferTypeIds.Contains));
            var done = new HashSet<int>();
            while (todo.TryDequeue(out var productId))
            {
                if (!done.Add(productId)) continue;
                var itemsToQueue = allRecipes[productId].Where(allRecipes.ContainsKey).ToList();
                var itemsToBuy = allRecipes[productId].Where(i => !itemsToQueue.Contains(i));
                items.AddRange(itemsToBuy);
                itemsToQueue.ForEach(todo.Enqueue);
            }

            items.RemoveRange(calc.Store.StoreData.BuyOffers.Select(o => o.Stack.Item.TypeID));
            if (items.Count == 0)
            {
                user.TempServerMessage(Localizer.DoStr($"The buy orders for all possible ingredients to make the current sell order products are already listed in the shop."));
                return;
            }


            calc.Store.CreateCategoryWithOffers(items.ToList(), true);
            foreach (var offer in calc.Store.StoreData.BuyOffers.Where(o => items.Contains(o.Stack.Item.TypeID)))
            {
                offer.Limit = 1;
                offer.Price = 0;
            }
            user.TempServerMessage(Localizer.DoStr($"{Text.Info(Text.Num(items.Count))} buy orders were added, please open the shop and set the Price and Limit for each of them."));
        }


        [ChatSubCommand(nameof(TradeAssistant), "Update the sell price of everything in the store based on the configured profit and cost per calory", ChatAuthorizationLevel.User)]
        public static void Update(User user)
        {
            var calc = TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var storeSellItemIds = calc.Store.StoreData.SellOffers.Select(o => o.Stack.Item.TypeID).Distinct().ToList();
            if (storeSellItemIds.Count == 0)
            {
                user.TempServerMessage(Localizer.Do($"{calc.Store.Parent.UILink()} has no sell orders."));
                return;
            }
            var buyProductsToUpdate = calc.Store.StoreData.BuyOffers.Select(o => o.Stack.Item.TypeID).Distinct().Where(storeSellItemIds.Contains).ToList();

            var byProducts = calc.Config.ByProducts.ToHashSet();
            var craftableItems = calc.CraftableItems.SelectMany(x => x.Value).Select(x => x.TypeID).Distinct().ToHashSet();
            var output = new StringBuilder();
            var updates = new List<LocString>();
            var warnings = new List<LocString>();


            foreach (var itemID in storeSellItemIds.OrderBy(p => byProducts.Contains(p) ? 0 : 1))
            {
                if (calc.TryGetCostPrice(itemID, out var price, out var reason, out var itemWarnings))
                {
                    // SellPrice = Tax + CostPrice * (1 + Profit)
                    // SellPrice = SellPrice * TaxRate + CostPrice * (1 + Profit)
                    // SellPrice - SellPrice * TaxRate = CostPrice * (1 + Profit)
                    // SellPrice * (1 - TaxRate) = CostPrice * (1 + Profit)
                    // SellPrice = CostPrice * (1 + Profit) / (1 - TaxRate)
                    var newPrice = Mathf.RoundToAcceptedDigits(price * (1 + calc.Config.Profit / 100f) / (1 - EconomyManager.Tax.GetSalesTax(calc.Store.Currency)));
                    updates.AddRange(calc.SetSellPrice(itemID, newPrice));
                    if (itemWarnings != null)
                        warnings.AddRange(itemWarnings);
                }
                else if (!byProducts.Contains(itemID) && craftableItems.Contains(itemID))
                    output.AppendLineLoc($"Failed to get the cost price of {Item.Get(itemID).UILink()} ({TextLoc.FoldoutLoc($"Why?", $"Why {Item.Get(itemID).UILink()}", reason.ToStringLoc())}).");
            }

            foreach (var itemID in buyProductsToUpdate)
                if (calc.TryGetCostPrice(itemID, out var price, out var _, out var _))
                    updates.AddRange(calc.SetBuyPrice(itemID, price));

            // TODO: I'm not happy with how warnings are working at the moment. Removing them for now.
            //if (warnings.Any())
            //    output.AppendLine(warnings.Select(w => "- " + w).Distinct().FoldoutListLoc("warning", Eco.Shared.Items.TooltipOrigin.None));

            if (updates.Count == 0 && output.Length == 0)
                user.TempServerMessage(Localizer.Do($"All prices are up to date!"));
            else
            {
                output.Insert(0, Localizer.Do($"Updating the sell prices at {calc.Store.Parent.UILink()}\n"));
                updates.ForEach(u => output.AppendLine(u));
                if (updates.Count > 0)
                    output.AppendLineLoc($"Updated the price(s) of {Text.StyledNum(updates.Count)} order(s)");
                user.TempServerMessage(output.ToStringLoc());
            }
        }
        [ChatSubCommand(nameof(TradeAssistant), "Explains how the sell price is calculated for a product", ChatAuthorizationLevel.User)]
        public static void Explain(User user, string itemName, User? whoToSendTo = null)
        {
            var calc = TradeAssistantCalculator.TryInitialize(user);
            if (calc == null) return;

            var item = CommandsUtil.ClosestMatchingEntity(user, itemName, Item.AllItems, x => x.GetType().Name, x => x.DisplayName);
            if (item == null) return;
            if (calc.TryGetCostPrice(item, out var price, out var reason, out var _))
            {
                var msg = new StringBuilder();
                if (whoToSendTo != null)
                    msg.AppendLineLoc($"{user.UILink()} shared this price explanation with you:");
                else
                    whoToSendTo = user;
                var newPrice = Mathf.RoundToAcceptedDigits(price * (1 + calc.Config.Profit / 100f) / (1 - EconomyManager.Tax.GetSalesTax(calc.Store.Currency)));
                var costPriceLink = TextLoc.FoldoutLoc($"CostPrice", $"Cost price of {item.UILink()}", reason.ToStringLoc());
                msg.AppendLineLoc($"Sell price for {item.UILink()}");
                msg.AppendLineLoc($"SellPrice = {costPriceLink} * (1 + Profit) / (1 - TaxRate)");
                msg.AppendLineLoc($"          = {Text.StyledNum(price)} * (1 + {Text.StyledNum(calc.Config.Profit / 100f)}) / (1 - {Text.StyledNum(EconomyManager.Tax.GetSalesTax(calc.Store.Currency))})");
                msg.AppendLineLoc($"          = {Text.StyledNum(newPrice)}");
                whoToSendTo.TempServerMessage(msg.ToStringLoc());
            }
            else
                user.TempServerMessage(reason.ToStringLoc());
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
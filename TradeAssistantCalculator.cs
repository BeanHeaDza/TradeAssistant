using Eco.Gameplay.Components;
using Eco.Gameplay.DynamicValues;
using Eco.Gameplay.Economy;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Property;
using Eco.Gameplay.Systems.TextLinks;
using Eco.Gameplay.Systems.Tooltip;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Utils;
using Eco.Shared.Voxel;
using System.Linq;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace TradeAssistant
{
    public class TradeAssistantCalculator
    {
        private record CachedPrice(float Price, StringBuilder Reason, Recipe? Recipe = null, List<LocString>? Warnings = null);
        private record IngredientPrice(float Price, Item Item, LocString Reason, List<LocString>? Warnings);
        private record ProductPrice(CraftingElement Product, float Price, LocString Reason);
        private List<CraftingComponent> CraftingTables { get; }
        private User User { get; }
        private Dictionary<int, float> StoreBuyPrices { get; }

        private Dictionary<int, float> StoreSellPrices { get; }
        private Dictionary<int, CachedPrice> CachedPrices { get; } = new();

        public UserConfig Config { get; }
        public StoreComponent Store { get; }
        public List<Item> CraftableItems { get; }

        public const int WORLD_OBJECT_CAP_NUMBER = 1;
        public const int TOOL_CAP_NUMBER = 5;

        private TradeAssistantCalculator(StoreComponent store, List<CraftingComponent> craftingTables, List<Item> craftableItems, User user, UserConfig config)
        {
            Store = store;
            CraftingTables = craftingTables;
            CraftableItems = craftableItems;
            User = user;
            Config = config;

            StoreBuyPrices = store.StoreData.BuyOffers
                .GroupBy(o => o.Stack.Item.TypeID)
                .ToDictionary(x => x.Key, x => x.Max(o => o.Price));

            StoreSellPrices = store.StoreData.SellOffers
                .GroupBy(o => o.Stack.Item.TypeID)
                .ToDictionary(x => x.Key, x => x.Min(o => o.Price));
        }

        public static TradeAssistantCalculator? TryInitialize(User user)
        {
            var sb = new StringBuilder();
            if (TryGetStoreAndCraftingTables(sb, user, out var store, out var craftingTables, out var craftableItems))
            {
                //If the sales tax is 100% the mod won't work
                if (EconomyManager.Tax.GetSalesTax(store!.Currency) == 1)
                {
                    user.TempServerMessage(Localizer.Do($"Price calculations won't work when tax is set to 100%"));
                    return null;
                }

                //If the profit % is set to -100% the mod calculations won't work
                if (user.Config().Profit == -100f)
                {
                    user.TempServerMessage(Localizer.Do($"Price calculations won't work your profit is set to -100%"));
                    return null;
                }

                return new TradeAssistantCalculator(store!, craftingTables!, craftableItems!, user, user.Config());
            }
            user.TempServerMessage(sb);
            return null;
        }

        public Dictionary<int, List<int>> ProductToRequiredItemsLookup()
        {
            return CraftingTables
                .SelectMany(w => w.Recipes.Where(recipe => recipe.RequiredSkills.All(s => s.IsMet(User))))
                .SelectMany(r => r.Recipes.Skip(r.CraftableDefault ? 0 : 1))
                .SelectMany(r => r.Items.Select(i => new { product = i.Item, r.Ingredients }))
                .SelectMany(x => x.Ingredients.Select(i => new { x.product, ingredient = i }))
                .SelectMany(x => x.ingredient.IsSpecificItem ? new[] { new { x.product, item = x.ingredient.Item } } : x.ingredient.Tag.TaggedItems().Select(t => new { x.product, item = t }))
                .DistinctBy(x => $"{x.product.TypeID}:{x.item.TypeID}")
                .GroupBy(x => x.product.TypeID)
                .ToDictionary(x => x.Key, x => x.Select(y => y.item.TypeID).ToList());
        }

        public bool TryGetCostPrice(Item item, out float outPrice, out StringBuilder reason, out List<LocString>? warnings)
        {
            warnings = null;

            // Check if the user is buying the item already in the store
            if (StoreBuyPrices.TryGetValue(item.TypeID, out outPrice))
            {
                reason = new StringBuilder(Localizer.DoStr($"{Store.Parent.UILink()} has a buy order for {item.UILink()} at a price of {Text.StyledNum(outPrice)}"));
                return true;
            }

            // Check if we haven't already calculated the price of the item.
            if (CachedPrices.TryGetValue(item.TypeID, out var cachedPrice))
            {
                outPrice = cachedPrice.Price;
                reason = cachedPrice.Reason;
                warnings = cachedPrice.Warnings;
                return !float.IsPositiveInfinity(cachedPrice.Price);
            }


            var recipes = CraftingTables.SelectMany(ct => ct.Recipes
                .SelectMany(rf => rf.Recipes.Skip(rf.CraftableDefault ? 0 : 1).Select(r => (ct, r)))
                .Where(x => x.r.Items.Any(i => i.Item.TypeID == item.TypeID))
            ).ToList();

            if (!recipes.Any())
            {
                outPrice = float.PositiveInfinity;
                reason = new StringBuilder(Localizer.DoStr($"There is no recipe on any of the crafting tables that can craft {item.UILink()}"));
                CachedPrices.Add(item.TypeID, new CachedPrice(outPrice, reason));
                return false;
            }

            CachedPrice? bestPrice = null;
            var rejectedPrices = new List<CachedPrice>();
            var failedRecipes = new List<LocString>();
            foreach (var (craftingTable, recipe) in recipes)
            {
                var explanation = new StringBuilder();

                // Ignore by-products if there is more than one product, and make sure the only product is the specified item
                var products = recipe.Items;
                var resourceEfficiencyContext = new ModuleContext(User, craftingTable.Parent.Position, craftingTable.ResourceEfficiencyModule);
                if (recipe.Items.Count > 1)
                {
                    products = recipe.Items.Where(p => !Config.ByProducts.Any(byProductId => byProductId == p.Item.TypeID)).ToList();
                    if (products.Count > 1)
                        failedRecipes.AddLoc($"{recipe.UILink()} has multiple output products, specify which are by-product(s): {string.Join(", ", products.Select(p => p.Item.UILink()))}");
                    else if (products.Count == 0)
                        failedRecipes.AddLoc($"All the products ({string.Join(", ", recipe.Items.Select(p => p.Item.UILink()))}) of the {recipe.UILink()} has been marked as waste, one should not be. Run {Text.Name("/ta config")} to set the by-products.");
                    if (products.Count != 1) continue;
                }
                var product = products[0];
                if (product.Item.TypeID != item.TypeID)
                {
                    failedRecipes.AddLoc($"{recipe.UILink()} has {item.UILink()} as a by-product. The main product of that recipe seems to be {product.Item.UILink()}.");
                    continue;
                }

                // Check the price of the by-products
                var byProducts = recipe.Items.Where(p => p != product).Select(p => ParseByProduct(p, resourceEfficiencyContext, craftingTable)).ToList();
                var unsetByProduct = byProducts.Where(p => float.IsPositiveInfinity(p.Price)).FirstOrDefault();
                if (unsetByProduct != null)
                {
                    failedRecipes.AddLoc($"No price set for the by-product {unsetByProduct.Product.Item.UILink()}, unable to process the recipe {recipe.UILink()}");
                    continue;
                }
                var byProductsPrice = byProducts.Sum(p => p.Price);
                var byProductText = LocString.Empty;
                if (byProducts.Any())
                {
                    var byProductFoldout = TextLoc.Foldout(TextLoc.StyledNum(byProductsPrice), Localizer.Do($"By-Products for {recipe.UILink()}"), byProducts.Select(bp => bp.Reason).FoldoutListLoc("By-Product", Eco.Shared.Items.TooltipOrigin.None));
                    explanation.AppendLineLoc($"By-Products: {byProductFoldout}");
                }

                // Labour cost
                var labourCost = recipe.Family.LaborInCalories.GetCurrentValue(User) * Config.CostPerCalory;
                explanation.AppendLineLoc($"Labour: UserModifiedCalories ({Text.StyledNum(recipe.Family.LaborInCalories.GetCurrentValue(User))}) * CostPerCalory ({Text.StyledNum(Config.CostPerCalory)}) = {Text.StyledNum(labourCost)}");

                // Ingredients cost
                var getIngredientPrice = (Item ingredient, IngredientElement element) =>
                {
                    if (!TryGetCostPrice(ingredient, out var tempPrice, out var reason, out var innerWarnings))
                        return new IngredientPrice(float.PositiveInfinity, ingredient, reason.ToStringLoc(), innerWarnings);

                    var ingredientCount = element.Quantity.GetCurrentValue(resourceEfficiencyContext, craftingTable);
                    var count = ingredientCount;
                    var countText = TextLoc.StyledNum(count);

                    if (item is WorldObjectItem)
                    {
                        count = element.Quantity.GetCurrentValueInt(resourceEfficiencyContext, craftingTable, WORLD_OBJECT_CAP_NUMBER) * 1f / WORLD_OBJECT_CAP_NUMBER;
                        countText = TextLoc.Foldout(TextLoc.StyledNum(count), Localizer.Do($"Count rounding reason"), Localizer.Do($"Crafting placable items are capped at crafting {WORLD_OBJECT_CAP_NUMBER} at a time, so the count got rounded up from {Text.StyledNum(ingredientCount)} to {Text.StyledNum(count)}"));
                    }
                    else if (item.IsTool)
                    {
                        count = element.Quantity.GetCurrentValueInt(resourceEfficiencyContext, craftingTable, TOOL_CAP_NUMBER) * 1f / TOOL_CAP_NUMBER;
                        countText = TextLoc.Foldout(TextLoc.StyledNum(count), Localizer.Do($"Count rounding reason"), Localizer.Do($"Tools are capped at crafting {TOOL_CAP_NUMBER} tool(s) at a time, so the count got rounded up from {Text.StyledNum(ingredientCount)} to {Text.StyledNum(count)}"));

                    }
                    var costPriceLink = TextLoc.FoldoutLoc($"CostPrice", $"Cost price of {ingredient.UILink()}", reason.ToStringLoc());
                    return new IngredientPrice(tempPrice * count, ingredient, Localizer.Do($"Ingredient {ingredient.UILink()}: Count ({countText}) * {costPriceLink} ({Text.StyledNum(tempPrice)}) = {Text.StyledNum(tempPrice * count)}"), innerWarnings);
                };

                var costs = recipe.Ingredients.Select(i => i.IsSpecificItem
                    ? getIngredientPrice(i.Item, i)
                    : i.Tag.TaggedItems().Select(ti => getIngredientPrice(ti, i)).OrderBy(x => x.Price).First());
                if (costs.Any(c => float.IsPositiveInfinity(c.Price)))
                {
                    failedRecipes.AddLoc($"For crafting {recipe.UILink()} we could not determine the price of {string.Join(", ", costs.Where(c => float.IsPositiveInfinity(c.Price)).Select(c => Localizer.Do($"{c.Item.UILink()} ({WhyFoldout(c)})")))}. Please run /ta setupbuy to add missing ingredients");
                    continue;
                };
                var ingredientsTotal = costs.Select(c => c.Price).Sum();
                costs.ForEach(c => explanation.AppendLine(c.Reason));
                var ingredientWarnings = costs.Where(c => c.Warnings != null).SelectMany(c => c.Warnings!).ToList();
                if (ingredientWarnings.Any())
                {
                    warnings ??= new List<LocString>();
                    warnings.AddRange(ingredientWarnings);
                }
                explanation.AppendLineLoc($"Ingredients Total: {Text.StyledNum(ingredientsTotal)}");

                var productCount = product.Quantity.GetCurrentValue(resourceEfficiencyContext, craftingTable);

                var totalCost = (ingredientsTotal - byProductsPrice + labourCost) / productCount;
                explanation.AppendLineLoc($"Total Cost: (Ingredients ({Text.StyledNum(ingredientsTotal)}) - ByProduct ({Text.StyledNum(byProductsPrice)}) + Labour Cost ({Text.StyledNum(labourCost)})) / ProductCount ({Text.StyledNum(productCount)}) = {Text.StyledNum(totalCost)}");
                if (bestPrice == null || totalCost < bestPrice.Price)
                {
                    if (bestPrice != null) { rejectedPrices.Add(bestPrice); }
                    bestPrice = new CachedPrice(totalCost, explanation, recipe);
                }
                else
                    rejectedPrices.Add(new CachedPrice(totalCost, explanation, recipe));
            }


            if (bestPrice == null)
            {
                failedRecipes.Insert(0, Localizer.Do($"Could not calculate the item cost from any of the provided recipes:\n"));
                bestPrice = new CachedPrice(float.PositiveInfinity, new StringBuilder(string.Join("\n", failedRecipes.Select(m => $"- {m}"))));
                CachedPrices.Add(item.TypeID, bestPrice);
                outPrice = bestPrice.Price;
                reason = bestPrice.Reason;
                return false;
            }
            else
            {
                if (failedRecipes.Count > 0 || rejectedPrices.Count > 0)
                {
                    var rejectedWarnings = new List<LocString>();
                    rejectedPrices
                        .Select(price => new
                        {
                            Price = price,
                            Foldout = TextLoc.FoldoutLoc($"{(price.Price > bestPrice.Price ? Localizer.Do($"greater than") : Localizer.Do($"equal to"))}", $"{item.UILink()} alternative cost price", price.Reason.ToStringLoc()),
                            // BestPrice * (1 + Percentage) = OtherPrice
                            // 1 + Percentage = OtherPrice / BestPrice
                            // Percentage = OtherPrice / BestPrice - 1
                            IncreasePercentage = bestPrice.Price <= 0 ? float.PositiveInfinity : (price.Price / bestPrice.Price - 1f),
                        })
                        .ForEach(x =>
                        {
                            failedRecipes.AddLoc($"{x.Price.Recipe.UILink()}'s price is{(x.IncreasePercentage > 0 ? (" " + Text.Negative(Text.Percent(x.IncreasePercentage))) : LocString.Empty)} {x.Foldout} the selected recipe's price.");
                            if (x.IncreasePercentage > Config.Profit / 100f)
                            {
                                rejectedWarnings.AddLoc($"Crafting {x.Price.Recipe.UILink()} will cause you to lose money. You should decrease the buy price of its ingredients");
                            }
                        });

                    if (rejectedWarnings.Count > 0)
                    {
                        warnings ??= new List<LocString>();
                        warnings.AddRange(rejectedWarnings);
                    }

                    var failedFoldout = TextLoc.FoldoutLoc($"these", $"Ignored recipes", new StringBuilder(string.Join("\n", failedRecipes.Select(m => $"- {m}"))).ToStringLoc());
                    bestPrice.Reason.Insert(0, Localizer.Do($"Ignored {failedFoldout} recipes.\n"));
                }
                if (warnings != null)
                    bestPrice = new CachedPrice(bestPrice.Price, bestPrice.Reason, bestPrice.Recipe, new List<LocString>(warnings));
                CachedPrices.Add(item.TypeID, bestPrice);
                outPrice = bestPrice.Price;
                reason = bestPrice.Reason;
                return true;
            }
        }

        private static LocString WhyFoldout(IngredientPrice price) => TextLoc.FoldoutLoc($"Why?", $"Why {price.Item.UILink()}", price.Reason);

        private ProductPrice ParseByProduct(CraftingElement product, ModuleContext resourceEfficiency, CraftingComponent craftingTable)
        {
            if (!StoreSellPrices.TryGetValue(product.Item.TypeID, out var storePrice))
                return new ProductPrice(product, float.PositiveInfinity, Localizer.Do($"No sell price set for the by-product {product.Item.UILink()}."));

            // SellPrice = CostPrice * (1 + Profit) / (1 - TaxRate)
            // CostPrice = SellPrice / (1 + Profit) * (1 - TaxRate)
            var costPrice = storePrice / (1 + Config.Profit / 100f) * (1 - EconomyManager.Tax.GetSalesTax(Store.Currency));
            var quantity = product.Quantity.GetCurrentValue(resourceEfficiency, craftingTable);
            var totalCostPrice = costPrice * quantity;

            var costReasonContent = new LocStringBuilder();
            costReasonContent.AppendLine(Localizer.Do($"CostPrice = SellPrice / (1 + Profit) * (1 - TaxRate)"));
            costReasonContent.AppendLine(Localizer.Do($"          = {Text.StyledNum(storePrice)} / (1 + {Text.StyledNum(Config.Profit / 100f)}) * (1 - {Text.StyledNum(EconomyManager.Tax.GetSalesTax(Store.Currency))})"));
            costReasonContent.AppendLine(Localizer.Do($"          = {Text.StyledNum(costPrice)}"));
            var costLink = TextLoc.Foldout(Localizer.Do($"CostPrice ({Text.StyledNum(costPrice)})"), Localizer.Do($"By-Product {product.Item.UILink()} cost price"), costReasonContent.ToLocString());

            var reason = Localizer.Do($"{Text.StyledNum(quantity)} {product.Item.UILink()} * {costLink} = {Text.StyledNum(totalCostPrice)}");
            return new ProductPrice(product, costPrice, reason);
        }
        private static bool TryGetStoreAndCraftingTables(StringBuilder sb, User user, out StoreComponent? store, out List<CraftingComponent>? craftingTables, out List<Item>? craftableItems)
        {
            store = null;
            craftingTables = null;
            craftableItems = null;

            // Get the plot the user is currently standing in
            var playerPlot = user.Position.XZi().ToPlotPos();

            // Check if the user is standing in a deed
            var accessedDeeds = PropertyManager.Obj.Deeds.Where(d => d.IsAuthorized(user.Player));
            var deedStandingIn = accessedDeeds.FirstOrDefault(d => d.Plots.Any(p => p.PlotPos == playerPlot));
            if (deedStandingIn == null)
            {
                sb.AppendLine(Localizer.Do($"You have to stand in a deed you have access to when you run this command"));
                return false;
            }

            // Get all the stores in the deed
            var stores = WorldObjectUtil.AllObjsWithComponent<StoreComponent>().Where(store => store.IsAuthorized(user, Eco.Shared.Items.AccessType.OwnerAccess) && deedStandingIn.Plots.Any(p => p.PlotPos == store.Parent.PlotPos()));
            if (!stores.Any())
            {
                sb.AppendLine(Localizer.Do($"You don't have a store you have owner access to in the plot you are standing in"));
                return false;
            }
            else if (stores.Take(2).Count() > 1)
            {
                sb.AppendLine(Localizer.Do($"You have more than one store on this property. The mod doesn't support that yet."));
                return false;
            }
            store = stores.First();

            // Get all the crafting tables in the deed
            craftingTables = WorldObjectUtil.AllObjsWithComponent<CraftingComponent>().Where(workbench => workbench.IsAuthorized(user, Eco.Shared.Items.AccessType.OwnerAccess) && deedStandingIn.Plots.Any(p => p.PlotPos == workbench.Parent.PlotPos())).ToList();
            if (!craftingTables.Any())
            {
                sb.AppendLine(Localizer.Do($"Could not find any crafting tables in {deedStandingIn.UILink()}"));
                return false;
            }

            // Check that the user can at least craft one recipe from the crafting tables
            craftableItems = craftingTables
                .SelectMany(w => w.Recipes
                    .Where(recipe => recipe.RequiredSkills.All(s => s.IsMet(user)))
                    .SelectMany(rf => rf.CraftableDefault ? rf.Recipes : rf.Recipes.Skip(1))
                    .SelectMany(r => r.Items)
                    .Select(p => p.Item)
                    .DistinctBy(p => p.TypeID)
                ).ToList();
            if (!craftableItems.Any())
            {
                sb.AppendLine(Localizer.Do($"You don't have the required skills/levels to craft any of the recipes in these crafting tables: {string.Join(", ", craftingTables.Select(w => w.Parent.UILink()))}"));
                return false;
            }

            return true;
        }

        public List<LocString> SetPrice(Item item, float newPrice)
        {
            StoreSellPrices[item.TypeID] = newPrice;

            var msgs = new List<LocString>();
            Store.StoreData.SellOffers.Where(o => o.Stack.Item.SameType(item) && o.Price != newPrice).ForEach(o =>
            {
                msgs.AddLoc($"Updating price of {item.UILink()} from {Text.StyledNum(o.Price)} to {Text.StyledNum(newPrice)}");
                o.Price = newPrice;
            });
            return msgs;
        }
    }
}

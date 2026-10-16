using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeAssistant
{
    /// <summary>Whether a recipe is treated as a craft (workbench/specialty) or cook (food/heat) recipe.</summary>
    public enum RecipeType
    {
        Craft,
        Cook
    }

    /// <summary>Per-item production quantities, split by recipe type. Zero/unset means "not configured".</summary>
    [Serialized]
    public class RecipeQuantitySetting
    {
        [Serialized] public int CraftQty { get; set; }
        [Serialized] public int CookQty { get; set; }
    }

    /// <summary>Central helper for classifying recipes as craft or cook and resolving the production
    /// quantity configured for a recipe's main product.</summary>
    public static class RecipeTypeResolver
    {
        // Cooking-class workbench item type names (verified against Eco.Mods.dll through a
        // MetadataLoadContext probe on the reference assemblies). These are the vanilla workbenches
        // whose recipes produce food: campfire, cast-iron stove, bakery oven, kitchen and stove.
        private static readonly HashSet<string> CookingWorkbenchTypeNames = new(StringComparer.Ordinal)
        {
            "CampfireItem",
            "CastIronStoveItem",
            "BakeryOvenItem",
            "KitchenItem",
            "StoveItem",
        };

        /// <summary>Classifies a recipe: cook when it is produced on a cooking workbench (campfire,
        /// stove, oven, kitchen...) or when any of its products is a food item; otherwise craft.</summary>
        public static RecipeType GetRecipeType(Recipe recipe)
        {
            var table = recipe.Family?.CraftingTable;
            if (table != null && IsCookingWorkbench(table))
                return RecipeType.Cook;

            if (recipe.Products.Any(p => p.Item is FoodItem))
                return RecipeType.Cook;

            return RecipeType.Craft;
        }

        public static bool IsCookingWorkbench(Item table)
        {
            if (table == null) return false;
            return CookingWorkbenchTypeNames.Contains(table.GetType().Name);
        }

        /// <summary>The item the recipe is primarily producing: its only product, or the first product
        /// that has not been marked as a by-product.</summary>
        public static Item MainProductOf(Recipe recipe, UserConfig config)
        {
            if (recipe.Products.Count == 1)
                return recipe.Products[0].Item;

            var main = recipe.Products.Select(p => p.Item).FirstOrDefault(p => !config.ByProducts.Contains(p.TypeID));
            return main ?? recipe.Products[0].Item;
        }

        /// <summary>Returns the configured production quantity for the recipe's main product, or 1
        /// (the historical default of "one batch") when the item has no configuration.</summary>
        public static int ProductionQuantity(Recipe recipe, UserConfig config)
        {
            if (config.RecipeQuantities.TryGetValue(MainProductOf(recipe, config).TypeID, out var setting))
            {
                var qty = GetRecipeType(recipe) == RecipeType.Cook ? setting.CookQty : setting.CraftQty;
                return Math.Max(0, qty);
            }
            return 1;
        }
    }
}

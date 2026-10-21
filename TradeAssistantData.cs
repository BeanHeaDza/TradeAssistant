using Eco.Core.Controller;
using Eco.Core.Serialization;
using Eco.Core.Utils;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Gameplay.PropertyHandling;
using Eco.Gameplay.Settlements;
using Eco.Gameplay.Utils;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Utils;
using PropertyChanged;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace TradeAssistant
{
    [Serialized]
    public class TradeAssistantData : Singleton<TradeAssistantData>, IStorage
    {
        [Serialized] public ThreadSafeDictionary<int, UserConfig> UserConfiguration = new();
        public IPersistent? StorageHandle { get; set; }
    }

    [Serialized]
    public class UserConfig
    {
        [Serialized] public float Profit { get; set; } = 20;
        [Serialized] public float CostPer1000Calories { get; set; } = 1f;
        [Serialized] public float SoonToExpireDiscount { get; set; } = 0f;
        [Serialized] public float SoonToExpireMinFreshness { get; set; } = 0f;
        [Serialized] public float SoonToExpireMaxFreshness { get; set; } = 50f;
        [Serialized] public string SoonToExpireCategoryName { get; set; } = "SOON TO EXPIRE";
        [Serialized] public ThreadSafeList<int> ByProducts { get; set; } = new();
        [Serialized] public ThreadSafeList<int> FrozenSellPrices { get; set; } = new();
        [Serialized] public ThreadSafeList<int> PartnerPlayers { get; set; } = new();
        [Serialized] public bool DurabilityPricingEnabled { get; set; } = false;
        [Serialized] public float DurabilityMinPricePercent { get; set; } = 50f;
        [Serialized] public float QualityPremiumPercent { get; set; } = 0f;
        [Serialized] public ThreadSafeDictionary<string, float> CategoryDiscounts { get; set; } = new();
        [Serialized] public bool AutoRepricingEnabled { get; set; } = false;
        [Serialized] public float AutoRepricingIntervalMinutes { get; set; } = 30f;
        [Serialized] public bool CompetitivePricingEnabled { get; set; } = false;
        [Serialized] public float CompetitiveFloorPercent { get; set; } = 85f;
        [Serialized] public float CompetitiveCeilingPercent { get; set; } = 0f;
        [Serialized] public ThreadSafeList<string> CompetitiveCategories { get; set; } = new();
        [Serialized] public string? SelectedStoreName { get; set; } = null;
        [Serialized] public ThreadSafeDictionary<int, RecipeQuantitySetting> RecipeQuantities { get; set; } = new();
        [Serialized] public bool BuyPriceAutoEnabled { get; set; } = false;
        [Serialized] public string BuyPriceAutoMode { get; set; } = "highest";
        [Serialized] public float BuyPriceAutoPercent { get; set; } = 80f;
        // 0.9.0: crafting fee tax support. When IncludeCraftingFeeTax is true the sell-price
        // formula adds the crafting fee factor: SellPrice = Cost * (1 + FeeRate) * (1 + Profit) / (1 - TaxRate).
        // CraftingFeeTaxOverridePercent 0 = auto-read the settlement's real crafting fee tax; >0 = manual override.
        [Serialized] public bool IncludeCraftingFeeTax { get; set; } = true;
        [Serialized] public float CraftingFeeTaxOverridePercent { get; set; } = 0f;

        public UserConfigUI ToUI()
        {
            UserConfigUI ui = new()
            {
                Profit = Profit,
                CostPerThousandCalories = CostPer1000Calories,
                SoonToExpireDiscount = SoonToExpireDiscount,
                SoonToExpireMinFreshness = SoonToExpireMinFreshness,
                SoonToExpireMaxFreshness = SoonToExpireMaxFreshness,
                AutoRepricingEnabled = AutoRepricingEnabled,
                AutoRepricingIntervalMinutes = AutoRepricingIntervalMinutes,
                CompetitivePricingEnabled = CompetitivePricingEnabled,
                CompetitiveFloorPercent = CompetitiveFloorPercent,
                CompetitiveCeilingPercent = CompetitiveCeilingPercent,
                BuyPriceAutoEnabled = BuyPriceAutoEnabled,
                BuyPriceAutoPercent = BuyPriceAutoPercent,
                IncludeCraftingFeeTax = IncludeCraftingFeeTax,
                CraftingFeeTaxOverridePercent = CraftingFeeTaxOverridePercent,
                SoonToExpireCategoryName = SoonToExpireCategoryName,
                SelectedStoreName = SelectedStoreName,
                CompetitiveCategoriesText = string.Join(", ", CompetitiveCategories),
                CategoryDiscountsText = string.Join("; ", CategoryDiscounts.Select(kv => $"{kv.Key}:{kv.Value}")),
                RecipeQuantitiesText = RecipeQuantities.Count == 0
                    ? string.Empty
                    : string.Join("; ", RecipeQuantities.Select(kv => $"{Item.Get(kv.Key)?.Name ?? kv.Key.ToString()}: cook={kv.Value.CookQty} craft={kv.Value.CraftQty}"))
            };
            ByProducts.Select(id => Item.Get(id)).Where(p => p != null).ForEach(ui.ByProducts.Add);
            FrozenSellPrices.Select(id => Item.Get(id)).Where(p => p != null).ForEach(ui.FrozenSellPrices.Add);
            PartnerPlayers.Select(id => UserManager.FindUserByID(id)).Where(p => p != null).ForEach(ui.Partners.Add);

            return ui;
        }

        public void UpdateFromUI(UserConfigUI config)
        {
            Profit = config.Profit <= -100 ? -99f : config.Profit;
            CostPer1000Calories = config.CostPerThousandCalories;
            SoonToExpireDiscount = Math.Clamp(config.SoonToExpireDiscount, 0f, 100f);
            SoonToExpireMinFreshness = Math.Clamp(config.SoonToExpireMinFreshness, 0f, 100f);
            SoonToExpireMaxFreshness = Math.Clamp(config.SoonToExpireMaxFreshness, 0f, 100f);
            AutoRepricingEnabled = config.AutoRepricingEnabled;
            AutoRepricingIntervalMinutes = Math.Clamp(config.AutoRepricingIntervalMinutes, 1f, 1440f);
            BuyPriceAutoEnabled = config.BuyPriceAutoEnabled;
            BuyPriceAutoPercent = Math.Clamp(config.BuyPriceAutoPercent, 1f, 200f);
            IncludeCraftingFeeTax = config.IncludeCraftingFeeTax;
            CraftingFeeTaxOverridePercent = Math.Clamp(config.CraftingFeeTaxOverridePercent, 0f, 100f);
            // 1.2.1: write back the three competitive-pricing settings from the UI (they were missing).
            CompetitivePricingEnabled = config.CompetitivePricingEnabled;
            CompetitiveFloorPercent = Math.Clamp(config.CompetitiveFloorPercent, 0f, 100f);
            CompetitiveCeilingPercent = Math.Clamp(config.CompetitiveCeilingPercent, 0f, 100f);
            // 1.1.2: configurable "SOON TO EXPIRE" category name.
            if (!string.IsNullOrWhiteSpace(config.SoonToExpireCategoryName))
                SoonToExpireCategoryName = config.SoonToExpireCategoryName.Trim();
            // 1.1.4: expose remaining config entries through the /ta config UI.
            SelectedStoreName = string.IsNullOrWhiteSpace(config.SelectedStoreName) ? null : config.SelectedStoreName.Trim();
            CompetitiveCategories.Clear();
            if (!string.IsNullOrWhiteSpace(config.CompetitiveCategoriesText))
                foreach (var name in config.CompetitiveCategoriesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!string.IsNullOrEmpty(name))
                        CompetitiveCategories.Add(name);
            CategoryDiscounts.Clear();
            if (!string.IsNullOrWhiteSpace(config.CategoryDiscountsText))
                foreach (var part in config.CategoryDiscountsText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var kv = part.Split(':', 2);
                    if (kv.Length == 2 && float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
                        CategoryDiscounts[kv[0].Trim()] = Math.Clamp(percent, 0f, 100f);
                }
            ByProducts.Clear();
            ByProducts.AddRange(config.ByProducts.Select(p => p.TypeID));
            FrozenSellPrices.Clear();
            FrozenSellPrices.AddRange(config.FrozenSellPrices.Select(p => p.TypeID));
            PartnerPlayers.Clear();
            PartnerPlayers.AddRange(config.Partners.Select(p => p.Id));
            
        }
    }

    public class UserConfigUI : IController, INotifyPropertyChanged, Eco.Core.PropertyHandling.INotifyPropertyChangedInvoker, IHasClientControlledContainers
    {
        [Eco] public float Profit { get; set; } = 20;
        [Eco] public float CostPerThousandCalories { get; set; } = 1f;
        [Eco, Range(0f, 100f)] public float SoonToExpireDiscount { get; set; } = 0f;
        [Eco, Range(0f, 100f)] public float SoonToExpireMinFreshness { get; set; } = 0f;
        [Eco, Range(0f, 100f)] public float SoonToExpireMaxFreshness { get; set; } = 50f;
        [Eco] public bool AutoRepricingEnabled { get; set; } = false;
        [Eco, Range(1f, 1440f)] public float AutoRepricingIntervalMinutes { get; set; } = 30f;
        [Eco] public bool CompetitivePricingEnabled { get; set; } = false;
        [Eco, Range(0f, 100f)] public float CompetitiveFloorPercent { get; set; } = 85f;
        [Eco, Range(0f, 100f)] public float CompetitiveCeilingPercent { get; set; } = 0f;
        [Eco] public bool BuyPriceAutoEnabled { get; set; } = false;
        [Eco, Range(1f, 200f)] public float BuyPriceAutoPercent { get; set; } = 80f;
        [Eco] public bool IncludeCraftingFeeTax { get; set; } = true;
        [Eco, Range(0f, 100f)] public float CraftingFeeTaxOverridePercent { get; set; } = 0f;
        [Eco] public string? SoonToExpireCategoryName { get; set; } = "SOON TO EXPIRE";
        [Eco] public string? SelectedStoreName { get; set; } = null;
        [Eco] public string? CompetitiveCategoriesText { get; set; } = null;
        [Eco] public string? CategoryDiscountsText { get; set; } = null;
        [Eco] public string? RecipeQuantitiesText { get; set; } = null;
        [Eco, AllowEmpty] public ControllerList<Item> ByProducts { get; set; }
        [Eco, AllowEmpty] public ControllerList<Item> FrozenSellPrices { get; set; }
        [Eco, AllowEmpty] public ControllerList<User> Partners { get; set; }

        public UserConfigUI()
        {
            ByProducts = new ControllerList<Item>(this, nameof(ByProducts));
            FrozenSellPrices = new ControllerList<Item>(this, nameof(FrozenSellPrices));
            Partners = new ControllerList<User>(this, nameof(Partners));
        }


        #region IController
        public event PropertyChangedEventHandler? PropertyChanged;
        int controllerID;
        [DoNotNotify] public ref int ControllerID => ref controllerID;

        public void InvokePropertyChanged(PropertyChangedEventArgs eventArgs)
        {
            if (PropertyChanged == null)
                return;
            PropertyChanged(this, eventArgs);
        }

        protected void OnPropertyChanged(string propertyName, object before, object after) => PropertyChangedNotificationInterceptor.Intercept(this, propertyName, before, after);
        #endregion
    }
}
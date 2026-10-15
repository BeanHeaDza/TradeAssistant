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
                CompetitiveCeilingPercent = CompetitiveCeilingPercent
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
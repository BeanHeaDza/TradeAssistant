using Eco.Core.Controller;
using Eco.Core.Serialization;
using Eco.Core.Utils;
using Eco.Gameplay.Items;
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
        [Serialized] public ThreadSafeList<int> ByProducts { get; set; } = new();

        public UserConfigUI ToUI()
        {
            UserConfigUI ui = new UserConfigUI
            {
                Profit = Profit,
                CostPerThousandCalories = CostPer1000Calories
            };
            ByProducts.Select(id => Item.Get(id)).Where(p => p != null).ForEach(p => ui.ByProducts.Add(p));
            return ui;
        }

        public void UpdateFromUI(UserConfigUI config)
        {
            Profit = config.Profit <= -100 ? -99f : config.Profit;
            CostPer1000Calories = config.CostPerThousandCalories;
            ByProducts.Clear();
            ByProducts.AddRange(config.ByProducts.Select(p => p.TypeID));
        }
    }

    public class UserConfigUI : IController, INotifyPropertyChanged, Eco.Core.PropertyHandling.INotifyPropertyChangedInvoker, IHasClientControlledContainers
    {
        [Eco] public float Profit { get; set; } = 20;
        [Eco] public float CostPerThousandCalories { get; set; } = 1f;
        [Eco, AllowEmpty] public ControllerList<Item> ByProducts { get; set; }

        public UserConfigUI()
        {
            ByProducts = new ControllerList<Item>(this, nameof(ByProducts));
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
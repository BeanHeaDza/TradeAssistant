using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Players;
using Eco.Gameplay.Settlements;
using Eco.Gameplay.Systems.Messaging.Notifications;
using Eco.Shared.Localization;
using Eco.Shared.Services;
using Eco.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradeAssistant
{
    public static class Extensions
    {
        public static void TempServerMessage(this User user, StringBuilder message, NotificationCategory category = NotificationCategory.Notifications, NotificationStyle style = NotificationStyle.Chat)
        {
            TempServerMessage(user, message.ToStringLoc(), category, style);
        }
        public static void TempServerMessage(this User user, LocString message, NotificationCategory category = NotificationCategory.Notifications, NotificationStyle style = NotificationStyle.Chat)
        {
            NotificationManager.TemporaryServerMessageToPlayer(message, user, category, style);
        }

        public static UserConfig Config(this User user)
        {
            if (!TradeAssistantData.Obj.UserConfiguration.TryGetValue(user.Id, out var config))
            {
                config = new UserConfig();
                TradeAssistantData.Obj.UserConfiguration.Add(user.Id, config);
            }

            return config;
        }

        public static void AddLoc(this List<LocString> list, FormattableString msg)
        {
            list.Add(Localizer.Do(msg));
        }

        public static float GetTax(this StoreComponent store)
        {
            var settlement = SettlementUtils.GetSettlementsAtPos(store.Parent.Position3i);
            if (settlement == null) return 0;
            return settlement.Sum(s => s.Taxes.GetSalesTax(store.Currency));
        }

        public static string ToStyledNum(this float number)
        {
            return Text.StyledNum(number);
        }
    }
}

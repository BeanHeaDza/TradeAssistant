using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Items;
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
        public static IEnumerable<int> OfferedItemTypeIDs(this TradeOffer offer)
        {
            if (offer.Stack?.Item != null)
                return new[] { offer.Stack.Item.TypeID };

            return Item.AllItemsExceptHidden
                .Where(offer.MeetsSpecialRequirements)
                .Select(i => i.TypeID);
        }

        public static bool MatchesTypeID(this TradeOffer offer, int typeID)
        {
            if (offer.Stack?.Item != null)
                return offer.Stack.Item.TypeID == typeID;

            return offer.MeetsSpecialRequirements(Item.Get(typeID));
        }

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
            // 1.3.1: guard against null currency / missing settlement data before querying taxes;
            // a store without a currency cannot have a meaningful sales tax contribution.
            if (store == null || store.Currency == null) return 0;
            var settlement = SettlementUtils.GetSettlementsAtPos(store.Parent.Position3i);
            if (settlement == null || !settlement.Any()) return 0;
            return settlement.Sum(s => s.Taxes?.GetSalesTax(store.Currency) ?? 0f);
        }

        /// <summary>
        /// Returns the crafting fee tax rate (0..1) applied by the settlement(s) covering this
        /// store's plot for the store's currency. The crafting fee tax is charged to the crafter
        /// whenever an item is crafted inside the settlement (SettlementTaxes.CraftingFeeTax /
        /// GetCraftingFeeTax), so it behaves as an extra production cost on top of the recipe cost.
        /// </summary>
        public static float GetCraftingFeeTax(this StoreComponent store)
        {
            // Mirror the GetTax guards: no currency / no settlement means no meaningful tax.
            if (store == null || store.Currency == null) return 0;
            var settlement = SettlementUtils.GetSettlementsAtPos(store.Parent.Position3i);
            if (settlement == null || !settlement.Any()) return 0;
            return settlement.Sum(s => s.Taxes?.GetCraftingFeeTax(store.Currency) ?? 0f);
        }

        /// <summary>
        /// Effective crafting-fee tax rate (0..1) used by the price calculations: 0 when the
        /// feature is disabled, the configured manual override when set (greater than 0),
        /// otherwise the real crafting fee tax of the settlement the store stands in.
        /// </summary>
        public static float CraftingFeeTaxRate(this TradeAssistantCalculator calc)
        {
            if (calc.Config.IncludeCraftingFeeTax == false) return 0f;
            if (calc.Config.CraftingFeeTaxOverridePercent > 0f)
                return calc.Config.CraftingFeeTaxOverridePercent / 100f;
            return calc.Store.GetCraftingFeeTax();
        }

        public static string ToStyledNum(this float number)
        {
            return Text.StyledNum(number);
        }
    }
}

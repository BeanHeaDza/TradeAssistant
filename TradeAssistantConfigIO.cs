using Eco.Core.Utils;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TradeAssistant
{
    /// <summary>
    /// Portable snapshot of a <see cref="UserConfig"/> used to migrate a player's configuration
    /// across servers. Item and player references are stored as names so the snapshot can be
    /// re-linked to server-local TypeIDs / user IDs on import.
    /// </summary>
    public class UserConfigSnapshot
    {
        public int Version { get; set; } = 1;
        public float Profit { get; set; } = 20f;
        public float CostPer1000Calories { get; set; } = 1f;
        public float SoonToExpireDiscount { get; set; } = 0f;
        public float SoonToExpireMinFreshness { get; set; } = 0f;
        public float SoonToExpireMaxFreshness { get; set; } = 50f;
        public bool DurabilityPricingEnabled { get; set; } = false;
        public float DurabilityMinPricePercent { get; set; } = 50f;
        public float QualityPremiumPercent { get; set; } = 0f;
        public bool AutoRepricingEnabled { get; set; } = false;
        public float AutoRepricingIntervalMinutes { get; set; } = 30f;
        public bool CompetitivePricingEnabled { get; set; } = false;
        public float CompetitiveFloorPercent { get; set; } = 85f;
        public float CompetitiveCeilingPercent { get; set; } = 0f;
        public List<string> CompetitiveCategories { get; set; } = new();
        public List<string> ByProducts { get; set; } = new();
        public List<string> FrozenSellPrices { get; set; } = new();
        public List<string> PartnerPlayers { get; set; } = new();
        public Dictionary<string, float> CategoryDiscounts { get; set; } = new();
        public Dictionary<string, RecipeQuantitySetting> RecipeQuantities { get; set; } = new();
        public string? SelectedStoreName { get; set; }
        public bool BuyPriceAutoEnabled { get; set; } = false;
        public string BuyPriceAutoMode { get; set; } = "highest";
        public float BuyPriceAutoPercent { get; set; } = 80f;
    }

    public static class TradeAssistantConfigIO
    {
        /// <summary>Exports the configuration as a pretty JSON string (for display) and a Base64
        /// payload (for copy/paste through chat commands, which split arguments on spaces).</summary>
        public static (string Json, string Base64) Export(UserConfig config)
        {
            var snapshot = BuildSnapshot(config);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var compact = JsonSerializer.Serialize(snapshot);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(compact));
            return (json, base64);
        }

        /// <summary>Imports a configuration from either a Base64 payload (preferred) or a raw JSON
        /// string. Item and player references are resolved against this server; unresolvable names
        /// are skipped and reported as warnings.</summary>
        public static (UserConfig Config, List<string> Warnings) Import(string payload)
        {
            var json = DecodePayload(payload);
            var snapshot = JsonSerializer.Deserialize<UserConfigSnapshot>(json);
            if (snapshot == null)
                throw new InvalidOperationException("the payload is not a valid Trade Assistant configuration");

            var warnings = new List<string>();
            var config = new UserConfig
            {
                Profit = snapshot.Profit,
                CostPer1000Calories = snapshot.CostPer1000Calories,
                SoonToExpireDiscount = snapshot.SoonToExpireDiscount,
                SoonToExpireMinFreshness = snapshot.SoonToExpireMinFreshness,
                SoonToExpireMaxFreshness = snapshot.SoonToExpireMaxFreshness,
                DurabilityPricingEnabled = snapshot.DurabilityPricingEnabled,
                DurabilityMinPricePercent = snapshot.DurabilityMinPricePercent,
                QualityPremiumPercent = snapshot.QualityPremiumPercent,
                AutoRepricingEnabled = snapshot.AutoRepricingEnabled,
                AutoRepricingIntervalMinutes = snapshot.AutoRepricingIntervalMinutes,
                CompetitivePricingEnabled = snapshot.CompetitivePricingEnabled,
                CompetitiveFloorPercent = snapshot.CompetitiveFloorPercent,
                CompetitiveCeilingPercent = snapshot.CompetitiveCeilingPercent,
                CompetitiveCategories = ToStringList(snapshot.CompetitiveCategories),
                ByProducts = ResolveItems(snapshot.ByProducts, warnings),
                FrozenSellPrices = ResolveItems(snapshot.FrozenSellPrices, warnings),
                PartnerPlayers = ResolveUsers(snapshot.PartnerPlayers, warnings),
                CategoryDiscounts = ToDiscountDictionary(snapshot.CategoryDiscounts),
                RecipeQuantities = ResolveRecipeQuantities(snapshot.RecipeQuantities, warnings),
                SelectedStoreName = snapshot.SelectedStoreName,
                BuyPriceAutoEnabled = snapshot.BuyPriceAutoEnabled,
                BuyPriceAutoMode = NormalizeMode(snapshot.BuyPriceAutoMode),
                BuyPriceAutoPercent = snapshot.BuyPriceAutoPercent
            };
            return (config, warnings);
        }

        private static UserConfigSnapshot BuildSnapshot(UserConfig config) => new()
        {
            Profit = config.Profit,
            CostPer1000Calories = config.CostPer1000Calories,
            SoonToExpireDiscount = config.SoonToExpireDiscount,
            SoonToExpireMinFreshness = config.SoonToExpireMinFreshness,
            SoonToExpireMaxFreshness = config.SoonToExpireMaxFreshness,
            DurabilityPricingEnabled = config.DurabilityPricingEnabled,
            DurabilityMinPricePercent = config.DurabilityMinPricePercent,
            QualityPremiumPercent = config.QualityPremiumPercent,
            AutoRepricingEnabled = config.AutoRepricingEnabled,
            AutoRepricingIntervalMinutes = config.AutoRepricingIntervalMinutes,
            CompetitivePricingEnabled = config.CompetitivePricingEnabled,
            CompetitiveFloorPercent = config.CompetitiveFloorPercent,
            CompetitiveCeilingPercent = config.CompetitiveCeilingPercent,
            CompetitiveCategories = config.CompetitiveCategories?.ToList() ?? new List<string>(),
            ByProducts = (config.ByProducts ?? new ThreadSafeList<int>()).Select(ItemNameOf).ToList(),
            FrozenSellPrices = (config.FrozenSellPrices ?? new ThreadSafeList<int>()).Select(ItemNameOf).ToList(),
            PartnerPlayers = (config.PartnerPlayers ?? new ThreadSafeList<int>()).Select(UserNameOf).ToList(),
            CategoryDiscounts = (config.CategoryDiscounts ?? new ThreadSafeDictionary<string, float>())
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            RecipeQuantities = (config.RecipeQuantities ?? new ThreadSafeDictionary<int, RecipeQuantitySetting>())
                .ToDictionary(kv => ItemNameOf(kv.Key), kv => new RecipeQuantitySetting { CraftQty = kv.Value.CraftQty, CookQty = kv.Value.CookQty }),
            SelectedStoreName = config.SelectedStoreName,
            BuyPriceAutoEnabled = config.BuyPriceAutoEnabled,
            BuyPriceAutoMode = NormalizeMode(config.BuyPriceAutoMode),
            BuyPriceAutoPercent = config.BuyPriceAutoPercent
        };

        private static string NormalizeMode(string? mode) =>
            mode != null && mode.Equals("median", StringComparison.OrdinalIgnoreCase) ? "median" : "highest";

        private static string DecodePayload(string payload)
        {
            var candidate = payload?.Trim() ?? throw new InvalidOperationException("empty payload");
            // Prefer Base64 when it looks like one: JSON never passes the Base64 alphabet check
            // (characters like '{', '"', ':' are rejected by Convert.FromBase64String), so a
            // successful decode is a strong signal.
            if (candidate.Length % 4 == 0)
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(candidate));
                    if (!string.IsNullOrWhiteSpace(decoded) && decoded.TrimStart().StartsWith("{"))
                        return decoded;
                }
                catch (FormatException) { }
            }
            return candidate;
        }

        private static ThreadSafeList<string> ToStringList(List<string>? names)
        {
            var result = new ThreadSafeList<string>();
            if (names != null)
                result.AddRange(names);
            return result;
        }

        private static ThreadSafeDictionary<string, float> ToDiscountDictionary(Dictionary<string, float>? discounts)
        {
            var result = new ThreadSafeDictionary<string, float>();
            if (discounts != null)
                foreach (var pair in discounts)
                    result[pair.Key] = pair.Value;
            return result;
        }

        private static ThreadSafeList<int> ResolveItems(List<string>? names, List<string> warnings)
        {
            var result = new ThreadSafeList<int>();
            if (names == null) return result;
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var item = Item.AllItemsExceptHidden.FirstOrDefault(i =>
                    string.Equals(i.DisplayName, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                {
                    warnings.Add($"Could not resolve item '{name}' on this server; skipped.");
                    continue;
                }
                result.Add(item.TypeID);
            }
            return result;
        }

        private static ThreadSafeDictionary<int, RecipeQuantitySetting> ResolveRecipeQuantities(Dictionary<string, RecipeQuantitySetting>? map, List<string> warnings)
        {
            var result = new ThreadSafeDictionary<int, RecipeQuantitySetting>();
            if (map == null) return result;
            foreach (var pair in map)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                var item = Item.AllItemsExceptHidden.FirstOrDefault(i =>
                    string.Equals(i.DisplayName, pair.Key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                {
                    warnings.Add($"Could not resolve item '{pair.Key}' for recipe quantities on this server; skipped.");
                    continue;
                }
                result[item.TypeID] = new RecipeQuantitySetting { CraftQty = pair.Value.CraftQty, CookQty = pair.Value.CookQty };
            }
            return result;
        }

        private static ThreadSafeList<int> ResolveUsers(List<string>? names, List<string> warnings)
        {
            var result = new ThreadSafeList<int>();
            if (names == null) return result;
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var user = UserManager.Users.FirstOrDefault(u =>
                    string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
                if (user == null)
                {
                    warnings.Add($"Could not resolve player '{name}' on this server; skipped.");
                    continue;
                }
                result.Add(user.Id);
            }
            return result;
        }

        private static string ItemNameOf(int typeID)
        {
            var item = Item.Get(typeID);
            return item?.Name ?? typeID.ToString();
        }

        private static string UserNameOf(int userId)
        {
            var user = UserManager.FindUserByID(userId);
            return user?.Name ?? userId.ToString();
        }
    }
}

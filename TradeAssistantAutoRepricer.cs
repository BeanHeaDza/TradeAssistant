using Eco.Core.Plugins;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Components.Store.Internal;
using Eco.Gameplay.Economy;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Gameplay.StrangeCloudGameplay;
using Eco.Gameplay.Systems;
using Eco.Gameplay.Systems.EcoMarketplace;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Gameplay.Systems.TextLinks;
using Eco.Gameplay.Utils;
using Eco.Shared;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TradeAssistant
{
    /// <summary>
    /// Optional automatic re-pricing: periodically re-runs the normal /ta update pricing pass for
    /// every online player who enabled it, and also schedules an immediate pass when market activity
    /// happens (new exchange trades or sell-offer changes, e.g. after a tax change or after unlocking
    /// new recipes and adding new offers).
    /// </summary>
    public static class TradeAssistantAutoRepricer
    {
        private const double TickIntervalSeconds = 5.0;
        private const double EventDebounceSeconds = 10.0;

        private static bool _subscribed;
        private static readonly HashSet<int> _runningUsers = new();
        private static readonly HashSet<int> _pendingRepriceUserIds = new();
        private static DateTime _lastTickUtc = DateTime.MinValue;
        private static DateTime _lastEventUtc = DateTime.MinValue;
        private static readonly Dictionary<int, DateTime> NextRunUtcByUser = new();

        /// <summary>Subscribes to market activity events exactly once.</summary>
        public static void EnsureStarted()
        {
            if (_subscribed) return;
            _subscribed = true;
            EconomyTracker.NewTradesAddedEvent.Add(OnMarketActivity);
            StoreItemData.SellOffersChangedEvent.Add(OnStoreActivity);
        }

        /// <summary>Called by the plugin each server tick; throttled internally.</summary>
        public static void Tick()
        {
            EnsureStarted();

            var now = DateTime.UtcNow;
            if ((now - _lastTickUtc).TotalSeconds < TickIntervalSeconds) return;
            _lastTickUtc = now;

            var enabledUserIds = TradeAssistantData.Obj.UserConfiguration
                .Where(kv => kv.Value.AutoRepricingEnabled)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var userId in enabledUserIds)
            {
                var due = !NextRunUtcByUser.TryGetValue(userId, out var next) || now >= next;
                if (!due && !_pendingRepriceUserIds.Contains(userId)) continue;

                var user = UserManager.FindUserByID(userId);
                if (user == null || !user.IsOnline)
                {
                    // 1.1.3: offline players that fell due are flagged so the market changes that
                    // happened while they were away are caught up when they come online.
                    _pendingRepriceUserIds.Add(userId);
                    if (!NextRunUtcByUser.ContainsKey(userId))
                        NextRunUtcByUser[userId] = now;
                    continue;
                }

                _pendingRepriceUserIds.Remove(userId);
                NextRunUtcByUser[userId] = now.AddMinutes(Math.Max(0.5, user.Config().AutoRepricingIntervalMinutes));
                _ = RunRepricingAsync(user);
            }
        }

        private static void OnMarketActivity(IEnumerable<TradeTracker> _) => ScheduleSoon();

        private static void OnStoreActivity(StoreItemData _) => ScheduleSoon();

        /// <summary>Marks every enabled user as due now, unless we are the ones that just modified offers.</summary>
        private static void ScheduleSoon()
        {
            if (_runningUsers.Count > 0) return;

            var now = DateTime.UtcNow;
            if ((now - _lastEventUtc).TotalSeconds < EventDebounceSeconds) return;
            _lastEventUtc = now;

            foreach (var kv in TradeAssistantData.Obj.UserConfiguration.Where(kv => kv.Value.AutoRepricingEnabled))
            {
                NextRunUtcByUser[kv.Key] = now;
                // 1.1.3: market events that happen while the player is offline are queued for catch-up.
                var target = UserManager.FindUserByID(kv.Key);
                if (target == null || !target.IsOnline)
                    _pendingRepriceUserIds.Add(kv.Key);
            }
        }

        // 1.2.3: per-user concurrency — a long repricing pass for one user no longer blocks or
        // drops repricing for every other user; runs requested while a user's own pass is in
        // flight are queued via _pendingRepriceUserIds and re-run as soon as the pass finishes.
        private static async Task RunRepricingAsync(User user)
        {
            if (!_runningUsers.Add(user.Id)) return;
            try
            {
                var calc = await TradeAssistantCalculator.TryInitialize(user);
                if (calc == null) return;

                var result = TradeAssistantRepricing.RunUpdate(calc, null, competitivePricing: true);
                if (result.TotalUpdates == 0)
                    return;

                user.TempServerMessage(Localizer.Do($"Automatic re-pricing updated {Text.StyledNum(result.TotalUpdates)} order(s) at {calc.Store.Parent.UILink()}."));
            }
            catch (Exception e)
            {
                user.TempServerMessage(Localizer.Do($"Automatic re-pricing failed: {e.Message}"));
            }
            finally
            {
                _runningUsers.Remove(user.Id);
                // 1.2.3: catch-up run for work that arrived while this pass was in flight.
                if (_pendingRepriceUserIds.Remove(user.Id))
                {
                    var refreshed = UserManager.FindUserByID(user.Id);
                    if (refreshed != null && refreshed.IsOnline)
                        _ = RunRepricingAsync(refreshed);
                }
            }
        }
    }
}

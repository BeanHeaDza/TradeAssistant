using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradeAssistant
{
    internal class TradeAssistantPlugin : Singleton<TradeAssistantPlugin>, IModKitPlugin, ISaveablePlugin
    {
        [NotNull] private readonly TradeAssistantData data;
        public TradeAssistantPlugin()
        {
            data = StorageManager.LoadOrCreate<TradeAssistantData>("TradeAssistant");
        }
        public string GetCategory() => Localizer.DoStr("Mods");
        public string GetStatus() => string.Empty;

        public void SaveAll()
        {
            StorageManager.Obj.MarkDirty(data);
        }
    }
}

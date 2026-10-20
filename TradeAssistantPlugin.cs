using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace TradeAssistant
{
    internal class TradeAssistantPlugin : Singleton<TradeAssistantPlugin>, IModKitPlugin, ISaveablePlugin, IThreadedPlugin
    {
        [NotNull] private readonly TradeAssistantData data;
        public TradeAssistantPlugin()
        {
            // 1.2.2: single storage name constant shared by the normal and recovery paths.
            const string storageName = "TradeAssistant";
            try
            {
                data = StorageManager.LoadOrCreate<TradeAssistantData>(storageName);
            }
            catch
            {
                // 1.2.2: back up corrupt storage files instead of deleting them, and do not switch
                // to the misspelled name "TradeAsisstant" on recovery (old code deleted every
                // TradeAssistant file, then re-created the data under the wrong name).
                foreach (var fileName in StorageManager.GetFiles(storageName))
                {
                    try
                    {
                        if (File.Exists(fileName))
                            File.Move(fileName, fileName + ".corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
                    }
                    catch { }
                }

                data = StorageManager.LoadOrCreate<TradeAssistantData>(storageName);
            }
        }
        public string GetCategory() => Localizer.DoStr("Mods");
        public string GetStatus() => string.Empty;

        public void SaveAll()
        {
            StorageManager.Obj.MarkDirty(data);
        }

        /// <summary>Server tick hook used to drive the optional automatic re-pricing loop.</summary>
        public void Run()
        {
            TradeAssistantAutoRepricer.Tick();
        }

        public Task ShutdownAsync() => Task.CompletedTask;
    }
}

using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace PriceWatcher;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<WatchEntry> WatchList { get; set; } = new();

    public int PollIntervalMinutes { get; set; } = 10;

    public bool AlertInChat { get; set; } = true;
    public bool AlertToast { get; set; } = true;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

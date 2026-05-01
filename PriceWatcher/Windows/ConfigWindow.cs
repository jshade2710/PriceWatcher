using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PriceWatcher.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _config;

    public ConfigWindow(Plugin plugin) : base("Price Watcher Settings###PriceWatcherConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(320, 160);
        SizeCondition = ImGuiCond.Always;
        _config = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var interval = _config.PollIntervalMinutes;
        ImGui.SetNextItemWidth(80f);
        if (ImGui.InputInt("Poll interval (minutes)", ref interval))
        {
            _config.PollIntervalMinutes = Math.Max(1, interval);
            _config.Save();
        }

        var chat = _config.AlertInChat;
        if (ImGui.Checkbox("Alert in chat", ref chat))
        {
            _config.AlertInChat = chat;
            _config.Save();
        }

        var toast = _config.AlertToast;
        if (ImGui.Checkbox("Alert via toast notification", ref toast))
        {
            _config.AlertToast = toast;
            _config.Save();
        }
    }
}

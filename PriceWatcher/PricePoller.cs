using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace PriceWatcher;

public class PricePoller : IDisposable
{
    private readonly Configuration _config;
    private readonly UniversalisClient _client;
    private CancellationTokenSource _cts = new();
    private Task? _task;

    public PricePoller(Configuration config)
    {
        _config = config;
        _client = new UniversalisClient();
        Start();
    }

    private void Start()
    {
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => RunLoop(_cts.Token));
    }

    private async Task RunLoop(CancellationToken ct)
    {
        // Stagger startup so we don't hit the API immediately on load
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            await PollAll(ct);

            var interval = TimeSpan.FromMinutes(Math.Max(1, _config.PollIntervalMinutes));
            await Task.Delay(interval, ct).ContinueWith(_ => { }); // swallow cancellation
        }
    }

    public async Task PollAll(CancellationToken ct = default)
    {
        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded) return;

        var world = playerState.HomeWorld.ValueNullable?.Name.ToString()
                    ?? playerState.CurrentWorld.ValueNullable?.Name.ToString();

        if (string.IsNullOrEmpty(world)) return;

        foreach (var entry in _config.WatchList)
        {
            if (!entry.IsEnabled) continue;
            if (ct.IsCancellationRequested) break;

            var checkWorld = string.IsNullOrEmpty(entry.World) ? world : entry.World;
            var data = await _client.GetPriceDataAsync(checkWorld, entry.ItemId);

            entry.LastChecked = DateTime.Now;

            if (data != null)
            {
                entry.LastSeenPrice = data.BestPrice;
                entry.LastSeenWorld = data.BestWorld;
                entry.TopListings   = data.TopListings;

                bool triggered = entry.AlertWhenBelow
                    ? data.BestPrice <= entry.TargetPrice
                    : data.BestPrice >= entry.TargetPrice;

                if (triggered && !entry.LastAlertFired)
                {
                    entry.LastAlertFired = true;
                    FireAlert(entry, data);
                }
                else if (!triggered)
                {
                    entry.LastAlertFired = false;
                }
            }

            // Be polite to Universalis — small delay between each item
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ContinueWith(_ => { });
        }

        _config.Save();
    }

    private void FireAlert(WatchEntry entry, PriceData data)
    {
        var dir    = entry.AlertWhenBelow ? "dropped to" : "risen to";
        var cond   = entry.AlertWhenBelow ? "<=" : ">=";
        var scope  = string.IsNullOrEmpty(entry.World)
            ? Plugin.PlayerState.HomeWorld.ValueNullable?.Name.ToString() ?? "home"
            : entry.World;

        if (_config.AlertInChat)
        {
            // Header line — gold
            Print(new SeStringBuilder()
                .AddUiForeground(060)
                .AddText($"[Price Watcher] {entry.ItemName} {dir} {data.BestPrice:N0}g")
                .AddText($" on {data.BestWorld}")
                .AddText($"  (target: {cond} {entry.TargetPrice:N0}g, scope: {scope})")
                .AddUiForegroundOff()
                .Build());

            // Top listings
            for (int i = 0; i < data.TopListings.Length; i++)
            {
                var l = data.TopListings[i];
                var sb = new SeStringBuilder()
                    .AddUiForeground(003)
                    .AddText($"  {i + 1,2}. ")
                    .AddUiForegroundOff()
                    .AddText($"{l.Price:N0}g")
                    .AddUiForeground(003)
                    .AddText($"  x{l.Quantity,-4}")
                    .AddUiForegroundOff();

                if (!string.IsNullOrEmpty(l.World))
                    sb.AddUiForeground(037)
                      .AddText($"  [{l.World}]")
                      .AddUiForegroundOff();

                if (l.IsHq)
                    sb.AddUiForeground(062)
                      .AddText("  HQ")
                      .AddUiForegroundOff();

                Print(sb.Build());
            }
        }

        if (_config.AlertToast)
            Plugin.ToastGui.ShowNormal(
                $"[Price Watcher] {entry.ItemName}: {data.BestPrice:N0}g on {data.BestWorld}");
    }

    private static void Print(SeString msg) =>
        Plugin.ChatGui.Print(new XivChatEntry { Message = msg, Type = XivChatType.Echo });

    public void Restart()
    {
        _cts.Cancel();
        _task?.Wait(1000);
        Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _task?.Wait(2000);
        _client.Dispose();
    }
}

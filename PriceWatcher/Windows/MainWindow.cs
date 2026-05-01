using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace PriceWatcher.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private Configuration Config => _plugin.Configuration;

    private static readonly (string Label, string Scope)[] AllRegions =
    [
        ("All NA",  "North-America"),
        ("All EU",  "Europe"),
        ("All JP",  "Japan"),
        ("All OCE", "Oceania"),
    ];

    private static readonly (string Region, string[] DCs)[] KnownDCs =
    [
        ("NA",  ["Aether", "Crystal", "Primal", "Dynamis"]),
        ("EU",  ["Chaos", "Light"]),
        ("JP",  ["Elemental", "Gale", "Mana", "Meteor"]),
        ("OCE", ["Materia"]),
    ];

    private static readonly Vector4 ColAlert   = new(0.22f, 0.90f, 0.22f, 1f);
    private static readonly Vector4 ColMonitor = new(0.40f, 0.65f, 1.00f, 1f);
    private static readonly Vector4 ColMuted   = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 ColInact   = new(0.40f, 0.40f, 0.40f, 1f);

    private string _searchText       = string.Empty;
    private Item[] _searchResults    = Array.Empty<Item>();
    private uint   _selectedItemId;
    private string _selectedItemName = string.Empty;
    private long   _targetPrice;
    private bool   _alertBelow       = true;
    private string _worldOverride    = string.Empty;
    private string _targetPriceText  = string.Empty;

    public MainWindow(Plugin plugin)
        : base("Price Watcher##main", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720, 440),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        _plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawToolbar();
        ImGui.Spacing();
        DrawWatchList();
        ImGui.Spacing();
        DrawAddPanel();
    }

    // ── Toolbar ────────────────────────────────────────────────────────
    private void DrawToolbar()
    {
        var count  = Config.WatchList.Count;
        var alerts = Config.WatchList.Count(e => e.LastAlertFired);

        ImGui.TextColored(ColMuted, $"{count} item{(count != 1 ? "s" : "")} watched");
        if (alerts > 0)
        {
            ImGui.SameLine(0, 8f);
            ImGui.TextColored(ColAlert, $"• {alerts} alert{(alerts != 1 ? "s" : "")} active");
        }

        // Right-align the two action buttons
        const float w1 = 74f, w2 = 64f, gap = 6f, pad = 16f;
        ImGui.SameLine(ImGui.GetWindowWidth() - w1 - w2 - gap - pad);
        if (ImGui.Button("Poll Now", new Vector2(w1, 0)))
            _ = _plugin.Poller.PollAll();
        ImGui.SameLine(0, gap);
        if (ImGui.Button("Settings", new Vector2(w2, 0)))
            _plugin.ToggleConfigUi();
    }

    // ── Watchlist table ────────────────────────────────────────────────
    private void DrawWatchList()
    {
        var tableH = Math.Max(80f, ImGui.GetContentRegionAvail().Y - 168f);
        const ImGuiTableFlags Flags =
            ImGuiTableFlags.Borders      |
            ImGuiTableFlags.RowBg        |
            ImGuiTableFlags.ScrollY      |
            ImGuiTableFlags.SizingFixedFit;

        using var table = ImRaii.Table("watchlist", 8, Flags, new Vector2(-1f, tableH));
        if (!table.Success) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##s",       ImGuiTableColumnFlags.WidthFixed,   14f);
        ImGui.TableSetupColumn("Item",      ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Scope",     ImGuiTableColumnFlags.WidthFixed,   86f);
        ImGui.TableSetupColumn("Alert",     ImGuiTableColumnFlags.WidthFixed,   80f);
        ImGui.TableSetupColumn("Best Price",ImGuiTableColumnFlags.WidthFixed,   84f);
        ImGui.TableSetupColumn("Found On",  ImGuiTableColumnFlags.WidthFixed,   78f);
        ImGui.TableSetupColumn("Checked",   ImGuiTableColumnFlags.WidthFixed,   58f);
        ImGui.TableSetupColumn("##x",       ImGuiTableColumnFlags.WidthFixed,   22f);
        ImGui.TableHeadersRow();

        if (Config.WatchList.Count == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TextDisabled("Watchlist is empty — add an item below.");
            return;
        }

        WatchEntry? toRemove = null;
        foreach (var entry in Config.WatchList)
        {
            ImGui.TableNextRow();

            // Status dot
            ImGui.TableNextColumn();
            DrawStatusDot(entry);

            // Name + enable checkbox
            ImGui.TableNextColumn();
            if (entry.LastAlertFired)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                    ImGui.GetColorU32(new Vector4(0.10f, 0.38f, 0.10f, 0.32f)));

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2f, 2f));
            var enabled = entry.IsEnabled;
            if (ImGui.Checkbox($"##{entry.Id}", ref enabled))
            {
                entry.IsEnabled = enabled;
                Config.Save();
            }
            ImGui.PopStyleVar();
            ImGui.SameLine(0, 5f);
            if (!entry.IsEnabled) ImGui.PushStyleColor(ImGuiCol.Text, ColInact);
            ImGui.TextUnformatted(entry.ItemName);
            if (!entry.IsEnabled) ImGui.PopStyleColor();

            // Scope
            ImGui.TableNextColumn();
            ImGui.TextColored(ColMuted, string.IsNullOrEmpty(entry.World) ? "home" : entry.World);

            // Alert condition
            ImGui.TableNextColumn();
            var dir = entry.AlertWhenBelow ? "<=" : ">=";
            ImGui.TextUnformatted($"{dir} {entry.TargetPrice:N0}g");

            // Best price
            ImGui.TableNextColumn();
            if (entry.LastSeenPrice.HasValue)
            {
                ImGui.TextColored(
                    entry.LastAlertFired ? ColAlert : new Vector4(1f, 1f, 1f, 1f),
                    $"{entry.LastSeenPrice.Value:N0}");

                if (ImGui.IsItemHovered())
                    DrawListingsTooltip(entry);
            }
            else
            {
                ImGui.TextDisabled("—");
            }

            // Found on
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.LastSeenWorld ?? "—");

            // Last checked
            ImGui.TableNextColumn();
            ImGui.TextColored(ColMuted, entry.LastChecked.HasValue
                ? entry.LastChecked.Value.ToString("HH:mm")
                : "—");

            // Remove
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.48f, 0.10f, 0.10f, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.68f, 0.14f, 0.14f, 0.90f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.80f, 0.18f, 0.18f, 1.00f));
            if (ImGui.SmallButton($"x##{entry.Id}")) toRemove = entry;
            ImGui.PopStyleColor(3);
        }

        if (toRemove != null)
        {
            Config.WatchList.Remove(toRemove);
            Config.Save();
        }
    }

    private static void DrawListingsTooltip(WatchEntry entry)
    {
        using var _ = ImRaii.Tooltip();

        ImGui.TextColored(ColMuted, $"Target: {(entry.AlertWhenBelow ? "<=" : ">=")} {entry.TargetPrice:N0}g");

        var listings = entry.TopListings;
        if (listings == null || listings.Length == 0)
        {
            ImGui.TextDisabled("No listing data yet.");
            return;
        }

        ImGui.Spacing();
        ImGui.TextColored(ColMuted, $"Top {listings.Length} listings:");
        ImGui.Separator();
        ImGui.Spacing();

        for (int i = 0; i < listings.Length; i++)
        {
            var l = listings[i];
            ImGui.TextColored(ColMuted, $"{i + 1,2}.");
            ImGui.SameLine(0, 6f);
            ImGui.Text($"{l.Price:N0}g");
            ImGui.SameLine(0, 8f);
            ImGui.TextColored(ColMuted, $"×{l.Quantity}");
            if (!string.IsNullOrEmpty(l.World))
            {
                ImGui.SameLine(0, 8f);
                ImGui.TextColored(ColMonitor, $"[{l.World}]");
            }
            if (l.IsHq)
            {
                ImGui.SameLine(0, 6f);
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.15f, 1f), "HQ");
            }
        }
    }

    private static void DrawStatusDot(WatchEntry entry)
    {
        var dl  = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        var col = !entry.IsEnabled    ? ColInact
                : entry.LastAlertFired ? ColAlert
                : entry.LastChecked.HasValue ? ColMonitor
                : ColInact;

        dl.AddCircleFilled(new Vector2(pos.X + 5f, pos.Y + 8f), 4f, ImGui.GetColorU32(col));
        ImGui.Dummy(new Vector2(12f, 14f));

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(!entry.IsEnabled          ? "Disabled"
                : entry.LastAlertFired                 ? "Alert active — threshold met!"
                : entry.LastChecked.HasValue           ? "Monitoring"
                :                                        "Pending first check");
    }

    // ── Add item panel ─────────────────────────────────────────────────
    private void DrawAddPanel()
    {
        using var child = ImRaii.Child("addpanel", new Vector2(-1f, 160f), true);
        if (!child.Success) return;

        ImGui.TextColored(ColMuted, "ADD ITEM");
        ImGui.Spacing();

        // Row 1: item search + picker
        ImGui.SetNextItemWidth(185f);
        if (ImGui.InputTextWithHint("##search", "Search item name...", ref _searchText, 64))
            RunSearch();

        ImGui.SameLine(0, 6f);
        ImGui.SetNextItemWidth(270f);
        var preview = _selectedItemName.Length > 0 ? _selectedItemName : "Select item...";
        using (var combo = ImRaii.Combo("##results", preview))
        {
            if (combo.Success)
            {
                if (_searchResults.Length == 0)
                    ImGui.TextDisabled(_searchText.Length < 2
                        ? "Type 2+ characters to search"
                        : "No tradeable items found");
                else
                    foreach (var item in _searchResults)
                    {
                        var name = item.Name.ToString();
                        if (ImGui.Selectable(name, _selectedItemId == item.RowId))
                        {
                            _selectedItemId   = item.RowId;
                            _selectedItemName = name;
                        }
                    }
            }
        }

        ImGui.Spacing();

        // Row 2: scope
        ImGui.TextColored(ColMuted, "Scope ");
        ImGui.SameLine(0, 0f);
        ImGui.SetNextItemWidth(148f);
        ImGui.InputTextWithHint("##scope", "World / DC / Region", ref _worldOverride, 32);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Blank  →  your home world\n" +
                "World  →  Cactuar, Balmung, ...\n" +
                "DC     →  Aether, Crystal, Chaos, ...\n" +
                "Region →  North-America, Europe, Japan, Oceania");

        ImGui.SameLine(0, 5f);
        if (ImGui.SmallButton("Pick scope..."))
            ImGui.OpenPopup("scope_picker");

        DrawScopePicker();

        ImGui.Spacing();

        // Row 3: alert condition + add button
        ImGui.TextColored(ColMuted, "Alert  ");
        ImGui.SameLine(0, 0f);
        ImGui.SetNextItemWidth(85f);
        var dirIdx = _alertBelow ? 0 : 1;
        if (ImGui.Combo("##dir", ref dirIdx, "drops to\0rises to\0"))
            _alertBelow = dirIdx == 0;
        ImGui.SameLine(0, 5f);
        ImGui.SetNextItemWidth(115f);
        if (ImGui.InputTextWithHint("##price", "gil amount", ref _targetPriceText, 16))
            long.TryParse(_targetPriceText.Replace(",", ""), out _targetPrice);

        ImGui.SameLine(0, 14f);
        var canAdd = _selectedItemId > 0 && _targetPrice > 0;

        // Green when ready, dim when not
        var btnCol   = canAdd ? new Vector4(0.18f, 0.52f, 0.22f, 1.00f) : new Vector4(0.28f, 0.28f, 0.28f, 0.70f);
        var btnHover = canAdd ? new Vector4(0.22f, 0.62f, 0.27f, 1.00f) : new Vector4(0.32f, 0.32f, 0.32f, 0.85f);
        var btnActiv = canAdd ? new Vector4(0.14f, 0.42f, 0.18f, 1.00f) : new Vector4(0.22f, 0.22f, 0.22f, 1.00f);
        ImGui.PushStyleColor(ImGuiCol.Button,        btnCol);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, btnHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  btnActiv);
        if (!canAdd) ImGui.BeginDisabled();
        if (ImGui.Button("+ Add to Watchlist"))
        {
            Config.WatchList.Add(new WatchEntry
            {
                ItemId        = _selectedItemId,
                ItemName      = _selectedItemName,
                TargetPrice   = _targetPrice,
                AlertWhenBelow= _alertBelow,
                World         = _worldOverride.Trim(),
            });
            Config.Save();
            _searchText = _selectedItemName = _worldOverride = _targetPriceText = string.Empty;
            _searchResults  = Array.Empty<Item>();
            _selectedItemId = 0;
            _targetPrice    = 0;
        }
        if (!canAdd) ImGui.EndDisabled();
        ImGui.PopStyleColor(3);
    }

    private void DrawScopePicker()
    {
        using var popup = ImRaii.Popup("scope_picker");
        if (!popup.Success) return;

        // ── Region (all worlds in region) ──────────────────────────
        ImGui.TextColored(ColMuted, "ENTIRE REGION");
        ImGui.Spacing();
        foreach (var (label, scope) in AllRegions)
        {
            ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.18f, 0.33f, 0.55f, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.44f, 0.72f, 1.00f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.14f, 0.26f, 0.44f, 1.00f));
            if (ImGui.Button(label, new Vector2(74f, 0f)))
            {
                _worldOverride = scope;
                ImGui.CloseCurrentPopup();
            }
            ImGui.PopStyleColor(3);
            ImGui.SameLine(0, 5f);
        }
        ImGui.NewLine();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Individual data centers ────────────────────────────────
        ImGui.TextColored(ColMuted, "DATA CENTER");
        ImGui.Spacing();
        foreach (var (region, dcs) in KnownDCs)
        {
            ImGui.TextColored(ColMuted, region);
            ImGui.SameLine(38f);
            foreach (var dc in dcs)
            {
                if (ImGui.SmallButton(dc))
                {
                    _worldOverride = dc;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine(0, 4f);
            }
            ImGui.NewLine();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Home world ─────────────────────────────────────────────
        ImGui.TextColored(ColMuted, "HOME WORLD");
        ImGui.SameLine(0, 6f);
        if (ImGui.SmallButton("Use home world"))
        {
            _worldOverride = string.Empty;
            ImGui.CloseCurrentPopup();
        }
    }

    private void RunSearch()
    {
        if (_searchText.Length < 2)
        {
            _searchResults = Array.Empty<Item>();
            return;
        }
        var sheet = Plugin.DataManager.GetExcelSheet<Item>();
        if (sheet == null) return;

        var query = _searchText.ToLowerInvariant();

        // Score each match once, then group by tier so every tier gets
        // representation regardless of how many items are in tier 0/1.
        // Without this, 25+ "Wyvernskin X" items (tier 1) would fill the
        // list before "Timeworn Wyvernskin" (tier 2) ever appears.
        _searchResults = sheet
            .Where(i => !i.IsUntradable && i.Name.ToString().ToLowerInvariant().Contains(query))
            .Select(i => (item: i, name: i.Name.ToString(),
                          score: RelevanceScore(i.Name.ToString().ToLowerInvariant(), query)))
            .GroupBy(x => x.score)
            .OrderBy(g => g.Key)
            .SelectMany(g => g.OrderBy(x => x.name))
            .Take(50)
            .Select(x => x.item)
            .ToArray();
    }

    // 0 = exact · 1 = starts with · 2 = word starts with · 3 = contains
    private static int RelevanceScore(string nameLower, string query)
    {
        if (nameLower == query) return 0;
        if (nameLower.StartsWith(query)) return 1;
        foreach (var word in nameLower.Split(' '))
            if (word.StartsWith(query)) return 2;
        return 3;
    }
}

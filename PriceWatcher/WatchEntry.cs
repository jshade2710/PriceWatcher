using System;
using Newtonsoft.Json;

namespace PriceWatcher;

[Serializable]
public class WatchEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;

    // The world to check — empty means use the player's home world
    public string World { get; set; } = string.Empty;

    public long TargetPrice { get; set; }

    // true = alert when price DROPS to or below target; false = alert when it RISES to or above
    public bool AlertWhenBelow { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    // Last known lowest listing price, filled in after each poll
    public long? LastSeenPrice { get; set; }
    public string? LastSeenWorld { get; set; }
    public DateTime? LastChecked { get; set; }
    public bool LastAlertFired { get; set; }

    // Top listings from last poll — transient, never saved to disk
    [JsonIgnore]
    public ListingInfo[]? TopListings { get; set; }
}

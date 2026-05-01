using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PriceWatcher;

public record ListingInfo(long Price, int Quantity, string World, bool IsHq);
public record PriceData(long BestPrice, string BestWorld, ListingInfo[] TopListings);

public class UniversalisClient : IDisposable
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://universalis.app/"),
        Timeout = TimeSpan.FromSeconds(15),
    };

    // Returns the cheapest listing + top 10 sorted listings across the given world/DC/region.
    // worldOrDc can be a world name, data center (e.g. "Aether"), or region (e.g. "North-America").
    public async Task<PriceData?> GetPriceDataAsync(string worldOrDc, uint itemId)
    {
        try
        {
            var response = await _http.GetAsync(
                $"api/v2/{Uri.EscapeDataString(worldOrDc)}/{itemId}?listings=50&entries=0");
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<UniversalisResponse>(json);
            if (result?.Listings == null || result.Listings.Length == 0) return null;

            var sorted = result.Listings
                .OrderBy(l => l.PricePerUnit)
                .ToArray();

            var best     = sorted[0];
            var foundOn  = string.IsNullOrEmpty(best.WorldName) ? worldOrDc : best.WorldName;

            var topListings = sorted
                .Take(10)
                .Select(l => new ListingInfo(
                    l.PricePerUnit,
                    l.Quantity,
                    string.IsNullOrEmpty(l.WorldName) ? worldOrDc : l.WorldName,
                    l.IsHq))
                .ToArray();

            return new PriceData(best.PricePerUnit, foundOn, topListings);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    private class UniversalisResponse
    {
        [JsonPropertyName("listings")]
        public Listing[]? Listings { get; set; }
    }

    private class Listing
    {
        [JsonPropertyName("pricePerUnit")]
        public long PricePerUnit { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("worldName")]
        public string WorldName { get; set; } = string.Empty;

        [JsonPropertyName("hq")]
        public bool IsHq { get; set; }
    }
}

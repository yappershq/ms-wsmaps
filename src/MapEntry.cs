using System.Text.Json.Serialization;

namespace YappersHQ.WSMaps;

internal sealed class MapEntry
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ulong WorkshopId { get; set; }

    public string? MapName { get; set; }

    [JsonIgnore]
    public bool IsStockMap => WorkshopId == 0;
}

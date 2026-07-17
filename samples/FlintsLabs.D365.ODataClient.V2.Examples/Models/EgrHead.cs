using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Attributes;

namespace FlintsLabs.D365.ODataClient.V2.Examples.Models;

public sealed class EgrHead
{
    [OdataKey]
    [JsonPropertyName("rvl_egrheadid")]
    public Guid Id { get; set; }

    [JsonPropertyName("rvl_name")]
    public string? Name { get; set; }

    [JsonPropertyName("rvl_wmsstatus")]
    public bool? WmsStatus { get; set; }

    [JsonPropertyName("dataAreaId")]
    public string? Company { get; set; }
}

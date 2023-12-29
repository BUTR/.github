using System.Text.Json.Serialization;

namespace DotNetTools.Models;

public class NewsForApp
{
    [JsonPropertyName("appnews")]
    public required AppNews AppNews { get; init; }
}
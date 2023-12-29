using System.Text.Json.Serialization;

namespace DotNetTools.Models;

public class AppNews
{
    [JsonPropertyName("appid")]
    public required long AppId { get; init; }

    [JsonPropertyName("newsitems")]
    public required NewsItem[] NewsItems { get; init; }

    [JsonPropertyName("count")]
    public required long Count { get; init; }
}
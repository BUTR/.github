using System;
using System.Text.Json.Serialization;

namespace DotNetTools.Models
{
    public class NewsItem
    {
        [JsonPropertyName("gid")]
        public required string GId { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("url")]
        public required Uri Url { get; init; }

        [JsonPropertyName("is_external_url")]
        public required bool IsExternalUrl { get; init; }

        [JsonPropertyName("author")]
        public required string Author { get; init; }

        [JsonPropertyName("contents")]
        public required string Contents { get; init; }

        [JsonPropertyName("feedlabel")]
        public required string FeedLabel { get; init; }

        [JsonPropertyName("date")]
        public required long Date { get; init; }

        [JsonPropertyName("feedname")]
        public required string FeedName { get; init; }

        [JsonPropertyName("feed_type")]
        public required long FeedType { get; init; }

        [JsonPropertyName("appid")]
        public required long Appid { get; init; }

        [JsonPropertyName("tags")]
        public required string[]? Tags { get; init; }
    }
}
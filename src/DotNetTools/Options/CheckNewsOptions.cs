namespace DotNetTools.Options;

public sealed class CheckNewsOptions
{
    public required string AppId { get; init; }
    public required int Count { get; init; } = 10;
}
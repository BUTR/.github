using System.Collections.Generic;

namespace DotNetTools.Options;

public sealed class SteamOptions
{
    public required string SteamLogin { get; init; }
    public required string SteamPassword { get; init; }
    public required int SteamAppId { get; init; }
    public required string SteamOS { get; init; }
    public required string SteamOSArch { get; init; }
    public required List<int> SteamDepotId { get; init; }
}
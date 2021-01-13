using HarmonyLib;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using SteamKit2;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DotNetTools
{
    public class NewsForApp
    {
        [JsonPropertyName("appnews")]
        public AppNews AppNews { get; set; }
    }
    public class AppNews
    {
        [JsonPropertyName("appid")]
        public long AppId { get; set; }

        [JsonPropertyName("newsitems")]
        public NewsItem[] NewsItems { get; set; }

        [JsonPropertyName("count")]
        public long Count { get; set; }
    }
    public class NewsItem
    {
        [JsonPropertyName("gid")]
        public string GId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("url")]
        public Uri Url { get; set; }

        [JsonPropertyName("is_external_url")]
        public bool IsExternalUrl { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("contents")]
        public string Contents { get; set; }

        [JsonPropertyName("feedlabel")]
        public string FeedLabel { get; set; }

        [JsonPropertyName("date")]
        public long Date { get; set; }

        [JsonPropertyName("feedname")]
        public string FeedName { get; set; }

        [JsonPropertyName("feed_type")]
        public long FeedType { get; set; }

        [JsonPropertyName("appid")]
        public long Appid { get; set; }

        [JsonPropertyName("tags")]
        public string[]? Tags { get; set; }
    }

    public class CheckNewsOptions
    {
        public string AppId { get; set; } = default!;
        public int Count { get; set; } = 10;
    }
    public class SecretsOptions
    {
        public long DateOfLastPost { get; set; } = default!;
    }
    public class SteamOptions
    {
        public string SteamLogin { get; set; } = default!;
        public string SteamPassword { get; set; } = default!;
        public uint SteamAppId { get; set; } = default!;
        public uint SteamDepotId { get; set; } = default!;
    }

    public enum BranchType
    {
        Unknown = 'i',
        Alpha = 'a',
        Beta = 'b',
        EarlyAccess = 'e',
        Release = 'v',
        Development = 'd'
    }
    public struct SteamAppBranch
    {
        public static readonly IReadOnlyDictionary<BranchType, string?> VersionPrefixToName = new SortedList<BranchType, string?>
        {
            { BranchType.Alpha, "Alpha" },
            { BranchType.Beta, "Beta" },
            { BranchType.EarlyAccess, "EarlyAccess" },
            { BranchType.Development, "Development" },
            { BranchType.Release, null },
            { BranchType.Unknown, "Invalid" },
        };

        public BranchType Prefix
        {
            get
            {
                if (string.IsNullOrEmpty(Name) || Name.Length < 1 || !char.IsDigit(Name[1]) || !Enum.IsDefined(typeof(BranchType), (int) Name[0]))
                    return BranchType.Unknown;
                return (BranchType) Name[0];
            }
        }

        public string Name { get; init; }
        public uint AppId { get; init; }
        public uint DepotId { get; init; }
        public uint BuildId { get; init; }

        public string GetVersion(string appVersion) =>
            //char.IsDigit(Name[1]) ? $"{Name[1..]}.{appVersion}-{Name[0]}" : "";
            char.IsDigit(Name[1]) ? $"{Name[1..]}.{appVersion}" : "";

        public override string ToString() => $"{Name} ({AppId} {DepotId} {BuildId})";
    }

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            await BuildCommandLine()
                .UseHost(_ => CreateHostBuilder(args))
                .UseDefaults()
                .Build()
                .InvokeAsync(args);
        }

        private static CommandLineBuilder BuildCommandLine()
        {
            var checkNews = new Command("check-news")
            {
                new Option<string>("--appId"),
                new Option<string>("--count")
            };
            checkNews.Handler = CommandHandler.Create(async (IHost host) =>
            {
                var @out = Console.Out;
                Console.SetOut(TextWriter.Null);

                var httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
                var client = httpClientFactory.CreateClient();

                var options = host.Services.GetRequiredService<IOptions<CheckNewsOptions>>().Value;

                var url = $"http://api.steampowered.com/ISteamNews/GetNewsForApp/v0002/?appid={options.AppId}&count={options.Count}&maxlength=1&format=json";
                var response =  await client.GetFromJsonAsync<NewsForApp>(url);

                var secrets = host.Services.GetRequiredService<IOptions<SecretsOptions>>().Value;
                var dateOfLastPost = secrets.DateOfLastPost;

                var lastPatchNotes = response?.AppNews.NewsItems.FirstOrDefault(n => n.Tags?.Any(t => t == "patchnotes" || t == "mod_reviewed" || t == "mod_require_rereview") == true);
                Console.SetOut(@out);
                if (lastPatchNotes is null || lastPatchNotes.Date == dateOfLastPost)
                    Console.WriteLine(0);
                else
                    Console.WriteLine(lastPatchNotes.Date);
            });

            var getBranches = new Command("get-branches")
            {
                new Option<string>("--steamLogin"),
                new Option<string>("--steamPassword"),
                new Option<string>("--steamAppId"),
                new Option<string>("--steamDepotId")
            };
            getBranches.Handler = CommandHandler.Create((IHost host) =>
            {
                var @out = Console.Out;
                Console.SetOut(TextWriter.Null);

                var steamOptions = host.Services.GetRequiredService<IOptions<SteamOptions>>().Value;

                var programType = typeof(DepotDownloader.ContentDownloaderException).Assembly.GetType("DepotDownloader.Program");
                var accountSettingsStoreType = typeof(DepotDownloader.ContentDownloaderException).Assembly.GetType("DepotDownloader.AccountSettingsStore");
                var contentDownloaderType = typeof(DepotDownloader.ContentDownloaderException).Assembly.GetType("DepotDownloader.ContentDownloader");
                var steam3SessionType = typeof(DepotDownloader.ContentDownloaderException).Assembly.GetType("DepotDownloader.Steam3Session");

                var loadFromFileMethod = AccessTools.Method(accountSettingsStoreType, "LoadFromFile");
                var initializeSteamMethod = AccessTools.Method(programType, "InitializeSteam");
                var requestAppInfoMethod = AccessTools.Method(steam3SessionType, "RequestAppInfo");
                var getSteam3AppSectionMethod = AccessTools.Method(contentDownloaderType, "GetSteam3AppSection");
                var shutdownSteam3Method = AccessTools.Method(contentDownloaderType, "ShutdownSteam3");

                var steam3Field = AccessTools.Field(contentDownloaderType, "steam3");

                loadFromFileMethod.Invoke(null, new object?[] { "account.config" });
                initializeSteamMethod.Invoke(null, new object?[] { steamOptions.SteamLogin, steamOptions.SteamPassword });
                var steam3 = steam3Field.GetValue(null);
                requestAppInfoMethod.Invoke(steam3, new object?[] { steamOptions.SteamAppId, false });
                var depots = getSteam3AppSectionMethod.Invoke(null, new object?[] { steamOptions.SteamAppId, EAppInfoSection.Depots }) as KeyValue;
                shutdownSteam3Method.Invoke(null, Array.Empty<object>());

                var branches = depots["branches"].Children.ConvertAll(c => new SteamAppBranch
                {
                    Name = c.Name!,
                    AppId = steamOptions.SteamAppId,
                    DepotId = steamOptions.SteamDepotId,
                    BuildId = uint.TryParse(c["buildid"].Value!, out var r) ? r : 0
                });

                //var prefixes = new HashSet<BranchType>(branches.Select(branch => branch.Prefix).Where(b => b != BranchType.Unknown));

                var publicBranch = branches.First(branch => branch.Name == "public");
                var otherBranches = branches.Where(branch => branch.Prefix != BranchType.Unknown).ToList();

                var stableBranchVersion = otherBranches.Find(branch => branch.BuildId == publicBranch.BuildId);
                var betaBranchVersion = otherBranches.Last();

                Console.SetOut(@out);
                Console.WriteLine($"{{ stable: \"{stableBranchVersion.Name}\", beta: \"{betaBranchVersion.Name}\" }}");

                System.Diagnostics.Process.GetCurrentProcess().Kill();
            });

            var root = new RootCommand { checkNews, getBranches };
            return new CommandLineBuilder(root);
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => new HostBuilder()
            .UseContentRoot(Directory.GetCurrentDirectory())
            .ConfigureHostConfiguration(config =>
            {
                config.AddEnvironmentVariables("DOTNET_");
                config.AddCommandLine(args);
            })
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddEnvironmentVariables("BUTR_SNC_");
                config.AddEnvironmentVariables();
                config.AddCommandLine(args);
            })
            /*
            .ConfigureLogging((context, logging) =>
            {
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                logging.AddConsole();
                logging.AddDebug();
                logging.AddEventSourceLogger();
            })
            */
            .UseDefaultServiceProvider((context, options) =>
            {
                var isDevelopment = context.HostingEnvironment.IsDevelopment();
                options.ValidateScopes = isDevelopment;
                options.ValidateOnBuild = isDevelopment;
            })
            .ConfigureServices((context, services) =>
            {
                services.AddHttpClient();

                services.Configure<SecretsOptions>(context.Configuration);
                services.Configure<CheckNewsOptions>(context.Configuration);
                services.Configure<SteamOptions>(context.Configuration);
            });
    }
}

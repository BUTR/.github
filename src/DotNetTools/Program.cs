using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bannerlord.ModuleManager;
using DotNetTools.Options;

namespace DotNetTools;

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
        var getBranches = new Command("get-latest-version")
        {
            new Option<string>("--steamLogin"),
            new Option<string>("--steamPassword"),
            new Option<int>("--steamAppId"),
            new Option<List<int>>("--steamDepotId"),
            new Option<string>("--steamOS"),
            new Option<string>("--steamOSArch"),
        };
        getBranches.Handler = CommandHandler.Create<SteamOptions, IHost>(async (options, host) =>
        {
            var @out = Console.Out;
            Console.SetOut(TextWriter.Null);

            try
            {
                DepotDownloader.AccountSettingsStore.LoadFromFile("account.config");
                DepotDownloader.ContentDownloader.InitializeSteam3(options.SteamLogin, options.SteamPassword);

                var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName().Replace(".", string.Empty));
                Directory.CreateDirectory(tempFolder);
                DepotDownloader.ContentDownloader.Config.MaxDownloads = 4;
                DepotDownloader.ContentDownloader.Config.InstallDirectory = tempFolder;

                var stableVersion = await GetBranchVersion("public", tempFolder, options, CancellationToken.None);
                var betaVersion = GetBetaBranch(options, CancellationToken.None) is { } betaBranch
                    ? await GetBranchVersion(betaBranch, tempFolder, options, CancellationToken.None)
                    : stableVersion;

                Directory.Delete(tempFolder, true);

                DepotDownloader.ContentDownloader.ShutdownSteam3();

                Console.SetOut(@out);
                Console.WriteLine($"{{ \"stable\": \"{stableVersion}\", \"beta\": \"{betaVersion}\" }}");
            }
            catch (Exception e)
            {
                Console.SetOut(@out);
                Console.WriteLine(e);
            }

            var applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            applicationLifetime.StopApplication();
        });

        var root = new RootCommand { getBranches };
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

            services.Configure<CheckNewsOptions>(context.Configuration);
            services.Configure<SteamOptions>(context.Configuration);
        });

    private static string? GetBetaBranch(SteamOptions options, CancellationToken ct)
    {
        var depots = DepotDownloader.ContentDownloader.GetSteam3AppSection((uint) options.SteamAppId, SteamKit2.EAppInfoSection.Depots);
        var branches = depots["branches"];
        return branches.Children
            .Where(x => x["pwdrequired"].Value != "1" && x["lcsrequired"].Value != "1")
            .Where(x => x["description"].Value == "beta")
            .Select(x => x.Name)
            .FirstOrDefault();
    }

    private static async Task<string?> GetBranchVersion(string branchName, string tempFolder, SteamOptions options, CancellationToken ct)
    {
        DepotDownloader.ContentDownloader.Config.UsingFileList = true;
        DepotDownloader.ContentDownloader.Config.FilesToDownload = [];
        DepotDownloader.ContentDownloader.Config.FilesToDownloadRegex = [];
        
        var filesToDownload = DepotDownloader.ContentDownloader.Config.FilesToDownload;
        filesToDownload.Clear();
        filesToDownload.Add("bin\\Win64_Shipping_Client\\Version.xml");
        filesToDownload.Add("bin/Win64_Shipping_Client/Version.xml");
        var filesToDownloadRegex = DepotDownloader.ContentDownloader.Config.FilesToDownloadRegex;
        filesToDownloadRegex.Clear();

        await DepotDownloader.ContentDownloader.DownloadAppAsync(
            (uint) options.SteamAppId,
            options.SteamDepotId.Select(x => ((uint) x, ulong.MaxValue)).ToList(),
            branchName,
            options.SteamOS,
            options.SteamOSArch,
            null!,
            false,
            false).ConfigureAwait(false);
        var file = Path.Combine(tempFolder, "bin", "Win64_Shipping_Client", "Version.xml");
        var content = await File.ReadAllTextAsync(file, ct);
        var toSearch1 = "<Singleplayer Value=\"";
        var idx1 = content.IndexOf(toSearch1, StringComparison.Ordinal);
        var idx2 = idx1 != -1 ? content.AsSpan(idx1 + toSearch1.Length).IndexOf("\"") : -1;
        if (idx1 != -1 && idx2 != -1)
        {
            var version = content.AsSpan(idx1 + toSearch1.Length, idx2).ToString();
            return ApplicationVersion.TryParse(version, out var appVersion) ? ToString(appVersion) : null;
        }
        return null;

    }

    private static string ToString(ApplicationVersion av) =>
        $"{ApplicationVersion.GetPrefix(av.ApplicationVersionType)}{av.Major}.{av.Minor}.{av.Revision}";
}
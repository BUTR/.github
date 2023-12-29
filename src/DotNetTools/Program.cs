﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bannerlord.ModuleManager;
using DotNetTools.Models;
using DotNetTools.Options;
using Octokit;

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
        var checkNews = new Command("check-news")
        {
            new Option<string>("--appId"),
            new Option<string>("--count")
        };
        checkNews.SetHandler(async context =>
        {
            var host = context.GetHost();

            var @out = Console.Out;
            Console.SetOut(TextWriter.Null);

            try
            {
                var github = new GitHubClient(new ProductHeaderValue("BUTR"))
                {
                    Credentials = new Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                };
                    
                var httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
                var client = httpClientFactory.CreateClient();

                var options = host.Services.GetRequiredService<IOptions<CheckNewsOptions>>().Value;

                var url = $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v0002/?appid={options.AppId}&count={options.Count}&maxlength=1&format=json";
                var response =  await client.GetFromJsonAsync<NewsForApp>(url);

                long dateOfLastPost;
                try
                {
                    var dateOfLastPostVariable = await github.Repository.Actions.Variables.Get("BUTR", ".github", "SC_DATE_OF_LAST_POST");
                    dateOfLastPost = long.TryParse(dateOfLastPostVariable.Value, out var val) ? val : 0;
                }
                catch (InvalidOperationException)
                {
                    dateOfLastPost = 0;
                }

                var lastPatchNotes = response?.AppNews.NewsItems.FirstOrDefault(n => n.Tags?.Any(t => t is "patchnotes" or "mod_reviewed" or "mod_require_rereview") == true);
                await github.Repository.Actions.Variables.Update("BUTR", ".github", "SC_DATE_OF_LAST_POST", new UpdateRepositoryVariable { Value = lastPatchNotes?.Date.ToString() ?? "0"});
                    
                Console.SetOut(@out);
                if (lastPatchNotes is null || lastPatchNotes.Date == dateOfLastPost)
                    Console.WriteLine(0);
                else
                    Console.WriteLine(lastPatchNotes.Date);
            }
            catch (Exception e)
            {
                Console.SetOut(@out);
                Console.WriteLine(e);
            }

            var applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            applicationLifetime.StopApplication();
        });


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
                DepotDownloaderExt.Init();
                DepotDownloaderExt.AccountSettingsStoreLoadFromFile("account.config");
                DepotDownloaderExt.DepotDownloaderProgramInitializeSteam(options.SteamLogin, options.SteamPassword);
                DepotDownloaderExt.ContentDownloadersteam3RequestAppInfo((uint) options.SteamAppId);

                var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName().Replace(".", string.Empty));
                Directory.CreateDirectory(tempFolder);
                DepotDownloaderExt.ContentDownloaderConfigSetMaxDownloads(4);
                DepotDownloaderExt.ContentDownloaderConfigSetInstallDirectory(tempFolder);

                var stableVersion = await GetBranchVersion("public", tempFolder, options, CancellationToken.None);
                var betaVersion = GetBetaBranch(options, CancellationToken.None) is { } betaBranch
                    ? await GetBranchVersion(betaBranch, tempFolder, options, CancellationToken.None)
                    : null;

                Directory.Delete(tempFolder, true);

                DepotDownloaderExt.ContentDownloaderShutdownSteam3();

                Console.SetOut(@out);
                Console.WriteLine(betaVersion is null ? $"{{ stable: \"{stableVersion}\" }}" : $"{{ stable: \"{stableVersion}\", beta: \"{betaVersion}\" }}");

                if (true)
                {
                    var github = new GitHubClient(new ProductHeaderValue("BUTR"))
                    {
                        Credentials = new Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                    };
                    await github.Organization.Actions.Variables.Update("BUTR", "GAME_VERSION_STABLE", new UpdateOrganizationVariable(stableVersion, "all", Array.Empty<long>()));
                    await github.Organization.Actions.Variables.Update("BUTR", "GAME_VERSION_BETA", new UpdateOrganizationVariable(betaVersion, "all", Array.Empty<long>()));
                }
            }
            catch (Exception e)
            {
                Console.SetOut(@out);
                Console.WriteLine(e);
            }

            var applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            applicationLifetime.StopApplication();
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

            services.Configure<CheckNewsOptions>(context.Configuration);
            services.Configure<SteamOptions>(context.Configuration);
        });

    private static string? GetBetaBranch(SteamOptions options, CancellationToken ct)
    {
        var depots = DepotDownloaderExt.ContentDownloaderGetSteam3AppSection((uint) options.SteamAppId);
        var branches = depots["branches"];
        return branches.Children
            .Where(x => x["pwdrequired"].Value != "1" && x["lcsrequired"].Value != "1")
            .Where(x => x["description"].Value == "beta")
            .Select(x => x.Name)
            .FirstOrDefault();
    }

    private static async Task<string?> GetBranchVersion(string branchName, string tempFolder, SteamOptions options, CancellationToken ct)
    {
        DepotDownloaderExt.ContentDownloaderConfigSetUsingFileList(true);
        var filesToDownload = DepotDownloaderExt.ContentDownloaderConfigGetFilesToDownload();
        filesToDownload.Clear();
        filesToDownload.Add("bin\\Win64_Shipping_Client\\Version.xml");
        filesToDownload.Add("bin/Win64_Shipping_Client/Version.xml");
        var filesToDownloadRegex = DepotDownloaderExt.ContentDownloaderConfigGetFilesToDownloadRegex();
        filesToDownloadRegex.Clear();

        await DepotDownloaderExt.ContentDownloaderDownloadAppAsync(
            (uint) options.SteamAppId,
            options.SteamDepotId.Select(x => ((uint) x, ulong.MaxValue)).ToList(),
            branchName,
            options.SteamOS,
            options.SteamOSArch,
            null!,
            false,
            false).ConfigureAwait(false);
        var file = Path.Combine(tempFolder, "bin\\Win64_Shipping_Client\\Version.xml");
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
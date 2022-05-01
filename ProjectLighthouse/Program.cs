#nullable enable
using System;
using System.Diagnostics;
using LBPUnion.ProjectLighthouse.Helpers;
using LBPUnion.ProjectLighthouse.Logging;
using LBPUnion.ProjectLighthouse.Logging.Loggers;
using LBPUnion.ProjectLighthouse.Logging.Loggers.AspNet;
using LBPUnion.ProjectLighthouse.Types.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LBPUnion.ProjectLighthouse;

public static class Program
{
    public static void Main(string[] args)
    {
        // Log startup time
        Stopwatch stopwatch = new();
        stopwatch.Start();

        // Setup logging
        Logger.AddLogger(new ConsoleLogger());
        Logger.AddLogger(new LighthouseFileLogger());

        Logger.LogInfo("Welcome to Project Lighthouse!", "Startup");
        Logger.LogInfo($"You are running version {VersionHelper.FullVersion}", "Startup");

        // Referencing ServerSettings.Instance here loads the config, see ServerSettings.cs for more information
        Logger.LogSuccess("Loaded config file version " + ServerSettings.Instance.ConfigVersion, "Startup");

        Logger.LogInfo("Determining if the database is available...", "Startup");
        bool dbConnected = ServerStatics.DbConnected;
        if (!dbConnected)
        {
            Logger.LogError("Database unavailable! Exiting.", "Startup");
        }
        else
        {
            Logger.LogSuccess("Connected to the database.", "Startup");
        }

        if (!dbConnected) Environment.Exit(1);
        using Database database = new();

        Logger.LogInfo("Migrating database...", "Database");
        MigrateDatabase(database);

        if (ServerSettings.Instance.InfluxEnabled)
        {
            Logger.LogInfo("Influx logging is enabled. Starting influx logging...", "Startup");
            InfluxHelper.StartLogging().Wait();
            if (ServerSettings.Instance.InfluxLoggingEnabled) Logger.AddLogger(new InfluxLogger());
        }

        Logger.LogDebug
        (
            "This is a debug build, so performance may suffer! " +
            "If you are running Lighthouse in a production environment, " +
            "it is highly recommended to run a release build. ",
            "Startup"
        );
        Logger.LogDebug("You can do so by running any dotnet command with the flag: \"-c Release\". ", "Startup");

        if (args.Length != 0)
        {
            MaintenanceHelper.RunCommand(args).Wait();
            return;
        }

        if (ServerSettings.Instance.ConvertAssetsOnStartup) FileHelper.ConvertAllTexturesToPng();

        Logger.LogInfo("Starting room cleanup thread...", "Startup");
        RoomHelper.StartCleanupThread();

        stopwatch.Stop();
        Logger.LogSuccess($"Ready! Startup took {stopwatch.ElapsedMilliseconds}ms. Passing off control to ASP.NET...", "Startup");

        CreateHostBuilder(args).Build().Run();
    }

    public static void MigrateDatabase(Database database)
    {
        Stopwatch stopwatch = new();
        stopwatch.Start();

        database.Database.MigrateAsync().Wait();

        stopwatch.Stop();
        Logger.LogSuccess($"Migration took {stopwatch.ElapsedMilliseconds}ms.", "Database");
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
        => Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults
            (
                webBuilder =>
                {
                    webBuilder.UseStartup<Startup.Startup>();
                    webBuilder.UseWebRoot("StaticFiles");
                    webBuilder.UseUrls(ServerSettings.Instance.ServerListenUrl);
                }
            )
            .ConfigureLogging
            (
                logging =>
                {
                    logging.ClearProviders();
                    logging.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, AspNetToLighthouseLoggerProvider>());
                }
            );
}
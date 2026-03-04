using EGT.Contracts.Models;
using EGT.Core.Pipeline;
using EGT.Profiles.GenericText;
using EGT.Profiles.RenPy;
using EGT.Translators.DeepL;
using EGT.Translators.Llm;
using EGT.Translators.Microsoft;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EGT.Cli;

public static class Program
{
  public static async Task<int> Main(string[] args)
  {
    if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
    {
      PrintHelp();
      return 0;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
      e.Cancel = true;
      cts.Cancel();
    };

    var host = BuildHost(args);
    var pipeline = host.Services.GetRequiredService<ITranslationPipeline>();
    var config = host.Services.GetRequiredService<IConfiguration>();

    try
    {
      var command = args[0].ToLowerInvariant();
      return command switch
      {
        "run" => await RunAsync(args.Skip(1).ToArray(), config, pipeline, cts.Token),
        "restore" => await RestoreAsync(args.Skip(1).ToArray(), pipeline, cts.Token),
        _ => UnknownCommand(command)
      };
    }
    catch (OperationCanceledException)
    {
      Console.WriteLine("Operation cancelled.");
      return 2;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error: {ex.Message}");
      return 1;
    }
  }

  private static IHost BuildHost(string[] args)
  {
    return Host.CreateDefaultBuilder(args)
      .ConfigureAppConfiguration((context, cfg) =>
      {
        cfg.Sources.Clear();
        cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        cfg.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        cfg.AddEnvironmentVariables("EGT_");
        cfg.AddCommandLine(args);
      })
      .UseSerilog((context, _, loggerConfig) =>
      {
        loggerConfig.ReadFrom.Configuration(context.Configuration);
      })
      .ConfigureServices((context, services) =>
      {
        services.AddEgtCore();
        services.AddRenPyProfile();
        services.AddGenericTextProfile();
        services.AddDeepLProvider();
        services.AddMicrosoftProvider();
        services.AddLlmProvider();

        var pluginsDir = context.Configuration["Plugins:Directory"] ?? "Plugins";
        PluginLoader.Load(services, pluginsDir);
      })
      .Build();
  }

  private static async Task<int> RunAsync(
    string[] args,
    IConfiguration config,
    ITranslationPipeline pipeline,
    CancellationToken ct)
  {
    var exePath = ReadOption(args, "--exe");
    if (string.IsNullOrWhiteSpace(exePath))
    {
      Console.WriteLine("Missing required option: --exe");
      return 1;
    }

    var defaults = config.GetSection("PipelineDefaults");
    var chunkSentenceCount = ReadInt(args, "--chunk-sentences")
      ?? ReadInt(args, "--batch-size")
      ?? int.Parse(defaults["ChunkSentenceCount"] ?? defaults["MaxItemsPerBatch"] ?? "40");

    var options = new PipelineOptions
    {
      ProfileName = ReadOption(args, "--profile"),
      ProviderName = ReadOption(args, "--provider") ?? defaults["ProviderName"] ?? "mock",
      FallbackProviderName = ReadOption(args, "--fallback-provider") ?? defaults["FallbackProviderName"],
      SecondFallbackProviderName = ReadOption(args, "--fallback2-provider") ?? defaults["SecondFallbackProviderName"],
      SourceLang = ReadOption(args, "--source") ?? defaults["SourceLang"] ?? "auto",
      TargetLang = ReadOption(args, "--target") ?? defaults["TargetLang"] ?? "zh-Hans",
      ApplyInPlace = HasFlag(args, "--apply"),
      OverwriteOutput = HasFlag(args, "--overwrite-output"),
      MaxConcurrency = ReadInt(args, "--concurrency") ?? int.Parse(defaults["MaxConcurrency"] ?? "4"),
      MaxItemsPerBatch = chunkSentenceCount,
      MaxCharsPerBatch = ReadInt(args, "--batch-chars") ?? int.Parse(defaults["MaxCharsPerBatch"] ?? "8000"),
      AiBatchSize = ReadInt(args, "--ai-batch-size") ?? int.Parse(defaults["AiBatchSize"] ?? "12"),
      MaxFileSizeMb = ReadInt(args, "--max-size-mb") ?? int.Parse(defaults["MaxFileSizeMb"] ?? "50"),
      GlossaryCsvPath = ReadOption(args, "--glossary"),
      OutputRoot = ReadOption(args, "--output") ?? defaults["OutputRoot"] ?? "EGT_Output",
      BackupRoot = ReadOption(args, "--backup") ?? defaults["BackupRoot"] ?? "EGT_Backup",
      CacheFilePath = ReadOption(args, "--cache") ?? defaults["CacheFilePath"] ?? "EGT_Cache/translation_cache.db",
      ProviderApiKey = ReadOption(args, "--api-key") ??
                       Environment.GetEnvironmentVariable("EGT_PROVIDER_API_KEY") ??
                       defaults["ProviderApiKey"],
      ProviderEndpoint = ReadOption(args, "--base-url") ??
                         Environment.GetEnvironmentVariable("EGT_PROVIDER_ENDPOINT") ??
                         defaults["ProviderEndpoint"],
      ProviderModel = ReadOption(args, "--model") ??
                      Environment.GetEnvironmentVariable("EGT_PROVIDER_MODEL") ??
                      defaults["ProviderModel"],
      ProviderRegion = ReadOption(args, "--region") ??
                       Environment.GetEnvironmentVariable("EGT_PROVIDER_REGION") ??
                       defaults["ProviderRegion"],
      FallbackProviderApiKey = ReadOption(args, "--fallback-api-key") ??
                               Environment.GetEnvironmentVariable("EGT_FALLBACK_PROVIDER_API_KEY") ??
                               defaults["FallbackProviderApiKey"],
      FallbackProviderEndpoint = ReadOption(args, "--fallback-base-url") ??
                                 Environment.GetEnvironmentVariable("EGT_FALLBACK_PROVIDER_ENDPOINT") ??
                                 defaults["FallbackProviderEndpoint"],
      FallbackProviderModel = ReadOption(args, "--fallback-model") ??
                              Environment.GetEnvironmentVariable("EGT_FALLBACK_PROVIDER_MODEL") ??
                              defaults["FallbackProviderModel"],
      FallbackProviderRegion = ReadOption(args, "--fallback-region") ??
                               Environment.GetEnvironmentVariable("EGT_FALLBACK_PROVIDER_REGION") ??
                               defaults["FallbackProviderRegion"],
      SecondFallbackProviderApiKey = ReadOption(args, "--fallback2-api-key") ??
                                     Environment.GetEnvironmentVariable("EGT_SECOND_FALLBACK_PROVIDER_API_KEY") ??
                                     defaults["SecondFallbackProviderApiKey"],
      SecondFallbackProviderEndpoint = ReadOption(args, "--fallback2-base-url") ??
                                       Environment.GetEnvironmentVariable("EGT_SECOND_FALLBACK_PROVIDER_ENDPOINT") ??
                                       defaults["SecondFallbackProviderEndpoint"],
      SecondFallbackProviderModel = ReadOption(args, "--fallback2-model") ??
                                    Environment.GetEnvironmentVariable("EGT_SECOND_FALLBACK_PROVIDER_MODEL") ??
                                    defaults["SecondFallbackProviderModel"],
      SecondFallbackProviderRegion = ReadOption(args, "--fallback2-region") ??
                                     Environment.GetEnvironmentVariable("EGT_SECOND_FALLBACK_PROVIDER_REGION") ??
                                     defaults["SecondFallbackProviderRegion"]
    };

    Console.WriteLine($"Running translation for: {exePath}");
    var progress = new Progress<PipelineProgress>(p =>
    {
      Console.WriteLine(
        $"[{p.Stage}] {p.ProcessedItems}/{p.TotalItems} | {p.ItemsPerSecond:F2}/s | {p.Message}");
    });

    var result = await pipeline.RunAsync(exePath, options, progress, ct);
    Console.WriteLine($"Done. Manifest: {result.ManifestPath}");
    Console.WriteLine($"Output: {result.OutputRoot}");
    Console.WriteLine($"Stats: total={result.TotalItems}, success={result.SuccessItems}, failed={result.FailedItems}");
    if (!string.IsNullOrWhiteSpace(result.CacheFilePath))
    {
      Console.WriteLine($"Cache: {result.CacheFilePath}");
    }

    if (!string.IsNullOrWhiteSpace(result.QualityReportPath))
    {
      Console.WriteLine($"Quality report: {result.QualityReportPath}");
      Console.WriteLine(
        $"Quality summary: unique={result.UniqueSourceCount}, cacheHits={result.CacheHits}, glossaryHits={result.GlossaryHits}, failedUnique={result.FailedUniqueSources}, identity={result.IdentityCount}, avgLenRatio={result.AverageLengthRatio:F2}");
    }

    if (!string.IsNullOrWhiteSpace(result.TranslationPreviewPath))
    {
      Console.WriteLine($"Preview CSV: {result.TranslationPreviewPath}");
    }

    if (!string.IsNullOrWhiteSpace(result.FailedItemsPath))
    {
      Console.WriteLine($"Failed items CSV: {result.FailedItemsPath}");
    }

    foreach (var warning in result.Warnings.Take(10))
    {
      Console.WriteLine($"warning: {warning}");
    }

    return result.FailedItems > 0 ? 3 : 0;
  }

  private static async Task<int> RestoreAsync(string[] args, ITranslationPipeline pipeline, CancellationToken ct)
  {
    var manifestPath = ReadOption(args, "--manifest");
    if (string.IsNullOrWhiteSpace(manifestPath))
    {
      Console.WriteLine("Missing required option: --manifest");
      return 1;
    }

    await pipeline.RestoreAsync(manifestPath, ct);
    Console.WriteLine("Restore completed.");
    return 0;
  }

  private static string? ReadOption(string[] args, string name)
  {
    for (var i = 0; i < args.Length; i++)
    {
      if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (i + 1 < args.Length)
      {
        return args[i + 1];
      }
    }

    return null;
  }

  private static int? ReadInt(string[] args, string name)
  {
    var value = ReadOption(args, name);
    return int.TryParse(value, out var parsed) ? parsed : null;
  }

  private static bool HasFlag(string[] args, string flag) =>
    args.Any(x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));

  private static int UnknownCommand(string command)
  {
    Console.WriteLine($"Unknown command: {command}");
    PrintHelp();
    return 1;
  }

  private static void PrintHelp()
  {
    Console.WriteLine(
      """
      easy_game_translator CLI (egt)

      Usage:
        egt run --exe <path> [options]
        egt restore --manifest <path>

      Run options:
        --provider <mock|deepl|microsoft|llm>    Translation provider
        --profile <auto|generic-text|renpy>     Profile name (default auto)
        --source <lang>                          Source language (default auto)
        --target <lang>                          Target language (default zh-Hans)
        --apply                                  Apply translated files to game folder (backup required)
        --output <path>                          Output root directory
        --backup <path>                          Backup root directory
        --glossary <csv>                         Glossary CSV path
        --api-key <key>                          Provider API key
        --base-url <url>                         Provider endpoint/base URL
        --model <model>                          LLM model
        --region <region>                        Microsoft region
        --fallback-provider <name>               Fallback provider if primary fails
        --fallback-api-key <key>                 Fallback provider API key
        --fallback-base-url <url>                Fallback provider endpoint
        --fallback-model <model>                 Fallback LLM model
        --fallback-region <region>               Fallback provider region
        --fallback2-provider <name>              Third-level fallback provider
        --fallback2-api-key <key>                Third-level fallback API key
        --fallback2-base-url <url>               Third-level fallback endpoint
        --fallback2-model <model>                Third-level fallback model
        --fallback2-region <region>              Third-level fallback region
        --concurrency <n>                        Max translation concurrency
        --chunk-sentences <n>                    Auto chunk translatable items by sentence count
        --batch-size <n>                         Alias of --chunk-sentences
        --batch-chars <n>                        Max chars per batch sent to provider
        --ai-batch-size <n>                      LLM items per single AI request
        --max-size-mb <n>                        Max scan file size in MB
      """);
  }
}

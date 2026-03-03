using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EGT.Contracts.Models;
using EGT.Contracts.Profiles;
using EGT.Contracts.Translation;
using EGT.Core.Abstractions;
using EGT.Core.Manifesting;
using EGT.Core.Translation;
using Microsoft.Extensions.Logging;

namespace EGT.Core.Pipeline;

public sealed class TranslationPipeline : ITranslationPipeline
{
  private const string RulesVersion = "v1";

  private readonly IGameProjectResolver _projectResolver;
  private readonly ProfileSelector _profileSelector;
  private readonly ProviderSelector _providerSelector;
  private readonly GlossaryLoader _glossaryLoader;
  private readonly PlaceholderProtector _placeholderProtector;
  private readonly ITranslationCache _cache;
  private readonly ISecretStore _secretStore;
  private readonly IManifestWriter _manifestWriter;
  private readonly IRestoreService _restoreService;
  private readonly ILogger<TranslationPipeline> _logger;

  public TranslationPipeline(
    IGameProjectResolver projectResolver,
    ProfileSelector profileSelector,
    ProviderSelector providerSelector,
    GlossaryLoader glossaryLoader,
    PlaceholderProtector placeholderProtector,
    ITranslationCache cache,
    ISecretStore secretStore,
    IManifestWriter manifestWriter,
    IRestoreService restoreService,
    ILogger<TranslationPipeline> logger)
  {
    _projectResolver = projectResolver;
    _profileSelector = profileSelector;
    _providerSelector = providerSelector;
    _glossaryLoader = glossaryLoader;
    _placeholderProtector = placeholderProtector;
    _cache = cache;
    _secretStore = secretStore;
    _manifestWriter = manifestWriter;
    _restoreService = restoreService;
    _logger = logger;
  }

  public async Task<TranslationRunResult> RunAsync(
    string exePath,
    PipelineOptions options,
    IProgress<PipelineProgress>? progress,
    CancellationToken ct)
  {
    var stopwatch = Stopwatch.StartNew();
    var warnings = new List<string>();
    var (resolvedCacheFilePath, migratedFromPath) = ResolveCacheFilePath(options.CacheFilePath);
    if (!string.IsNullOrWhiteSpace(migratedFromPath))
    {
      warnings.Add($"检测到旧缓存并已迁移：{migratedFromPath} -> {resolvedCacheFilePath}");
    }

    var project = _projectResolver.Resolve(exePath);
    Report(progress, "detect-project", 0, 0, null, stopwatch, $"已识别游戏项目：{project.Name}");

    var profile = _profileSelector.Select(project, options);
    _logger.LogInformation("Profile selected: {ProfileName}", profile.Name);

    var provider = _providerSelector.Select(options.ProviderName);
    _logger.LogInformation("Provider selected: {ProviderName}", provider.Name);

    ITranslationProvider? fallbackProvider = null;
    if (!string.IsNullOrWhiteSpace(options.FallbackProviderName))
    {
      fallbackProvider = _providerSelector.Select(options.FallbackProviderName);
      _logger.LogInformation("Fallback provider selected: {ProviderName}", fallbackProvider.Name);
    }

    Report(progress, "extract", 0, 0, null, stopwatch, $"正在使用 Profile 抽取：{profile.Name}");
    var extraction = await profile.ExtractAsync(project, options, ct);
    var total = extraction.Entries.Count;
    Report(progress, "extract", 0, total, null, stopwatch, $"已抽取 {total} 条文本");

    var glossary = await _glossaryLoader.LoadAsync(options.GlossaryCsvPath, ct);
    var resolvedApiKey = await ResolveProviderApiKeyAsync(provider.Name, options, ct);
    var resolvedFallbackApiKey = await ResolveFallbackProviderApiKeyAsync(options, ct);
    var translateOptions = options.ToTranslateOptions(glossary, resolvedApiKey, resolvedFallbackApiKey);

    var translatedByEntryId = new Dictionary<string, string>(StringComparer.Ordinal);
    var translatedBySource = new Dictionary<string, string>(StringComparer.Ordinal);
    var failedSources = new HashSet<string>(StringComparer.Ordinal);
    var failedDetails = new List<FailedTranslationItem>();
    var glossaryHitCount = 0;
    var cacheHitCount = 0;

    var sourceGroups = extraction.Entries
      .GroupBy(x => x.SourceText, StringComparer.Ordinal)
      .ToList();

    var pending = new List<(string GroupKey, TranslateItem Item, string CacheKey)>();
    foreach (var group in sourceGroups)
    {
      ct.ThrowIfCancellationRequested();

      var source = group.Key;
      if (glossary?.Entries.TryGetValue(source, out var glossaryHit) == true)
      {
        translatedBySource[source] = glossaryHit;
        glossaryHitCount++;
        continue;
      }

      var protectedResult = _placeholderProtector.Protect(source);
      var cacheKey = BuildCacheKey(provider.Name, translateOptions, protectedResult.ProtectedText, glossary?.Version);
      var cached = await _cache.GetAsync(resolvedCacheFilePath, cacheKey, ct);
      if (!string.IsNullOrEmpty(cached))
      {
        translatedBySource[source] = cached;
        cacheHitCount++;
        continue;
      }

      var item = new TranslateItem
      {
        Id = Hashing.Sha256(source),
        Source = protectedResult.ProtectedText,
        Context = string.Join(" | ", group.Select(x => $"{x.RelativePath}@{x.Start}").Take(3)),
        Placeholders = protectedResult.Map
      };

      pending.Add((source, item, cacheKey));
    }

    var completed = glossaryHitCount + cacheHitCount;
    if (sourceGroups.Count > 0)
    {
      Report(
        progress,
        "translate",
        completed,
        sourceGroups.Count,
        null,
        stopwatch,
        $"断点续传：缓存命中 {cacheHitCount}，术语命中 {glossaryHitCount}，待翻译 {pending.Count}");
    }

    var maxItemsPerBatch = Math.Max(1, options.MaxItemsPerBatch);
    var maxCharsPerBatch = Math.Max(200, options.MaxCharsPerBatch);
    var batches = SplitBatches(pending, maxItems: maxItemsPerBatch, maxChars: maxCharsPerBatch).ToList();
    for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
    {
      var batch = batches[batchIndex];
      ct.ThrowIfCancellationRequested();
      var batchItems = batch.Select(x => x.Item).ToList();
      Report(
        progress,
        "translate",
        completed,
        sourceGroups.Count,
        batchItems[0].Context,
        stopwatch,
        $"请求批次 {batchIndex + 1}/{batches.Count}（{batchItems.Count} 条）");

      TranslateResult result;
      try
      {
        result = await provider.TranslateBatchAsync(batchItems, translateOptions, ct);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Primary provider threw exception. Batch will be marked for fallback.");
        warnings.Add($"primary-provider-exception: {ex.Message}");
        result = new TranslateResult
        {
          Items = Array.Empty<TranslatedItem>(),
          Errors = batchItems.Select(x => new ProviderError
          {
            Id = x.Id,
            Code = "provider_exception",
            Message = ex.Message,
            IsTransient = true
          }).ToList()
        };
      }

      var translatedMap = result.Items.ToDictionary(x => x.Id, x => x.TranslatedText, StringComparer.Ordinal);
      var errorsMap = result.Errors.ToDictionary(x => x.Id, x => x, StringComparer.Ordinal);

      if (fallbackProvider is not null && errorsMap.Count > 0)
      {
        var failedItems = batchItems.Where(x => errorsMap.ContainsKey(x.Id)).ToList();
        if (failedItems.Count > 0)
        {
          var fallbackOptions = BuildFallbackTranslateOptions(translateOptions);
          TranslateResult fallbackResult;
          try
          {
            fallbackResult = await fallbackProvider.TranslateBatchAsync(failedItems, fallbackOptions, ct);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Fallback provider threw exception.");
            warnings.Add($"fallback-provider-exception: {ex.Message}");
            fallbackResult = new TranslateResult
            {
              Items = Array.Empty<TranslatedItem>(),
              Errors = failedItems.Select(x => new ProviderError
              {
                Id = x.Id,
                Code = "fallback_provider_exception",
                Message = ex.Message,
                IsTransient = true
              }).ToList()
            };
          }

          foreach (var fallbackItem in fallbackResult.Items)
          {
            translatedMap[fallbackItem.Id] = fallbackItem.TranslatedText;
            errorsMap.Remove(fallbackItem.Id);
          }

          foreach (var fallbackError in fallbackResult.Errors)
          {
            errorsMap[fallbackError.Id] = fallbackError;
          }
        }
      }

      Report(
        progress,
        "translate",
        completed,
        sourceGroups.Count,
        batchItems[0].Context,
        stopwatch,
        $"批次 {batchIndex + 1}/{batches.Count} 已返回，正在写入缓存");

      foreach (var entry in batch)
      {
        if (translatedMap.TryGetValue(entry.Item.Id, out var translatedRaw))
        {
          var restored = _placeholderProtector.Restore(translatedRaw, entry.Item.Placeholders);
          translatedBySource[entry.GroupKey] = restored;
          await _cache.SetAsync(resolvedCacheFilePath, entry.CacheKey, restored, ct);
        }
        else
        {
          translatedBySource[entry.GroupKey] = entry.GroupKey;
          failedSources.Add(entry.GroupKey);
          var errorCode = "missing_item";
          var errorMessage = "Missing translated item in provider response.";
          if (errorsMap.TryGetValue(entry.Item.Id, out var error))
          {
            errorCode = error.Code;
            errorMessage = error.Message;
            warnings.Add($"{entry.Item.Id}: {error.Code} - {error.Message}");
          }

          failedDetails.Add(new FailedTranslationItem
          {
            Id = entry.Item.Id,
            Source = entry.GroupKey,
            Context = entry.Item.Context ?? string.Empty,
            Code = errorCode,
            Message = errorMessage
          });
        }

        completed++;
        Report(progress, "translate", completed, sourceGroups.Count, entry.Item.Context, stopwatch, "翻译中");
      }
    }

    foreach (var extracted in extraction.Entries)
    {
      if (translatedBySource.TryGetValue(extracted.SourceText, out var translated))
      {
        translatedByEntryId[extracted.Id] = translated;
      }
      else
      {
        translatedByEntryId[extracted.Id] = extracted.SourceText;
      }
    }

    Report(progress, "apply", total, total, null, stopwatch, "正在写入翻译结果");
    var apply = await profile.ApplyAsync(project, extraction, translatedByEntryId, options, ct);

    var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
    var outputRoot = Path.GetFullPath(Path.Combine(options.OutputRoot, project.Name, runId));
    var backupRoot = Path.GetFullPath(Path.Combine(options.BackupRoot, runId));

    var failedEntryCount = extraction.Entries.Count(x => failedSources.Contains(x.SourceText));

    var stats = new ManifestStatistics
    {
      TotalEntries = total,
      Succeeded = total - failedEntryCount,
      Failed = failedEntryCount,
      Skipped = 0
    };

    var manifestPath = await _manifestWriter.WriteAsync(
      project,
      options,
      runId,
      apply,
      stats,
      outputRoot,
      backupRoot,
      ct);

    var qualityReport = await WriteQualityReportAsync(
      outputRoot,
      sourceGroups,
      translatedBySource,
      failedSources,
      failedDetails,
      cacheHitCount,
      glossaryHitCount,
      pending.Count,
      ct);

    warnings.Insert(0, $"质量报告：{qualityReport.JsonPath}");
    warnings.Insert(1, $"缓存文件：{resolvedCacheFilePath}");
    warnings.Insert(2, $"翻译预览：{qualityReport.PreviewCsvPath}");
    if (!string.IsNullOrWhiteSpace(qualityReport.FailedItemsCsvPath))
    {
      warnings.Insert(3, $"失败明细：{qualityReport.FailedItemsCsvPath}");
    }

    Report(progress, "done", total, total, null, stopwatch, "已完成");

    return new TranslationRunResult
    {
      Success = failedEntryCount == 0,
      ManifestPath = manifestPath,
      OutputRoot = outputRoot,
      TotalItems = total,
      SuccessItems = total - failedEntryCount,
      FailedItems = failedEntryCount,
      Warnings = warnings,
      QualityReportPath = qualityReport.JsonPath,
      TranslationPreviewPath = qualityReport.PreviewCsvPath,
      FailedItemsPath = qualityReport.FailedItemsCsvPath,
      CacheFilePath = resolvedCacheFilePath,
      UniqueSourceCount = qualityReport.TotalUniqueSources,
      CacheHits = qualityReport.CacheHits,
      GlossaryHits = qualityReport.GlossaryHits,
      FailedUniqueSources = qualityReport.FailedUniqueSources,
      IdentityCount = qualityReport.IdentityCount,
      AverageLengthRatio = qualityReport.AverageLengthRatio
    };
  }

  public Task RestoreAsync(string manifestPath, CancellationToken ct) =>
    _restoreService.RestoreAsync(manifestPath, ct);

  private static IEnumerable<List<(string GroupKey, TranslateItem Item, string CacheKey)>> SplitBatches(
    List<(string GroupKey, TranslateItem Item, string CacheKey)> items,
    int maxItems,
    int maxChars)
  {
    var batch = new List<(string GroupKey, TranslateItem Item, string CacheKey)>();
    var chars = 0;

    foreach (var item in items)
    {
      if (batch.Count >= maxItems || chars + item.Item.Source.Length > maxChars)
      {
        yield return batch;
        batch = new List<(string GroupKey, TranslateItem Item, string CacheKey)>();
        chars = 0;
      }

      batch.Add(item);
      chars += item.Item.Source.Length;
    }

    if (batch.Count > 0)
    {
      yield return batch;
    }
  }

  private static string BuildCacheKey(
    string provider,
    TranslateOptions options,
    string normalizedSource,
    string? glossaryVersion)
  {
    var payload =
      $"{provider}|{options.SourceLang}|{options.TargetLang}|{normalizedSource.Trim()}|{glossaryVersion ?? "none"}|{RulesVersion}";
    return Hashing.Sha256(payload);
  }

  private static (string ResolvedPath, string? MigratedFromPath) ResolveCacheFilePath(string configuredPath)
  {
    if (string.IsNullOrWhiteSpace(configuredPath))
    {
      configuredPath = "translation_cache.db";
    }

    if (Path.IsPathRooted(configuredPath))
    {
      return (configuredPath, null);
    }

    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var normalized = configuredPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    var resolved = Path.Combine(localAppData, "easy_game_translator", normalized);
    var legacy = Path.GetFullPath(configuredPath);

    try
    {
      if (File.Exists(legacy) && !File.Exists(resolved))
      {
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        File.Copy(legacy, resolved, overwrite: false);
        return (resolved, legacy);
      }
    }
    catch
    {
      // Cache migration failure should never fail the translation flow.
    }

    return (resolved, null);
  }

  private static async Task<QualityReportOutput> WriteQualityReportAsync(
    string outputRoot,
    IReadOnlyList<IGrouping<string, ExtractedEntry>> sourceGroups,
    IReadOnlyDictionary<string, string> translatedBySource,
    IReadOnlySet<string> failedSources,
    IReadOnlyList<FailedTranslationItem> failedDetails,
    int cacheHitCount,
    int glossaryHitCount,
    int pendingCount,
    CancellationToken ct)
  {
    var totalUnique = sourceGroups.Count;
    var translatedUnique = translatedBySource.Count;
    var failedUnique = failedSources.Count;
    var identityCount = translatedBySource.Count(x => string.Equals(x.Key, x.Value, StringComparison.Ordinal));

    var comparable = translatedBySource
      .Where(x => !string.IsNullOrWhiteSpace(x.Key))
      .Select(x => new
      {
        SourceLength = x.Key.Length,
        TargetLength = x.Value?.Length ?? 0
      })
      .Where(x => x.SourceLength > 0)
      .ToList();

    var avgLengthRatio = comparable.Count == 0
      ? 0d
      : comparable.Average(x => (double)x.TargetLength / x.SourceLength);

    var preview = sourceGroups
      .Take(50)
      .Select(g =>
      {
        var source = g.Key;
        translatedBySource.TryGetValue(source, out var translated);
        translated ??= source;
        return new QualityPreviewItem
        {
          Source = source,
          Translated = translated,
          IsIdentity = string.Equals(source, translated, StringComparison.Ordinal),
          File = g.First().RelativePath
        };
      })
      .ToList();

    var report = new QualityReport
    {
      GeneratedAtUtc = DateTimeOffset.UtcNow,
      TotalUniqueSources = totalUnique,
      PendingUniqueSources = pendingCount,
      CacheHits = cacheHitCount,
      GlossaryHits = glossaryHitCount,
      TranslatedUniqueSources = translatedUnique,
      FailedUniqueSources = failedUnique,
      IdentityCount = identityCount,
      AverageLengthRatio = Math.Round(avgLengthRatio, 4),
      Preview = preview
    };

    var reportDir = Path.Combine(outputRoot, "report");
    Directory.CreateDirectory(reportDir);
    var jsonPath = Path.Combine(reportDir, "quality_report.json");
    var previewCsvPath = Path.Combine(reportDir, "translation_preview.csv");
    var failedCsvPath = Path.Combine(reportDir, "failed_items.csv");

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
      WriteIndented = true
    });
    await File.WriteAllTextAsync(jsonPath, json, ct);

    var csv = new StringBuilder();
    csv.AppendLine("file,is_identity,source,translated");
    foreach (var item in preview)
    {
      csv
        .Append(EscapeCsv(item.File)).Append(',')
        .Append(item.IsIdentity ? "true" : "false").Append(',')
        .Append(EscapeCsv(item.Source)).Append(',')
        .Append(EscapeCsv(item.Translated)).AppendLine();
    }

    await File.WriteAllTextAsync(previewCsvPath, csv.ToString(), ct);

    if (failedDetails.Count > 0)
    {
      var failedCsv = new StringBuilder();
      failedCsv.AppendLine("id,code,message,context,source");
      foreach (var failed in failedDetails)
      {
        failedCsv
          .Append(EscapeCsv(failed.Id)).Append(',')
          .Append(EscapeCsv(failed.Code)).Append(',')
          .Append(EscapeCsv(failed.Message)).Append(',')
          .Append(EscapeCsv(failed.Context)).Append(',')
          .Append(EscapeCsv(failed.Source)).AppendLine();
      }

      await File.WriteAllTextAsync(failedCsvPath, failedCsv.ToString(), ct);
    }
    else if (File.Exists(failedCsvPath))
    {
      File.Delete(failedCsvPath);
    }

    return new QualityReportOutput
    {
      JsonPath = jsonPath,
      PreviewCsvPath = previewCsvPath,
      FailedItemsCsvPath = failedDetails.Count > 0 ? failedCsvPath : null,
      TotalUniqueSources = totalUnique,
      CacheHits = cacheHitCount,
      GlossaryHits = glossaryHitCount,
      FailedUniqueSources = failedUnique,
      IdentityCount = identityCount,
      AverageLengthRatio = Math.Round(avgLengthRatio, 4)
    };
  }

  private static string EscapeCsv(string value)
  {
    var text = value.Replace("\"", "\"\"", StringComparison.Ordinal);
    return $"\"{text}\"";
  }

  private sealed class QualityReport
  {
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required int TotalUniqueSources { get; init; }
    public required int PendingUniqueSources { get; init; }
    public required int CacheHits { get; init; }
    public required int GlossaryHits { get; init; }
    public required int TranslatedUniqueSources { get; init; }
    public required int FailedUniqueSources { get; init; }
    public required int IdentityCount { get; init; }
    public required double AverageLengthRatio { get; init; }
    public required IReadOnlyList<QualityPreviewItem> Preview { get; init; }
  }

  private sealed class QualityPreviewItem
  {
    public required string File { get; init; }
    public required string Source { get; init; }
    public required string Translated { get; init; }
    public required bool IsIdentity { get; init; }
  }

  private sealed class FailedTranslationItem
  {
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required string Context { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
  }

  private sealed class QualityReportOutput
  {
    public required string JsonPath { get; init; }
    public required string PreviewCsvPath { get; init; }
    public string? FailedItemsCsvPath { get; init; }
    public required int TotalUniqueSources { get; init; }
    public required int CacheHits { get; init; }
    public required int GlossaryHits { get; init; }
    public required int FailedUniqueSources { get; init; }
    public required int IdentityCount { get; init; }
    public required double AverageLengthRatio { get; init; }
  }

  private async Task<string?> ResolveProviderApiKeyAsync(
    string providerName,
    PipelineOptions options,
    CancellationToken ct)
  {
    if (string.Equals(providerName, "mock", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var secretKey = $"provider:{providerName}:api-key";
    if (!string.IsNullOrWhiteSpace(options.ProviderApiKey))
    {
      await _secretStore.SaveAsync(secretKey, options.ProviderApiKey, ct);
      return options.ProviderApiKey;
    }

    return await _secretStore.GetAsync(secretKey, ct);
  }

  private async Task<string?> ResolveFallbackProviderApiKeyAsync(PipelineOptions options, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(options.FallbackProviderName))
    {
      return null;
    }

    var secretKey = $"provider:{options.FallbackProviderName}:fallback-api-key";
    if (!string.IsNullOrWhiteSpace(options.FallbackProviderApiKey))
    {
      await _secretStore.SaveAsync(secretKey, options.FallbackProviderApiKey, ct);
      return options.FallbackProviderApiKey;
    }

    return await _secretStore.GetAsync(secretKey, ct);
  }

  private static TranslateOptions BuildFallbackTranslateOptions(TranslateOptions options)
  {
    return new TranslateOptions
    {
      SourceLang = options.SourceLang,
      TargetLang = options.TargetLang,
      Glossary = options.Glossary,
      MaxConcurrency = options.MaxConcurrency,
      MaxItemsPerBatch = options.MaxItemsPerBatch,
      MaxCharsPerBatch = options.MaxCharsPerBatch,
      AiBatchSize = options.AiBatchSize,
      PreserveFormatting = options.PreserveFormatting,
      ProviderApiKey = options.FallbackProviderApiKey,
      ProviderEndpoint = options.FallbackProviderEndpoint,
      ProviderModel = options.FallbackProviderModel,
      ProviderRegion = options.FallbackProviderRegion,
      FallbackProviderApiKey = null,
      FallbackProviderEndpoint = null,
      FallbackProviderModel = null,
      FallbackProviderRegion = null
    };
  }

  private static void Report(
    IProgress<PipelineProgress>? progress,
    string stage,
    int processed,
    int total,
    string? file,
    Stopwatch stopwatch,
    string message)
  {
    if (progress is null)
    {
      return;
    }

    var rate = stopwatch.Elapsed.TotalSeconds <= 0
      ? 0
      : processed / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);

    progress.Report(new PipelineProgress(stage, processed, total, file, rate, message));
  }
}

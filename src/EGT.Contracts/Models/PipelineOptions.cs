using EGT.Contracts.Translation;

namespace EGT.Contracts.Models;

public sealed class PipelineOptions
{
  public string? ProfileName { get; init; }
  public string ProviderName { get; init; } = "mock";
  public string? FallbackProviderName { get; init; }
  public string SourceLang { get; init; } = "auto";
  public string TargetLang { get; init; } = "zh-Hans";
  public bool PreserveFormatting { get; init; } = true;
  public int MaxConcurrency { get; init; } = 4;
  public int MaxItemsPerBatch { get; init; } = 40;
  public int MaxCharsPerBatch { get; init; } = 8000;
  public int AiBatchSize { get; init; } = 12;
  public int MaxFileSizeMb { get; init; } = 50;
  public bool ApplyInPlace { get; init; }
  public bool OverwriteOutput { get; init; }
  public string OutputRoot { get; init; } = "EGT_Output";
  public string BackupRoot { get; init; } = "EGT_Backup";
  public string CacheFilePath { get; init; } = "EGT_Cache/translation_cache.db";
  public string? GlossaryCsvPath { get; init; }
  public string? ProviderApiKey { get; init; }
  public string? ProviderEndpoint { get; init; }
  public string? ProviderModel { get; init; }
  public string? ProviderRegion { get; init; }
  public string? FallbackProviderApiKey { get; init; }
  public string? FallbackProviderEndpoint { get; init; }
  public string? FallbackProviderModel { get; init; }
  public string? FallbackProviderRegion { get; init; }
  public bool UploadSourceFilesToProvider { get; init; }
  public IReadOnlyCollection<string> IncludeFolders { get; init; } = Array.Empty<string>();
  public IReadOnlyCollection<string> ExcludeExtensions { get; init; } =
    new[]
    {
      ".pak", ".bundle", ".dll", ".exe", ".bin", ".dat", ".mp4", ".mp3", ".ogg", ".wav"
    };

  public TranslateOptions ToTranslateOptions(
    Glossary? glossary,
    string? providerApiKeyOverride = null,
    string? fallbackProviderApiKeyOverride = null) =>
    new()
    {
      SourceLang = SourceLang,
      TargetLang = TargetLang,
      Glossary = glossary,
      MaxConcurrency = MaxConcurrency,
      MaxItemsPerBatch = MaxItemsPerBatch,
      MaxCharsPerBatch = MaxCharsPerBatch,
      AiBatchSize = AiBatchSize,
      PreserveFormatting = PreserveFormatting,
      ProviderApiKey = providerApiKeyOverride ?? ProviderApiKey,
      ProviderEndpoint = ProviderEndpoint,
      ProviderModel = ProviderModel,
      ProviderRegion = ProviderRegion,
      FallbackProviderApiKey = fallbackProviderApiKeyOverride ?? FallbackProviderApiKey,
      FallbackProviderEndpoint = FallbackProviderEndpoint,
      FallbackProviderModel = FallbackProviderModel,
      FallbackProviderRegion = FallbackProviderRegion
    };
}

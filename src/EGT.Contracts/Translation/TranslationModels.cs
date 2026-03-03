namespace EGT.Contracts.Translation;

public sealed class TranslateItem
{
  public required string Id { get; init; }
  public required string Source { get; init; }
  public string? Context { get; init; }
  public PlaceholderMap Placeholders { get; init; } = PlaceholderMap.Empty;
}

public sealed class TranslateOptions
{
  public string SourceLang { get; init; } = "auto";
  public string TargetLang { get; init; } = "zh-Hans";
  public Glossary? Glossary { get; init; }
  public int MaxConcurrency { get; init; } = 4;
  public int MaxItemsPerBatch { get; init; } = 40;
  public int MaxCharsPerBatch { get; init; } = 8000;
  public int AiBatchSize { get; init; } = 12;
  public bool PreserveFormatting { get; init; } = true;
  public string? ProviderApiKey { get; init; }
  public string? ProviderEndpoint { get; init; }
  public string? ProviderModel { get; init; }
  public string? ProviderRegion { get; init; }
  public string? FallbackProviderApiKey { get; init; }
  public string? FallbackProviderEndpoint { get; init; }
  public string? FallbackProviderModel { get; init; }
  public string? FallbackProviderRegion { get; init; }
}

public sealed class TranslateResult
{
  public required IReadOnlyList<TranslatedItem> Items { get; init; }
  public required IReadOnlyList<ProviderError> Errors { get; init; }
}

public sealed class TranslatedItem
{
  public required string Id { get; init; }
  public required string TranslatedText { get; init; }
}

public sealed class ProviderError
{
  public required string Id { get; init; }
  public required string Code { get; init; }
  public required string Message { get; init; }
  public bool IsTransient { get; init; }
}

public sealed class Glossary
{
  public required string Version { get; init; }
  public required IReadOnlyDictionary<string, string> Entries { get; init; }
}

public sealed class PlaceholderMap
{
  public static PlaceholderMap Empty { get; } = new(new Dictionary<string, string>());

  public PlaceholderMap(IReadOnlyDictionary<string, string> values)
  {
    Values = values;
  }

  public IReadOnlyDictionary<string, string> Values { get; }
}

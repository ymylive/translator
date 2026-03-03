using EGT.Contracts.Translation;

namespace EGT.Core.Translation;

public sealed class MockTranslationProvider : ITranslationProvider
{
  public string Name => "mock";

  public Task<TranslateResult> TranslateBatchAsync(
    IReadOnlyList<TranslateItem> items,
    TranslateOptions options,
    CancellationToken ct)
  {
    var translated = items.Select(x => new TranslatedItem
    {
      Id = x.Id,
      TranslatedText = $"[ZH]{x.Source}"
    }).ToList();

    return Task.FromResult(new TranslateResult
    {
      Items = translated,
      Errors = Array.Empty<ProviderError>()
    });
  }
}


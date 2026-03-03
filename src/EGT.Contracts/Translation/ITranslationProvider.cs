namespace EGT.Contracts.Translation;

public interface ITranslationProvider
{
  string Name { get; }

  Task<TranslateResult> TranslateBatchAsync(
    IReadOnlyList<TranslateItem> items,
    TranslateOptions options,
    CancellationToken ct);
}


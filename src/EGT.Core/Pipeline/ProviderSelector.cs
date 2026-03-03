using EGT.Contracts.Translation;

namespace EGT.Core.Pipeline;

public sealed class ProviderSelector
{
  private readonly IReadOnlyList<ITranslationProvider> _providers;

  public ProviderSelector(IEnumerable<ITranslationProvider> providers)
  {
    _providers = providers.ToList();
  }

  public ITranslationProvider Select(string providerName)
  {
    var provider = _providers.FirstOrDefault(x =>
      string.Equals(x.Name, providerName, StringComparison.OrdinalIgnoreCase));

    if (provider is null)
    {
      throw new InvalidOperationException($"Translation provider not found: {providerName}");
    }

    return provider;
  }
}


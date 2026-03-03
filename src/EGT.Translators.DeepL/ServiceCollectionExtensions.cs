using EGT.Contracts.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace EGT.Translators.DeepL;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddDeepLProvider(this IServiceCollection services)
  {
    services.AddHttpClient("deepl");
    services.AddSingleton<ITranslationProvider, DeepLTranslationProvider>();
    return services;
  }
}


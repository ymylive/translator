using EGT.Contracts.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace EGT.Translators.Microsoft;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddMicrosoftProvider(this IServiceCollection services)
  {
    services.AddHttpClient("microsoft");
    services.AddSingleton<ITranslationProvider, MicrosoftTranslationProvider>();
    return services;
  }
}


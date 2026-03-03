using EGT.Contracts.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace EGT.Translators.Llm;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddLlmProvider(this IServiceCollection services)
  {
    services.AddHttpClient("llm");
    services.AddSingleton<ITranslationProvider, LlmTranslationProvider>();
    return services;
  }
}


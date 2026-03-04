using EGT.Contracts.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace EGT.Translators.Llm;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddLlmProvider(this IServiceCollection services)
  {
    services.AddHttpClient("llm", client =>
    {
      client.Timeout = TimeSpan.FromMinutes(5);
    });
    services.AddSingleton<ITranslationProvider, LlmTranslationProvider>();
    return services;
  }
}

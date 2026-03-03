using EGT.Contracts.Models;
using EGT.Contracts.Translation;
using EGT.Core.Abstractions;
using EGT.Core.Encoding;
using EGT.Core.Manifesting;
using EGT.Core.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace EGT.Core.Pipeline;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddEgtCore(this IServiceCollection services)
  {
    services.AddSingleton<TextFileCodec>();
    services.AddSingleton<PlaceholderProtector>();
    services.AddSingleton<GlossaryLoader>();
    services.AddSingleton<ITranslationCache, SqliteTranslationCache>();
    services.AddSingleton<ITranslationProvider, MockTranslationProvider>();
    services.AddSingleton<IGameProjectResolver, GameProjectResolver>();
    services.AddSingleton<IManifestWriter, ManifestWriter>();
    services.AddSingleton<IRestoreService, RestoreService>();
    services.AddSingleton<ISecretStore, WindowsSecretStore>();
    services.AddSingleton<ProfileSelector>();
    services.AddSingleton<ProviderSelector>();
    services.AddSingleton<ITranslationPipeline, TranslationPipeline>();
    return services;
  }
}

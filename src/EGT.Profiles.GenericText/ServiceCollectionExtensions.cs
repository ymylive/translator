using EGT.Contracts.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace EGT.Profiles.GenericText;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddGenericTextProfile(this IServiceCollection services)
  {
    services.AddSingleton<IProfile, GenericTextProfile>();
    return services;
  }
}


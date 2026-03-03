using EGT.Contracts.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace EGT.Profiles.RenPy;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddRenPyProfile(this IServiceCollection services)
  {
    services.AddSingleton<IProfile, RenPyProfile>();
    return services;
  }
}

using System.Reflection;
using EGT.Contracts.Profiles;
using EGT.Contracts.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EGT.Core.Pipeline;

public static class PluginLoader
{
  public static void Load(IServiceCollection services, string pluginsDirectory, ILogger? logger = null)
  {
    if (!Directory.Exists(pluginsDirectory))
    {
      return;
    }

    foreach (var dll in Directory.EnumerateFiles(pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
    {
      try
      {
        var asm = Assembly.LoadFrom(dll);
        RegisterFromAssembly(services, asm);
        logger?.LogInformation("Loaded plugin assembly: {AssemblyPath}", dll);
      }
      catch (Exception ex)
      {
        logger?.LogWarning(ex, "Failed to load plugin assembly: {AssemblyPath}", dll);
      }
    }
  }

  public static void RegisterFromAssembly(IServiceCollection services, Assembly asm)
  {
    var types = asm.GetExportedTypes().Where(t => t is { IsAbstract: false, IsInterface: false }).ToList();
    foreach (var type in types)
    {
      if (typeof(IProfile).IsAssignableFrom(type))
      {
        services.AddSingleton(typeof(IProfile), type);
      }

      if (typeof(ITranslationProvider).IsAssignableFrom(type))
      {
        services.AddSingleton(typeof(ITranslationProvider), type);
      }
    }
  }
}


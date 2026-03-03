using Avalonia;
using EGT.Profiles.GenericText;
using EGT.Core.Pipeline;
using EGT.Translators.DeepL;
using EGT.Translators.Llm;
using EGT.Translators.Microsoft;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EGT.App;

internal static class Program
{
  public static IHost? Host { get; private set; }

  [STAThread]
  public static void Main(string[] args)
  {
    Host = BuildHost(args);
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
  }

  private static IHost BuildHost(string[] args)
  {
    return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
      .ConfigureAppConfiguration((context, cfg) =>
      {
        cfg.Sources.Clear();
        cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        cfg.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        cfg.AddEnvironmentVariables("EGT_");
      })
      .UseSerilog((context, _, loggerConfig) =>
      {
        loggerConfig.ReadFrom.Configuration(context.Configuration);
      })
      .ConfigureServices((context, services) =>
      {
        services.AddEgtCore();
        services.AddGenericTextProfile();
        services.AddDeepLProvider();
        services.AddMicrosoftProvider();
        services.AddLlmProvider();
        services.AddSingleton<ViewModels.MainWindowViewModel>();

        var pluginsDir = context.Configuration["Plugins:Directory"] ?? "Plugins";
        PluginLoader.Load(services, pluginsDir);
      })
      .Build();
  }

  public static AppBuilder BuildAvaloniaApp()
  {
    return AppBuilder.Configure<App>()
      .UsePlatformDetect()
      .LogToTrace();
  }
}

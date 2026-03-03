using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace EGT.App;

public partial class App : Application
{
  public override void Initialize()
  {
    AvaloniaXamlLoader.Load(this);
  }

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      var vm = Program.Host!.Services.GetService(typeof(ViewModels.MainWindowViewModel)) as ViewModels.MainWindowViewModel
               ?? throw new InvalidOperationException("MainWindowViewModel is not registered.");
      desktop.MainWindow = new Views.MainWindow
      {
        DataContext = vm
      };
    }

    base.OnFrameworkInitializationCompleted();
  }
}


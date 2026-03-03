using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.ComponentModel;
using EGT.App.ViewModels;

namespace EGT.App.Views;

public partial class MainWindow : Window
{
  private MainWindowViewModel? _boundViewModel;

  public MainWindow()
  {
    InitializeComponent();
    DragDrop.SetAllowDrop(DropZone, true);
    DropZone.AddHandler(DragDrop.DragOverEvent, DropZone_OnDragOver);
    DropZone.AddHandler(DragDrop.DropEvent, DropZone_OnDrop);
    DataContextChanged += OnDataContextChanged;
  }

  private void OnDataContextChanged(object? sender, EventArgs e)
  {
    if (_boundViewModel is not null)
    {
      _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    _boundViewModel = DataContext as MainWindowViewModel;
    if (_boundViewModel is not null)
    {
      _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }
  }

  private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (!string.Equals(e.PropertyName, nameof(MainWindowViewModel.Logs), StringComparison.Ordinal))
    {
      return;
    }

    Dispatcher.UIThread.Post(() =>
    {
      var text = LogsTextBox.Text ?? string.Empty;
      LogsTextBox.CaretIndex = text.Length;
      LogsTextBox.SelectionStart = text.Length;
      LogsTextBox.SelectionEnd = text.Length;
    });
  }

  private void DropZone_OnDragOver(object? sender, DragEventArgs e)
  {
    e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    e.Handled = true;
  }

  private async void DropZone_OnPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
    if (clipboard is null || DataContext is not MainWindowViewModel vm)
    {
      return;
    }

    var text = await clipboard.GetTextAsync();
    if (string.IsNullOrWhiteSpace(text))
    {
      return;
    }

    var candidate = text.Trim().Trim('"');
    if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
    {
      vm.SetExePath(candidate);
    }
  }

  private void DropZone_OnDrop(object? sender, DragEventArgs e)
  {
    if (DataContext is not MainWindowViewModel vm)
    {
      return;
    }

    if (!e.Data.Contains(DataFormats.Files))
    {
      return;
    }

    if (e.Data.Get(DataFormats.Files) is not IEnumerable<IStorageItem> files)
    {
      return;
    }

    var file = files?
      .Select(x => x.TryGetLocalPath())
      .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) &&
                           x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    if (!string.IsNullOrWhiteSpace(file))
    {
      vm.SetExePath(file);
    }
  }
}

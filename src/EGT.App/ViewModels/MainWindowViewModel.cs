using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EGT.Contracts.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EGT.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
  private readonly ITranslationPipeline _pipeline;
  private readonly ILogger<MainWindowViewModel> _logger;
  private readonly Queue<string> _logLines = new();
  private const int MaxUiLogLines = 2000;
  private CancellationTokenSource? _cts;

  public MainWindowViewModel(
    ITranslationPipeline pipeline,
    ILogger<MainWindowViewModel> logger,
    IConfiguration configuration)
  {
    _pipeline = pipeline;
    _logger = logger;

    StartCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ExePath));
    CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    OpenOutputCommand = new RelayCommand(OpenOutput, () => !string.IsNullOrWhiteSpace(OutputPath));
    ToggleAdvancedCommand = new RelayCommand(() => ShowAdvancedSettings = !ShowAdvancedSettings);
    LoadDefaults(configuration);
    UpdateProgressCaption("waiting");
    UpdateCurrentFileCaption();
  }

  public string[] ProviderOptions { get; } = new[] { "mock", "deepl", "microsoft", "llm" };

  [ObservableProperty]
  private string exePath = string.Empty;

  [ObservableProperty]
  private string profileName = "generic-text";

  [ObservableProperty]
  private string providerName = "mock";

  [ObservableProperty]
  private string fallbackProviderName = string.Empty;

  [ObservableProperty]
  private string sourceLang = "auto";

  [ObservableProperty]
  private string targetLang = "zh-Hans";

  [ObservableProperty]
  private string glossaryPath = string.Empty;

  [ObservableProperty]
  private string providerApiKey = string.Empty;

  [ObservableProperty]
  private string providerEndpoint = string.Empty;

  [ObservableProperty]
  private string providerModel = string.Empty;

  [ObservableProperty]
  private string providerRegion = string.Empty;

  [ObservableProperty]
  private string fallbackProviderApiKey = string.Empty;

  [ObservableProperty]
  private string fallbackProviderEndpoint = string.Empty;

  [ObservableProperty]
  private string fallbackProviderModel = string.Empty;

  [ObservableProperty]
  private string fallbackProviderRegion = string.Empty;

  [ObservableProperty]
  private bool applyInPlace;

  [ObservableProperty]
  private string maxConcurrency = "4";

  [ObservableProperty]
  private string maxFileSizeMb = "50";

  [ObservableProperty]
  private string maxItemsPerBatch = "40";

  [ObservableProperty]
  private string maxCharsPerBatch = "8000";

  [ObservableProperty]
  private string aiBatchSize = "12";

  [ObservableProperty]
  private bool isBusy;

  [ObservableProperty]
  private bool showAdvancedSettings;

  [ObservableProperty]
  private string advancedToggleText = "Show Advanced Settings";

  [ObservableProperty]
  private double progressValue;

  [ObservableProperty]
  private string statusMessage = "Ready";

  [ObservableProperty]
  private string statusDetail = "Select a game EXE and click Start.";

  [ObservableProperty]
  private bool hasError;

  [ObservableProperty]
  private string errorMessage = string.Empty;

  [ObservableProperty]
  private string statusBorderBrush = "#99CCFBF1";

  [ObservableProperty]
  private string statusBackground = "White";

  [ObservableProperty]
  private string currentFile = "-";

  [ObservableProperty]
  private string progressCaption = "Progress: 0.0% (waiting)";

  [ObservableProperty]
  private string currentFileCaption = "Current file: -";

  [ObservableProperty]
  private string outputPath = string.Empty;

  [ObservableProperty]
  private string logs = string.Empty;

  public IAsyncRelayCommand StartCommand { get; }
  public IRelayCommand CancelCommand { get; }
  public IRelayCommand OpenOutputCommand { get; }
  public IRelayCommand ToggleAdvancedCommand { get; }

  public void SetExePath(string path)
  {
    ExePath = path;
    AppendLog($"EXE selected: {path}");
  }

  private async Task StartAsync()
  {
    if (IsBusy)
    {
      return;
    }

    IsBusy = true;
    ProgressValue = 0;
    CurrentFile = "-";
    SetRunningStatus("Preparing", "Initializing translation task.");
    UpdateProgressCaption("initializing");
    UpdateCurrentFileCaption();
    OutputPath = string.Empty;
    _cts = new CancellationTokenSource();
    NotifyCommandStates();

    var options = new PipelineOptions
    {
      ProfileName = string.IsNullOrWhiteSpace(ProfileName) ? null : ProfileName,
      ProviderName = ProviderName,
      FallbackProviderName = string.IsNullOrWhiteSpace(FallbackProviderName) ? null : FallbackProviderName,
      SourceLang = SourceLang,
      TargetLang = TargetLang,
      ApplyInPlace = ApplyInPlace,
      MaxConcurrency = ParsePositiveInt(MaxConcurrency, 4),
      MaxItemsPerBatch = ParsePositiveInt(MaxItemsPerBatch, 40),
      MaxCharsPerBatch = ParsePositiveInt(MaxCharsPerBatch, 8000),
      AiBatchSize = ParsePositiveInt(AiBatchSize, 12),
      MaxFileSizeMb = ParsePositiveInt(MaxFileSizeMb, 50),
      GlossaryCsvPath = string.IsNullOrWhiteSpace(GlossaryPath) ? null : GlossaryPath,
      ProviderApiKey = string.IsNullOrWhiteSpace(ProviderApiKey) ? null : ProviderApiKey,
      ProviderEndpoint = string.IsNullOrWhiteSpace(ProviderEndpoint) ? null : ProviderEndpoint,
      ProviderModel = string.IsNullOrWhiteSpace(ProviderModel) ? null : ProviderModel,
      ProviderRegion = string.IsNullOrWhiteSpace(ProviderRegion) ? null : ProviderRegion,
      FallbackProviderApiKey = string.IsNullOrWhiteSpace(FallbackProviderApiKey) ? null : FallbackProviderApiKey,
      FallbackProviderEndpoint = string.IsNullOrWhiteSpace(FallbackProviderEndpoint) ? null : FallbackProviderEndpoint,
      FallbackProviderModel = string.IsNullOrWhiteSpace(FallbackProviderModel) ? null : FallbackProviderModel,
      FallbackProviderRegion = string.IsNullOrWhiteSpace(FallbackProviderRegion) ? null : FallbackProviderRegion
    };

    var progress = new Progress<PipelineProgress>(p =>
    {
      var stageLabel = GetStageLabel(p.Stage);
      SetRunningStatus($"Running: {stageLabel}", p.Message);

      if (p.TotalItems > 0)
      {
        ProgressValue = p.ProcessedItems * 100d / p.TotalItems;
        UpdateProgressCaption($"processed {p.ProcessedItems}/{p.TotalItems}");
      }
      else
      {
        UpdateProgressCaption("counting workload");
      }

      if (!string.IsNullOrWhiteSpace(p.CurrentFile))
      {
        CurrentFile = p.CurrentFile;
      }

      UpdateCurrentFileCaption();

      if (p.ProcessedItems % 25 == 0 || p.Stage is "done" or "extract")
      {
        AppendLog($"[{p.Stage}] {p.ProcessedItems}/{Math.Max(1, p.TotalItems)} {p.ItemsPerSecond:F2}/s");
      }
    });

    try
    {
      var result = await _pipeline.RunAsync(ExePath, options, progress, _cts.Token);
      OutputPath = result.OutputRoot;

      if (result.TotalItems > 0)
      {
        ProgressValue = 100;
        UpdateProgressCaption($"processed {result.TotalItems}/{result.TotalItems}");
      }
      else
      {
        UpdateProgressCaption("no translatable entries found");
      }

      if (result.Success)
      {
        SetIdleStatus("Completed", "Translation finished. You can open the output folder.");
      }
      else
      {
        SetIdleStatus("Completed with warnings", "Some entries failed. Check logs for details.");
      }

      AppendLog($"Manifest: {result.ManifestPath}");
      AppendLog($"Stats: total={result.TotalItems}, success={result.SuccessItems}, failed={result.FailedItems}");
      foreach (var warning in result.Warnings.Take(10))
      {
        AppendLog($"warning: {warning}");
      }
    }
    catch (OperationCanceledException)
    {
      SetIdleStatus("Cancelled", "Task cancelled by user.");
      UpdateProgressCaption("cancelled");
      AppendLog("Operation cancelled.");
    }
    catch (Exception ex)
    {
      SetErrorStatus("Failed", "Translation pipeline did not complete.", ex.Message);
      UpdateProgressCaption("failed");
      AppendLog($"error: {ex.Message}");
      _logger.LogError(ex, "UI translation pipeline failed.");
    }
    finally
    {
      IsBusy = false;
      _cts?.Dispose();
      _cts = null;
      NotifyCommandStates();
    }
  }

  private void Cancel()
  {
    _cts?.Cancel();
  }

  private void OpenOutput()
  {
    if (string.IsNullOrWhiteSpace(OutputPath) || !Directory.Exists(OutputPath))
    {
      return;
    }

    Process.Start(new ProcessStartInfo
    {
      FileName = OutputPath,
      UseShellExecute = true
    });
  }

  private void SetIdleStatus(string message, string detail)
  {
    StatusMessage = message;
    StatusDetail = detail;
    HasError = false;
    ErrorMessage = string.Empty;
    StatusBorderBrush = "#99CCFBF1";
    StatusBackground = "White";
  }

  private void SetRunningStatus(string message, string detail)
  {
    StatusMessage = message;
    StatusDetail = detail;
    HasError = false;
    ErrorMessage = string.Empty;
    StatusBorderBrush = "#5EEAD4";
    StatusBackground = "#F0FDFA";
  }

  private void SetErrorStatus(string message, string detail, string errorDetail)
  {
    StatusMessage = message;
    StatusDetail = detail;
    HasError = true;
    ErrorMessage = $"Error detail: {errorDetail}";
    StatusBorderBrush = "#FCA5A5";
    StatusBackground = "#FFF1F2";
  }

  private void UpdateProgressCaption(string tail)
  {
    ProgressCaption = $"Progress: {ProgressValue:F1}% ({tail})";
  }

  private void UpdateCurrentFileCaption()
  {
    CurrentFileCaption = string.IsNullOrWhiteSpace(CurrentFile) || CurrentFile == "-"
      ? "Current file: -"
      : $"Current file: {CurrentFile}";
  }

  private static string GetStageLabel(string stage)
  {
    return stage switch
    {
      "detect-project" => "project detection",
      "extract" => "text extraction",
      "translate" => "translation",
      "apply" => "write output",
      "done" => "finalization",
      _ => "processing"
    };
  }

  private void AppendLog(string line)
  {
    var redacted = RedactSensitive(line);
    if (_logLines.Count >= MaxUiLogLines)
    {
      _logLines.Dequeue();
    }

    _logLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {redacted}");
    Logs = string.Join(Environment.NewLine, _logLines);
  }

  private static string RedactSensitive(string input)
  {
    return input.Replace("apiKey", "apiKey***", StringComparison.OrdinalIgnoreCase);
  }

  private static int ParsePositiveInt(string value, int fallback)
  {
    if (int.TryParse(value, out var parsed) && parsed > 0)
    {
      return parsed;
    }

    return fallback;
  }

  private void LoadDefaults(IConfiguration configuration)
  {
    var defaults = configuration.GetSection("PipelineDefaults");
    ProviderName = defaults["ProviderName"] ?? ProviderName;
    FallbackProviderName = defaults["FallbackProviderName"] ?? FallbackProviderName;
    SourceLang = defaults["SourceLang"] ?? SourceLang;
    TargetLang = defaults["TargetLang"] ?? TargetLang;
    MaxConcurrency = defaults["MaxConcurrency"] ?? MaxConcurrency;
    MaxFileSizeMb = defaults["MaxFileSizeMb"] ?? MaxFileSizeMb;
    MaxItemsPerBatch = defaults["MaxItemsPerBatch"] ?? MaxItemsPerBatch;
    MaxCharsPerBatch = defaults["MaxCharsPerBatch"] ?? MaxCharsPerBatch;
    AiBatchSize = defaults["AiBatchSize"] ?? AiBatchSize;
    ProviderEndpoint = defaults["ProviderEndpoint"] ?? ProviderEndpoint;
    ProviderModel = defaults["ProviderModel"] ?? ProviderModel;
    ProviderRegion = defaults["ProviderRegion"] ?? ProviderRegion;
    ProviderApiKey = defaults["ProviderApiKey"] ?? ProviderApiKey;
    FallbackProviderEndpoint = defaults["FallbackProviderEndpoint"] ?? FallbackProviderEndpoint;
    FallbackProviderModel = defaults["FallbackProviderModel"] ?? FallbackProviderModel;
    FallbackProviderRegion = defaults["FallbackProviderRegion"] ?? FallbackProviderRegion;
    FallbackProviderApiKey = defaults["FallbackProviderApiKey"] ?? FallbackProviderApiKey;
  }

  partial void OnExePathChanged(string value)
  {
    NotifyCommandStates();
  }

  partial void OnIsBusyChanged(bool value)
  {
    NotifyCommandStates();
  }

  partial void OnOutputPathChanged(string value)
  {
    NotifyCommandStates();
  }

  partial void OnShowAdvancedSettingsChanged(bool value)
  {
    AdvancedToggleText = value ? "Hide Advanced Settings" : "Show Advanced Settings";
  }

  private void NotifyCommandStates()
  {
    StartCommand.NotifyCanExecuteChanged();
    CancelCommand.NotifyCanExecuteChanged();
    OpenOutputCommand.NotifyCanExecuteChanged();
  }
}

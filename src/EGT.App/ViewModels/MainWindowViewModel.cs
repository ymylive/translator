using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EGT.Contracts.Models;
using EGT.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EGT.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
  private readonly ITranslationPipeline _pipeline;
  private readonly ISecretStore _secretStore;
  private readonly ILogger<MainWindowViewModel> _logger;
  private readonly Queue<string> _logLines = new();
  private const int MaxUiLogLines = 2000;
  private DateTime _lastProgressLogAtUtc = DateTime.MinValue;
  private int _lastProgressLoggedProcessed = -1;
  private string _lastProgressLoggedStage = string.Empty;
  private string _lastProgressLoggedMessage = string.Empty;
  private readonly string _localSettingsPath;
  private CancellationTokenSource? _cts;

  public MainWindowViewModel(
    ITranslationPipeline pipeline,
    ISecretStore secretStore,
    ILogger<MainWindowViewModel> logger,
    IConfiguration configuration)
  {
    _pipeline = pipeline;
    _secretStore = secretStore;
    _logger = logger;

    StartCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ExePath));
    CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    OpenOutputCommand = new RelayCommand(OpenOutput, () => !string.IsNullOrWhiteSpace(OutputPath));
    OpenQualityReportCommand = new RelayCommand(OpenQualityReport, () => !string.IsNullOrWhiteSpace(QualityReportPath));
    OpenPreviewCommand = new RelayCommand(OpenPreview, () => !string.IsNullOrWhiteSpace(TranslationPreviewPath));
    OpenFailedItemsCommand = new RelayCommand(OpenFailedItems, () => !string.IsNullOrWhiteSpace(FailedItemsPath));
    SaveSettingsCommand = new RelayCommand(() => SaveSettings().GetAwaiter().GetResult(), () => !IsBusy);
    ToggleAdvancedCommand = new RelayCommand(() => ShowAdvancedSettings = !ShowAdvancedSettings);
    _localSettingsPath = ResolveLocalSettingsPath();
    LoadDefaults(configuration);
    ApplyAiPrioritySelectionToEditor();
    UpdateProgressCaption("waiting");
    UpdateCurrentFileCaption();
  }

  public string[] ProviderOptions { get; } = new[] { "mock", "deepl", "microsoft", "llm" };
  public string[] AiPriorityOptions { get; } =
    new[] { "manual", "openai-responses", "openai-chat", "openrouter-responses", "modelscope-chat", "none" };

  [ObservableProperty]
  private string exePath = string.Empty;

  [ObservableProperty]
  private string profileName = "auto";

  [ObservableProperty]
  private string providerName = "mock";

  [ObservableProperty]
  private string fallbackProviderName = string.Empty;

  [ObservableProperty]
  private string aiPriorityPrimary = "openai-responses";

  [ObservableProperty]
  private string aiPrioritySecondary = "manual";

  [ObservableProperty]
  private string aiPriorityTertiary = "none";

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
  private string secondFallbackProviderName = string.Empty;

  [ObservableProperty]
  private string secondFallbackProviderApiKey = string.Empty;

  [ObservableProperty]
  private string secondFallbackProviderEndpoint = string.Empty;

  [ObservableProperty]
  private string secondFallbackProviderModel = string.Empty;

  [ObservableProperty]
  private string secondFallbackProviderRegion = string.Empty;

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
  private string advancedToggleText = "Show advanced settings";

  [ObservableProperty]
  private double progressValue;

  [ObservableProperty]
  private string statusMessage = "Ready";

  [ObservableProperty]
  private string statusDetail = "Choose a game EXE, then click Start.";

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

  [ObservableProperty]
  private string qualitySummary = "No quality report yet.";

  [ObservableProperty]
  private string qualityPreview = "Preview will appear after translation.";

  [ObservableProperty]
  private string qualityReportPath = string.Empty;

  [ObservableProperty]
  private string translationPreviewPath = string.Empty;

  [ObservableProperty]
  private string failedItemsPath = string.Empty;

  public IAsyncRelayCommand StartCommand { get; }
  public IRelayCommand CancelCommand { get; }
  public IRelayCommand OpenOutputCommand { get; }
  public IRelayCommand OpenQualityReportCommand { get; }
  public IRelayCommand OpenPreviewCommand { get; }
  public IRelayCommand OpenFailedItemsCommand { get; }
  public IRelayCommand SaveSettingsCommand { get; }
  public IRelayCommand ToggleAdvancedCommand { get; }

  public void SetExePath(string path)
  {
    ExePath = path;
    AppendLog($"Selected EXE: {path}");
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
    SetRunningStatus("Preparing", "Initializing translation task...");
    UpdateProgressCaption("Initializing");
    UpdateCurrentFileCaption();
    OutputPath = string.Empty;
    QualitySummary = "Running translation, quality report will be generated on completion.";
    QualityPreview = "Waiting for translation results...";
    QualityReportPath = string.Empty;
    TranslationPreviewPath = string.Empty;
    FailedItemsPath = string.Empty;
    _cts = new CancellationTokenSource();
    _lastProgressLogAtUtc = DateTime.MinValue;
    _lastProgressLoggedProcessed = -1;
    _lastProgressLoggedStage = string.Empty;
    _lastProgressLoggedMessage = string.Empty;
    AppendLog("Resume mode enabled: cache entries are reused first.");
    NotifyCommandStates();

    var effectiveProviderName = ProviderName;
    var effectiveFallbackProviderName = string.IsNullOrWhiteSpace(FallbackProviderName) ? null : FallbackProviderName;
    var effectiveSecondFallbackProviderName = string.IsNullOrWhiteSpace(SecondFallbackProviderName) ? null : SecondFallbackProviderName;
    var effectiveProviderEndpoint = string.IsNullOrWhiteSpace(ProviderEndpoint) ? null : ProviderEndpoint;
    var effectiveProviderModel = string.IsNullOrWhiteSpace(ProviderModel) ? null : ProviderModel;
    var effectiveProviderRegion = string.IsNullOrWhiteSpace(ProviderRegion) ? null : ProviderRegion;
    var effectiveFallbackProviderEndpoint = string.IsNullOrWhiteSpace(FallbackProviderEndpoint) ? null : FallbackProviderEndpoint;
    var effectiveFallbackProviderModel = string.IsNullOrWhiteSpace(FallbackProviderModel) ? null : FallbackProviderModel;
    var effectiveFallbackProviderRegion = string.IsNullOrWhiteSpace(FallbackProviderRegion) ? null : FallbackProviderRegion;
    var effectiveSecondFallbackProviderEndpoint = string.IsNullOrWhiteSpace(SecondFallbackProviderEndpoint) ? null : SecondFallbackProviderEndpoint;
    var effectiveSecondFallbackProviderModel = string.IsNullOrWhiteSpace(SecondFallbackProviderModel) ? null : SecondFallbackProviderModel;
    var effectiveSecondFallbackProviderRegion = string.IsNullOrWhiteSpace(SecondFallbackProviderRegion) ? null : SecondFallbackProviderRegion;

    ApplyAiPriorityRouting(
      ref effectiveProviderName,
      ref effectiveFallbackProviderName,
      ref effectiveSecondFallbackProviderName,
      ref effectiveProviderEndpoint,
      ref effectiveProviderModel,
      ref effectiveProviderRegion,
      ref effectiveFallbackProviderEndpoint,
      ref effectiveFallbackProviderModel,
      ref effectiveFallbackProviderRegion,
      ref effectiveSecondFallbackProviderEndpoint,
      ref effectiveSecondFallbackProviderModel,
      ref effectiveSecondFallbackProviderRegion);

    var options = new PipelineOptions
    {
      ProfileName = string.IsNullOrWhiteSpace(ProfileName) ? null : ProfileName,
      ProviderName = effectiveProviderName,
      FallbackProviderName = effectiveFallbackProviderName,
      SecondFallbackProviderName = effectiveSecondFallbackProviderName,
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
      ProviderEndpoint = effectiveProviderEndpoint,
      ProviderModel = effectiveProviderModel,
      ProviderRegion = effectiveProviderRegion,
      FallbackProviderApiKey = string.IsNullOrWhiteSpace(FallbackProviderApiKey) ? null : FallbackProviderApiKey,
      FallbackProviderEndpoint = effectiveFallbackProviderEndpoint,
      FallbackProviderModel = effectiveFallbackProviderModel,
      FallbackProviderRegion = effectiveFallbackProviderRegion,
      SecondFallbackProviderApiKey = string.IsNullOrWhiteSpace(SecondFallbackProviderApiKey) ? null : SecondFallbackProviderApiKey,
      SecondFallbackProviderEndpoint = effectiveSecondFallbackProviderEndpoint,
      SecondFallbackProviderModel = effectiveSecondFallbackProviderModel,
      SecondFallbackProviderRegion = effectiveSecondFallbackProviderRegion
    };

    var progress = new Progress<PipelineProgress>(p =>
    {
      var stageLabel = GetStageLabel(p.Stage);
      SetRunningStatus($"Running: {stageLabel}", p.Message);

      if (p.TotalItems > 0)
      {
        ProgressValue = p.ProcessedItems * 100d / p.TotalItems;
        UpdateProgressCaption($"Processed {p.ProcessedItems}/{p.TotalItems}");
      }
      else
      {
        UpdateProgressCaption("Counting workload");
      }

      if (!string.IsNullOrWhiteSpace(p.CurrentFile))
      {
        CurrentFile = p.CurrentFile;
      }

      UpdateCurrentFileCaption();

      if (ShouldWriteProgressLog(p))
      {
        AppendLog($"[{p.Stage}] {p.ProcessedItems}/{Math.Max(1, p.TotalItems)}, {p.ItemsPerSecond:F2}/s, {p.Message}");
      }
    });

    try
    {
      var result = await _pipeline.RunAsync(ExePath, options, progress, _cts.Token);
      OutputPath = result.OutputRoot;
      QualityReportPath = result.QualityReportPath ?? string.Empty;
      TranslationPreviewPath = result.TranslationPreviewPath ?? string.Empty;
      FailedItemsPath = result.FailedItemsPath ?? string.Empty;
      UpdateQualityPanel(result);

      if (result.TotalItems > 0)
      {
        ProgressValue = 100;
        UpdateProgressCaption($"Processed {result.TotalItems}/{result.TotalItems}");
      }
      else
      {
        UpdateProgressCaption("No translatable entries found");
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
      AppendLog($"Quality summary: {QualitySummary}");
      foreach (var warning in result.Warnings.Take(10))
      {
        AppendLog($"Warning: {warning}");
      }
    }
    catch (OperationCanceledException)
    {
      SetIdleStatus("Cancelled", "Task cancelled by user.");
      UpdateProgressCaption("Cancelled");
      AppendLog("Operation cancelled.");
    }
    catch (Exception ex)
    {
      SetErrorStatus("Failed", "Translation pipeline could not complete.", ex.Message);
      UpdateProgressCaption("Failed");
      AppendLog($"Error: {ex.Message}");
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

  private void OpenQualityReport()
  {
    if (!File.Exists(QualityReportPath))
    {
      return;
    }

    Process.Start(new ProcessStartInfo
    {
      FileName = QualityReportPath,
      UseShellExecute = true
    });
  }

  private void OpenPreview()
  {
    if (!File.Exists(TranslationPreviewPath))
    {
      return;
    }

    Process.Start(new ProcessStartInfo
    {
      FileName = TranslationPreviewPath,
      UseShellExecute = true
    });
  }

  private void OpenFailedItems()
  {
    if (!File.Exists(FailedItemsPath))
    {
      return;
    }

    Process.Start(new ProcessStartInfo
    {
      FileName = FailedItemsPath,
      UseShellExecute = true
    });
  }

  private async Task SaveSettings()
  {
    try
    {
      JsonObject root;
      if (File.Exists(_localSettingsPath))
      {
        var existing = File.ReadAllText(_localSettingsPath);
        root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
      }
      else
      {
        root = new JsonObject();
      }

      var pipelineDefaults = root["PipelineDefaults"] as JsonObject ?? new JsonObject();
      var maxItems = ParsePositiveInt(MaxItemsPerBatch, 40);

      pipelineDefaults["ProviderName"] = ProviderName;
      pipelineDefaults["FallbackProviderName"] = FallbackProviderName;
      pipelineDefaults["SecondFallbackProviderName"] = SecondFallbackProviderName;
      pipelineDefaults["AiPriorityPrimary"] = AiPriorityPrimary;
      pipelineDefaults["AiPrioritySecondary"] = AiPrioritySecondary;
      pipelineDefaults["AiPriorityTertiary"] = AiPriorityTertiary;
      pipelineDefaults["SourceLang"] = SourceLang;
      pipelineDefaults["TargetLang"] = TargetLang;
      pipelineDefaults["MaxConcurrency"] = ParsePositiveInt(MaxConcurrency, 4);
      pipelineDefaults["ChunkSentenceCount"] = maxItems;
      pipelineDefaults["MaxItemsPerBatch"] = maxItems;
      pipelineDefaults["MaxCharsPerBatch"] = ParsePositiveInt(MaxCharsPerBatch, 8000);
      pipelineDefaults["AiBatchSize"] = ParsePositiveInt(AiBatchSize, 12);
      pipelineDefaults["MaxFileSizeMb"] = ParsePositiveInt(MaxFileSizeMb, 50);
      pipelineDefaults["ApplyInPlace"] = ApplyInPlace;
      pipelineDefaults["GlossaryCsvPath"] = string.IsNullOrWhiteSpace(GlossaryPath) ? null : GlossaryPath;
      pipelineDefaults["ProviderEndpoint"] = string.IsNullOrWhiteSpace(ProviderEndpoint) ? null : ProviderEndpoint;
      pipelineDefaults["ProviderModel"] = string.IsNullOrWhiteSpace(ProviderModel) ? null : ProviderModel;
      pipelineDefaults["ProviderRegion"] = string.IsNullOrWhiteSpace(ProviderRegion) ? null : ProviderRegion;
      pipelineDefaults["FallbackProviderEndpoint"] = string.IsNullOrWhiteSpace(FallbackProviderEndpoint) ? null : FallbackProviderEndpoint;
      pipelineDefaults["FallbackProviderModel"] = string.IsNullOrWhiteSpace(FallbackProviderModel) ? null : FallbackProviderModel;
      pipelineDefaults["FallbackProviderRegion"] = string.IsNullOrWhiteSpace(FallbackProviderRegion) ? null : FallbackProviderRegion;
      pipelineDefaults["SecondFallbackProviderEndpoint"] =
        string.IsNullOrWhiteSpace(SecondFallbackProviderEndpoint) ? null : SecondFallbackProviderEndpoint;
      pipelineDefaults["SecondFallbackProviderModel"] =
        string.IsNullOrWhiteSpace(SecondFallbackProviderModel) ? null : SecondFallbackProviderModel;
      pipelineDefaults["SecondFallbackProviderRegion"] =
        string.IsNullOrWhiteSpace(SecondFallbackProviderRegion) ? null : SecondFallbackProviderRegion;

      await SaveProviderSecretAsync(ProviderName, "api-key", ProviderApiKey);
      await SaveProviderSecretAsync(FallbackProviderName, "fallback-api-key", FallbackProviderApiKey);
      await SaveProviderSecretAsync(SecondFallbackProviderName, "second-fallback-api-key", SecondFallbackProviderApiKey);

      root["PipelineDefaults"] = pipelineDefaults;

      Directory.CreateDirectory(Path.GetDirectoryName(_localSettingsPath) ?? Directory.GetCurrentDirectory());
      var json = root.ToJsonString(new JsonSerializerOptions
      {
        WriteIndented = true
      });
      File.WriteAllText(_localSettingsPath, json);
      AppendLog($"Settings saved to: {_localSettingsPath}");
      SetIdleStatus("Settings saved", "Configuration has been persisted.");
    }
    catch (Exception ex)
    {
      AppendLog($"Save settings failed: {ex.Message}");
      SetErrorStatus("Save failed", "Could not persist settings.", ex.Message);
      _logger.LogError(ex, "Save settings failed.");
    }
  }

  private async Task SaveProviderSecretAsync(string? providerName, string suffix, string? apiKey)
  {
    if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(apiKey))
    {
      return;
    }

    var secretKey = $"provider:{providerName}:" + suffix;
    await _secretStore.SaveAsync(secretKey, apiKey, CancellationToken.None);
  }

  private static string ResolveLocalSettingsPath()
  {
    var cwd = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.local.json");
    var baseDir = Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");
    if (File.Exists(cwd))
    {
      return cwd;
    }

    if (File.Exists(baseDir))
    {
      return baseDir;
    }

    if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")))
    {
      return cwd;
    }

    if (File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))
    {
      return baseDir;
    }

    return cwd;
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
    ErrorMessage = $"Error details: {errorDetail}";
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
      "detect-project" => "Project detection",
      "extract" => "Text extraction",
      "translate" => "Translation",
      "apply" => "Writing output",
      "done" => "Completed",
      _ => "Processing"
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

  private bool ShouldWriteProgressLog(PipelineProgress progress)
  {
    var now = DateTime.UtcNow;

    if (progress.Stage != _lastProgressLoggedStage)
    {
      _lastProgressLoggedStage = progress.Stage;
      _lastProgressLoggedProcessed = progress.ProcessedItems;
      _lastProgressLoggedMessage = progress.Message;
      _lastProgressLogAtUtc = now;
      return true;
    }

    if (!string.Equals(progress.Message, _lastProgressLoggedMessage, StringComparison.Ordinal))
    {
      _lastProgressLoggedProcessed = progress.ProcessedItems;
      _lastProgressLoggedMessage = progress.Message;
      _lastProgressLogAtUtc = now;
      return true;
    }

    if (progress.ProcessedItems >= progress.TotalItems && progress.TotalItems > 0)
    {
      _lastProgressLoggedProcessed = progress.ProcessedItems;
      _lastProgressLoggedMessage = progress.Message;
      _lastProgressLogAtUtc = now;
      return true;
    }

    if (progress.ProcessedItems <= 20 && progress.ProcessedItems != _lastProgressLoggedProcessed)
    {
      _lastProgressLoggedProcessed = progress.ProcessedItems;
      _lastProgressLoggedMessage = progress.Message;
      _lastProgressLogAtUtc = now;
      return true;
    }

    if (progress.ProcessedItems != _lastProgressLoggedProcessed &&
        now - _lastProgressLogAtUtc >= TimeSpan.FromSeconds(1))
    {
      _lastProgressLoggedProcessed = progress.ProcessedItems;
      _lastProgressLoggedMessage = progress.Message;
      _lastProgressLogAtUtc = now;
      return true;
    }

    return false;
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

  private void ApplyAiPriorityRouting(
    ref string providerName,
    ref string? fallbackProviderName,
    ref string? secondFallbackProviderName,
    ref string? providerEndpoint,
    ref string? providerModel,
    ref string? providerRegion,
    ref string? fallbackProviderEndpoint,
    ref string? fallbackProviderModel,
    ref string? fallbackProviderRegion,
    ref string? secondFallbackProviderEndpoint,
    ref string? secondFallbackProviderModel,
    ref string? secondFallbackProviderRegion)
  {
    if (!string.Equals(providerName, "llm", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    if (TryResolveAiPreset(AiPriorityPrimary, out var primary))
    {
      providerEndpoint = primary.Endpoint;
      providerModel = primary.Model;
      providerRegion = null;
      AppendLog($"AI priority 1: {AiPriorityPrimary} -> {primary.Endpoint} / {primary.Model}");
    }

    if (TryResolveAiPreset(AiPrioritySecondary, out var secondary))
    {
      fallbackProviderName = "llm";
      fallbackProviderEndpoint = secondary.Endpoint;
      fallbackProviderModel = secondary.Model;
      fallbackProviderRegion = null;
      AppendLog($"AI priority 2: {AiPrioritySecondary} -> {secondary.Endpoint} / {secondary.Model}");
    }
    else if (string.Equals(AiPrioritySecondary, "none", StringComparison.OrdinalIgnoreCase))
    {
      fallbackProviderName = null;
      fallbackProviderEndpoint = null;
      fallbackProviderModel = null;
      fallbackProviderRegion = null;
    }

    if (TryResolveAiPreset(AiPriorityTertiary, out var tertiary))
    {
      secondFallbackProviderName = "llm";
      secondFallbackProviderEndpoint = tertiary.Endpoint;
      secondFallbackProviderModel = tertiary.Model;
      secondFallbackProviderRegion = null;
      AppendLog($"AI priority 3: {AiPriorityTertiary} -> {tertiary.Endpoint} / {tertiary.Model}");
    }
    else if (string.Equals(AiPriorityTertiary, "none", StringComparison.OrdinalIgnoreCase))
    {
      secondFallbackProviderName = null;
      secondFallbackProviderEndpoint = null;
      secondFallbackProviderModel = null;
      secondFallbackProviderRegion = null;
    }
  }

  private void ApplyAiPrioritySelectionToEditor()
  {
    if (!string.Equals(ProviderName, "llm", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    if (TryResolveAiPreset(AiPriorityPrimary, out var primary))
    {
      ProviderEndpoint = primary.Endpoint;
      ProviderModel = primary.Model;
      ProviderRegion = string.Empty;
    }

    if (TryResolveAiPreset(AiPrioritySecondary, out var secondary))
    {
      FallbackProviderName = "llm";
      FallbackProviderEndpoint = secondary.Endpoint;
      FallbackProviderModel = secondary.Model;
      FallbackProviderRegion = string.Empty;
    }
    else if (string.Equals(AiPrioritySecondary, "none", StringComparison.OrdinalIgnoreCase))
    {
      FallbackProviderName = string.Empty;
      FallbackProviderEndpoint = string.Empty;
      FallbackProviderModel = string.Empty;
      FallbackProviderRegion = string.Empty;
    }

    if (TryResolveAiPreset(AiPriorityTertiary, out var tertiary))
    {
      SecondFallbackProviderName = "llm";
      SecondFallbackProviderEndpoint = tertiary.Endpoint;
      SecondFallbackProviderModel = tertiary.Model;
      SecondFallbackProviderRegion = string.Empty;
    }
    else if (string.Equals(AiPriorityTertiary, "none", StringComparison.OrdinalIgnoreCase))
    {
      SecondFallbackProviderName = string.Empty;
      SecondFallbackProviderEndpoint = string.Empty;
      SecondFallbackProviderModel = string.Empty;
      SecondFallbackProviderRegion = string.Empty;
    }
  }

  private static bool TryResolveAiPreset(string key, out AiPreset preset)
  {
    if (string.IsNullOrWhiteSpace(key))
    {
      preset = default;
      return false;
    }

    switch (key.Trim().ToLowerInvariant())
    {
      case "openai-responses":
        preset = new AiPreset("https://gmn.chuangzuoli.com/v1/responses", "gpt-5.2");
        return true;
      case "openai-chat":
        preset = new AiPreset("https://api.openai.com/v1/chat/completions", "gpt-4o-mini");
        return true;
      case "openrouter-responses":
        preset = new AiPreset("https://openrouter.ai/api/v1/responses", "z-ai/glm-4.5-air:free");
        return true;
      case "modelscope-chat":
        preset = new AiPreset("https://api-inference.modelscope.cn/v1/chat/completions", "ZhipuAI/GLM-5");
        return true;
      default:
        preset = default;
        return false;
    }
  }

  private readonly record struct AiPreset(string Endpoint, string Model);

  private void UpdateQualityPanel(TranslationRunResult result)
  {
    QualitySummary =
      $"鍞竴婧愭枃鏈?{result.UniqueSourceCount}锝滅紦瀛樺懡涓?{result.CacheHits}锝滄湳璇懡涓?{result.GlossaryHits}锝滃け璐?{result.FailedUniqueSources}锝滅枒浼兼湭缈昏瘧 {result.IdentityCount}锝滈暱搴︽瘮 {result.AverageLengthRatio:F2}";

    if (string.IsNullOrWhiteSpace(result.QualityReportPath) || !File.Exists(result.QualityReportPath))
    {
      QualityPreview = "Quality report not generated.";
      return;
    }

    try
    {
      using var doc = JsonDocument.Parse(File.ReadAllText(result.QualityReportPath));
      if (!doc.RootElement.TryGetProperty("Preview", out var previewNode) || previewNode.ValueKind != JsonValueKind.Array)
      {
        QualityPreview = "Quality report contains no preview data.";
        return;
      }

      var lines = new List<string>();
      foreach (var item in previewNode.EnumerateArray().Take(8))
      {
        var source = item.TryGetProperty("Source", out var sourceNode) ? sourceNode.GetString() ?? string.Empty : string.Empty;
        var translated = item.TryGetProperty("Translated", out var targetNode) ? targetNode.GetString() ?? string.Empty : string.Empty;
        var isIdentity = item.TryGetProperty("IsIdentity", out var identityNode) &&
                         identityNode.ValueKind == JsonValueKind.True;
        var tag = isIdentity ? "identity" : "translated";
        lines.Add($"[{tag}] {source} => {translated}");
      }

      QualityPreview = lines.Count > 0
        ? string.Join(Environment.NewLine, lines)
        : "Quality report has no preview rows.";
    }
    catch (Exception ex)
    {
      QualityPreview = $"Failed to parse quality report: {ex.Message}";
    }
  }

  private void LoadDefaults(IConfiguration configuration)
  {
    var defaults = configuration.GetSection("PipelineDefaults");
    ProviderName = defaults["ProviderName"] ?? ProviderName;
    FallbackProviderName = defaults["FallbackProviderName"] ?? FallbackProviderName;
    SecondFallbackProviderName = defaults["SecondFallbackProviderName"] ?? SecondFallbackProviderName;
    AiPriorityPrimary = defaults["AiPriorityPrimary"] ?? AiPriorityPrimary;
    AiPrioritySecondary = defaults["AiPrioritySecondary"] ?? AiPrioritySecondary;
    AiPriorityTertiary = defaults["AiPriorityTertiary"] ?? AiPriorityTertiary;
    SourceLang = defaults["SourceLang"] ?? SourceLang;
    TargetLang = defaults["TargetLang"] ?? TargetLang;
    MaxConcurrency = defaults["MaxConcurrency"] ?? MaxConcurrency;
    MaxFileSizeMb = defaults["MaxFileSizeMb"] ?? MaxFileSizeMb;
    MaxItemsPerBatch = defaults["ChunkSentenceCount"] ?? defaults["MaxItemsPerBatch"] ?? MaxItemsPerBatch;
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
    SecondFallbackProviderEndpoint = defaults["SecondFallbackProviderEndpoint"] ?? SecondFallbackProviderEndpoint;
    SecondFallbackProviderModel = defaults["SecondFallbackProviderModel"] ?? SecondFallbackProviderModel;
    SecondFallbackProviderRegion = defaults["SecondFallbackProviderRegion"] ?? SecondFallbackProviderRegion;
    SecondFallbackProviderApiKey = defaults["SecondFallbackProviderApiKey"] ?? SecondFallbackProviderApiKey;
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

  partial void OnProviderNameChanged(string value)
  {
    if (string.Equals(value, "llm", StringComparison.OrdinalIgnoreCase))
    {
      ApplyAiPrioritySelectionToEditor();
    }
  }

  partial void OnAiPriorityPrimaryChanged(string value)
  {
    ApplyAiPrioritySelectionToEditor();
  }

  partial void OnAiPrioritySecondaryChanged(string value)
  {
    ApplyAiPrioritySelectionToEditor();
  }

  partial void OnAiPriorityTertiaryChanged(string value)
  {
    ApplyAiPrioritySelectionToEditor();
  }

  partial void OnQualityReportPathChanged(string value)
  {
    NotifyCommandStates();
  }

  partial void OnTranslationPreviewPathChanged(string value)
  {
    NotifyCommandStates();
  }

  partial void OnFailedItemsPathChanged(string value)
  {
    NotifyCommandStates();
  }

  partial void OnShowAdvancedSettingsChanged(bool value)
  {
    AdvancedToggleText = value ? "Hide advanced settings" : "Show advanced settings";
  }
  private void NotifyCommandStates()
  {
    StartCommand.NotifyCanExecuteChanged();
    CancelCommand.NotifyCanExecuteChanged();
    OpenOutputCommand.NotifyCanExecuteChanged();
    OpenQualityReportCommand.NotifyCanExecuteChanged();
    OpenPreviewCommand.NotifyCanExecuteChanged();
    OpenFailedItemsCommand.NotifyCanExecuteChanged();
    SaveSettingsCommand.NotifyCanExecuteChanged();
  }
}











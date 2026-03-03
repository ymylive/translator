using System.Diagnostics;
using System.Text.Json;
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
  private DateTime _lastProgressLogAtUtc = DateTime.MinValue;
  private int _lastProgressLoggedProcessed = -1;
  private string _lastProgressLoggedStage = string.Empty;
  private string _lastProgressLoggedMessage = string.Empty;
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
    OpenQualityReportCommand = new RelayCommand(OpenQualityReport, () => !string.IsNullOrWhiteSpace(QualityReportPath));
    OpenPreviewCommand = new RelayCommand(OpenPreview, () => !string.IsNullOrWhiteSpace(TranslationPreviewPath));
    OpenFailedItemsCommand = new RelayCommand(OpenFailedItems, () => !string.IsNullOrWhiteSpace(FailedItemsPath));
    ToggleAdvancedCommand = new RelayCommand(() => ShowAdvancedSettings = !ShowAdvancedSettings);
    LoadDefaults(configuration);
    UpdateProgressCaption("waiting");
    UpdateCurrentFileCaption();
  }

  public string[] ProviderOptions { get; } = new[] { "mock", "deepl", "microsoft", "llm" };

  [ObservableProperty]
  private string exePath = string.Empty;

  [ObservableProperty]
  private string profileName = "auto";

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
  private string advancedToggleText = "显示高级设置";

  [ObservableProperty]
  private double progressValue;

  [ObservableProperty]
  private string statusMessage = "就绪";

  [ObservableProperty]
  private string statusDetail = "请选择游戏 EXE，然后点击开始。";

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
  private string progressCaption = "进度：0.0%（等待中）";

  [ObservableProperty]
  private string currentFileCaption = "当前文件：-";

  [ObservableProperty]
  private string outputPath = string.Empty;

  [ObservableProperty]
  private string logs = string.Empty;

  [ObservableProperty]
  private string qualitySummary = "暂无质量报告";

  [ObservableProperty]
  private string qualityPreview = "执行后会显示翻译样例预览。";

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
  public IRelayCommand ToggleAdvancedCommand { get; }

  public void SetExePath(string path)
  {
    ExePath = path;
    AppendLog($"已选择 EXE：{path}");
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
    SetRunningStatus("准备中", "正在初始化翻译任务。");
    UpdateProgressCaption("初始化中");
    UpdateCurrentFileCaption();
    OutputPath = string.Empty;
    QualitySummary = "正在执行翻译，完成后生成质量报告…";
    QualityPreview = "正在等待翻译结果…";
    QualityReportPath = string.Empty;
    TranslationPreviewPath = string.Empty;
    FailedItemsPath = string.Empty;
    _cts = new CancellationTokenSource();
    _lastProgressLogAtUtc = DateTime.MinValue;
    _lastProgressLoggedProcessed = -1;
    _lastProgressLoggedStage = string.Empty;
    _lastProgressLoggedMessage = string.Empty;
    AppendLog("断点续传已启用：重新运行会优先命中本地翻译缓存。");
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
      SetRunningStatus($"执行中：{stageLabel}", p.Message);

      if (p.TotalItems > 0)
      {
        ProgressValue = p.ProcessedItems * 100d / p.TotalItems;
        UpdateProgressCaption($"已处理 {p.ProcessedItems}/{p.TotalItems}");
      }
      else
      {
        UpdateProgressCaption("正在统计工作量");
      }

      if (!string.IsNullOrWhiteSpace(p.CurrentFile))
      {
        CurrentFile = p.CurrentFile;
      }

      UpdateCurrentFileCaption();

      if (ShouldWriteProgressLog(p))
      {
        AppendLog($"[{p.Stage}] {p.ProcessedItems}/{Math.Max(1, p.TotalItems)}，速率 {p.ItemsPerSecond:F2}/s，{p.Message}");
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
        UpdateProgressCaption($"已处理 {result.TotalItems}/{result.TotalItems}");
      }
      else
      {
        UpdateProgressCaption("未发现可翻译条目");
      }

      if (result.Success)
      {
        SetIdleStatus("已完成", "翻译已完成，可打开输出目录查看。");
      }
      else
      {
        SetIdleStatus("完成（有警告）", "部分条目失败，请查看日志详情。");
      }

      AppendLog($"Manifest：{result.ManifestPath}");
      AppendLog($"统计：总数={result.TotalItems}，成功={result.SuccessItems}，失败={result.FailedItems}");
      AppendLog($"质量摘要：{QualitySummary}");
      foreach (var warning in result.Warnings.Take(10))
      {
        AppendLog($"警告：{warning}");
      }
    }
    catch (OperationCanceledException)
    {
      SetIdleStatus("已取消", "任务已由用户取消。");
      UpdateProgressCaption("已取消");
      AppendLog("操作已取消。");
    }
    catch (Exception ex)
    {
      SetErrorStatus("失败", "翻译流程未能完成。", ex.Message);
      UpdateProgressCaption("失败");
      AppendLog($"错误：{ex.Message}");
      _logger.LogError(ex, "UI 翻译流程执行失败。");
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
    ErrorMessage = $"错误详情：{errorDetail}";
    StatusBorderBrush = "#FCA5A5";
    StatusBackground = "#FFF1F2";
  }

  private void UpdateProgressCaption(string tail)
  {
    ProgressCaption = $"进度：{ProgressValue:F1}%（{tail}）";
  }

  private void UpdateCurrentFileCaption()
  {
    CurrentFileCaption = string.IsNullOrWhiteSpace(CurrentFile) || CurrentFile == "-"
      ? "当前文件：-"
      : $"当前文件：{CurrentFile}";
  }

  private static string GetStageLabel(string stage)
  {
    return stage switch
    {
      "detect-project" => "项目识别",
      "extract" => "文本抽取",
      "translate" => "文本翻译",
      "apply" => "写入输出",
      "done" => "收尾完成",
      _ => "处理中"
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

  private void UpdateQualityPanel(TranslationRunResult result)
  {
    QualitySummary =
      $"唯一源文本 {result.UniqueSourceCount}｜缓存命中 {result.CacheHits}｜术语命中 {result.GlossaryHits}｜失败 {result.FailedUniqueSources}｜疑似未翻译 {result.IdentityCount}｜长度比 {result.AverageLengthRatio:F2}";

    if (string.IsNullOrWhiteSpace(result.QualityReportPath) || !File.Exists(result.QualityReportPath))
    {
      QualityPreview = "未生成质量预览。";
      return;
    }

    try
    {
      using var doc = JsonDocument.Parse(File.ReadAllText(result.QualityReportPath));
      if (!doc.RootElement.TryGetProperty("Preview", out var previewNode) || previewNode.ValueKind != JsonValueKind.Array)
      {
        QualityPreview = "质量报告无预览数据。";
        return;
      }

      var lines = new List<string>();
      foreach (var item in previewNode.EnumerateArray().Take(8))
      {
        var source = item.TryGetProperty("Source", out var sourceNode) ? sourceNode.GetString() ?? string.Empty : string.Empty;
        var translated = item.TryGetProperty("Translated", out var targetNode) ? targetNode.GetString() ?? string.Empty : string.Empty;
        var isIdentity = item.TryGetProperty("IsIdentity", out var identityNode) &&
                         identityNode.ValueKind == JsonValueKind.True;
        var tag = isIdentity ? "未变更" : "已翻译";
        lines.Add($"[{tag}] {source} => {translated}");
      }

      QualityPreview = lines.Count > 0
        ? string.Join(Environment.NewLine, lines)
        : "质量报告没有可展示样例。";
    }
    catch (Exception ex)
    {
      QualityPreview = $"质量报告解析失败：{ex.Message}";
    }
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
    AdvancedToggleText = value ? "隐藏高级设置" : "显示高级设置";
  }

  private void NotifyCommandStates()
  {
    StartCommand.NotifyCanExecuteChanged();
    CancelCommand.NotifyCanExecuteChanged();
    OpenOutputCommand.NotifyCanExecuteChanged();
    OpenQualityReportCommand.NotifyCanExecuteChanged();
    OpenPreviewCommand.NotifyCanExecuteChanged();
    OpenFailedItemsCommand.NotifyCanExecuteChanged();
  }
}

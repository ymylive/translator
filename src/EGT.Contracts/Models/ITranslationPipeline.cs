namespace EGT.Contracts.Models;

public interface ITranslationPipeline
{
  Task<TranslationRunResult> RunAsync(
    string exePath,
    PipelineOptions options,
    IProgress<PipelineProgress>? progress,
    CancellationToken ct);

  Task RestoreAsync(string manifestPath, CancellationToken ct);
}


namespace EGT.Contracts.Models;

public sealed record PipelineProgress(
  string Stage,
  int ProcessedItems,
  int TotalItems,
  string? CurrentFile,
  double ItemsPerSecond,
  string Message);


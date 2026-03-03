namespace EGT.Contracts.Models;

public sealed class TranslationRunResult
{
  public required bool Success { get; init; }
  public required string ManifestPath { get; init; }
  public required string OutputRoot { get; init; }
  public required int TotalItems { get; init; }
  public required int SuccessItems { get; init; }
  public required int FailedItems { get; init; }
  public required IReadOnlyList<string> Warnings { get; init; }
  public string? QualityReportPath { get; init; }
  public string? TranslationPreviewPath { get; init; }
  public string? FailedItemsPath { get; init; }
  public string? CacheFilePath { get; init; }
  public int UniqueSourceCount { get; init; }
  public int CacheHits { get; init; }
  public int GlossaryHits { get; init; }
  public int FailedUniqueSources { get; init; }
  public int IdentityCount { get; init; }
  public double AverageLengthRatio { get; init; }
}

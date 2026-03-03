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
}


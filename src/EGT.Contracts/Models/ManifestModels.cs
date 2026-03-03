namespace EGT.Contracts.Models;

public sealed class TranslationManifest
{
  public string ManifestVersion { get; init; } = "1.0.0";
  public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
  public required ManifestProjectInfo Project { get; init; }
  public required ManifestRunInfo Run { get; init; }
  public required ManifestStatistics Statistics { get; init; }
  public required IReadOnlyList<ManifestFileChange> Files { get; init; }
  public required ManifestRestoreInfo Restore { get; init; }
}

public sealed class ManifestProjectInfo
{
  public required string GameName { get; init; }
  public required string ExePath { get; init; }
  public required string RootPath { get; init; }
  public required string Profile { get; init; }
  public required string Provider { get; init; }
  public required string OptionsHash { get; init; }
}

public sealed class ManifestRunInfo
{
  public required string OutputRoot { get; init; }
  public required string BackupRoot { get; init; }
  public required string RunId { get; init; }
  public required bool AppliedInPlace { get; init; }
}

public sealed class ManifestStatistics
{
  public required int TotalEntries { get; init; }
  public required int Succeeded { get; init; }
  public required int Failed { get; init; }
  public required int Skipped { get; init; }
}

public sealed class ManifestFileChange
{
  public required string OriginalPath { get; init; }
  public required string OutputPath { get; init; }
  public string? AppliedPath { get; init; }
  public string? BackupPath { get; init; }
  public required string OriginalSha256 { get; init; }
  public required string OutputSha256 { get; init; }
  public required string Encoding { get; init; }
}

public sealed class ManifestRestoreInfo
{
  public required bool CanRestore { get; init; }
  public required IReadOnlyList<ManifestRestoreItem> Items { get; init; }
}

public sealed class ManifestRestoreItem
{
  public required string TargetPath { get; init; }
  public required string BackupPath { get; init; }
  public required string BackupSha256 { get; init; }
}


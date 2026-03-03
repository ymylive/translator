using System.Text.Json;
using EGT.Contracts.Models;
using EGT.Core.Abstractions;

namespace EGT.Core.Manifesting;

public sealed class RestoreService : IRestoreService
{
  public async Task RestoreAsync(string manifestPath, CancellationToken ct)
  {
    if (!File.Exists(manifestPath))
    {
      throw new FileNotFoundException($"Manifest not found: {manifestPath}");
    }

    var json = await File.ReadAllTextAsync(manifestPath, ct);
    var manifest = JsonSerializer.Deserialize<TranslationManifest>(json)
      ?? throw new InvalidOperationException("Manifest parse failed.");

    if (!manifest.Restore.CanRestore)
    {
      throw new InvalidOperationException("Manifest indicates restore is unavailable.");
    }

    foreach (var item in manifest.Restore.Items)
    {
      ct.ThrowIfCancellationRequested();
      if (!File.Exists(item.BackupPath))
      {
        throw new FileNotFoundException($"Backup file missing: {item.BackupPath}");
      }

      var actualHash = Hashing.FileSha256(item.BackupPath);
      if (!string.Equals(actualHash, item.BackupSha256, StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidOperationException($"Backup hash mismatch: {item.BackupPath}");
      }

      Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);
      File.Copy(item.BackupPath, item.TargetPath, overwrite: true);
    }
  }
}


using System.Text.Json;
using EGT.Contracts.Models;
using EGT.Contracts.Profiles;
using EGT.Core.Abstractions;
using EGT.Core.Encoding;

namespace EGT.Core.Manifesting;

public sealed class ManifestWriter : IManifestWriter
{
  private readonly TextFileCodec _codec;

  public ManifestWriter(TextFileCodec codec)
  {
    _codec = codec;
  }

  public async Task<string> WriteAsync(
    GameProject project,
    PipelineOptions options,
    string runId,
    ProfileApplyResult applyResult,
    ManifestStatistics statistics,
    string outputRoot,
    string backupRoot,
    CancellationToken ct)
  {
    Directory.CreateDirectory(outputRoot);
    if (options.ApplyInPlace)
    {
      Directory.CreateDirectory(backupRoot);
    }

    var changes = new List<ManifestFileChange>();
    var restoreItems = new List<ManifestRestoreItem>();

    foreach (var file in applyResult.Files)
    {
      ct.ThrowIfCancellationRequested();

      var originalPath = file.OriginalAbsolutePath;
      var outputPath = Path.Combine(outputRoot, file.RelativePath);
      var outputDirectory = Path.GetDirectoryName(outputPath)!;
      Directory.CreateDirectory(outputDirectory);
      _codec.Write(outputPath, file.OutputContent, file.EncodingName);

      string? appliedPath = null;
      string? backupPath = null;

      if (options.ApplyInPlace)
      {
        appliedPath = originalPath;
        backupPath = Path.Combine(backupRoot, file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(originalPath, backupPath, overwrite: true);
        _codec.Write(originalPath, file.OutputContent, file.EncodingName);
      }

      var originalHash = Hashing.FileSha256(options.ApplyInPlace ? backupPath! : originalPath);
      var outputHash = Hashing.FileSha256(outputPath);
      changes.Add(new ManifestFileChange
      {
        OriginalPath = originalPath,
        OutputPath = outputPath,
        AppliedPath = appliedPath,
        BackupPath = backupPath,
        OriginalSha256 = originalHash,
        OutputSha256 = outputHash,
        Encoding = file.EncodingName
      });

      if (!string.IsNullOrWhiteSpace(backupPath))
      {
        restoreItems.Add(new ManifestRestoreItem
        {
          TargetPath = originalPath,
          BackupPath = backupPath,
          BackupSha256 = Hashing.FileSha256(backupPath)
        });
      }
    }

    var manifest = new TranslationManifest
    {
      Project = new ManifestProjectInfo
      {
        GameName = project.Name,
        ExePath = project.ExePath,
        RootPath = project.RootPath,
        Profile = options.ProfileName ?? "auto",
        Provider = options.ProviderName,
        OptionsHash = Hashing.OptionsHash(options)
      },
      Run = new ManifestRunInfo
      {
        RunId = runId,
        OutputRoot = outputRoot,
        BackupRoot = backupRoot,
        AppliedInPlace = options.ApplyInPlace
      },
      Statistics = statistics,
      Files = changes,
      Restore = new ManifestRestoreInfo
      {
        CanRestore = options.ApplyInPlace,
        Items = restoreItems
      }
    };

    var manifestPath = Path.Combine(outputRoot, "manifest.json");
    var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
    {
      WriteIndented = true
    });
    await File.WriteAllTextAsync(manifestPath, json, ct);
    return manifestPath;
  }
}


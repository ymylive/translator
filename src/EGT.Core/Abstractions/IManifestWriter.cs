using EGT.Contracts.Models;
using EGT.Contracts.Profiles;

namespace EGT.Core.Abstractions;

public interface IManifestWriter
{
  Task<string> WriteAsync(
    GameProject project,
    PipelineOptions options,
    string runId,
    ProfileApplyResult applyResult,
    ManifestStatistics statistics,
    string outputRoot,
    string backupRoot,
    CancellationToken ct);
}


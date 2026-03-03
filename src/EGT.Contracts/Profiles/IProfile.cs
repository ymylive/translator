using EGT.Contracts.Models;

namespace EGT.Contracts.Profiles;

public interface IProfile
{
  string Name { get; }
  ProfileCapability Capability { get; }
  bool Supports(GameProject project);

  Task<ProfileExtractionResult> ExtractAsync(
    GameProject project,
    PipelineOptions options,
    CancellationToken ct);

  Task<ProfileApplyResult> ApplyAsync(
    GameProject project,
    ProfileExtractionResult extraction,
    IReadOnlyDictionary<string, string> translatedEntries,
    PipelineOptions options,
    CancellationToken ct);
}


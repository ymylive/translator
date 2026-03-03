namespace EGT.Contracts.Profiles;

public sealed class ProfileCapability
{
  public string Version { get; init; } = "1.0.0";
  public required IReadOnlyList<string> SupportedExtensions { get; init; }
  public required IReadOnlyList<string> EngineHints { get; init; }
  public int Priority { get; init; } = 0;
}

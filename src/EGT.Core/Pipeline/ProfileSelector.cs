using EGT.Contracts.Models;
using EGT.Contracts.Profiles;

namespace EGT.Core.Pipeline;

public sealed class ProfileSelector
{
  private readonly IReadOnlyList<IProfile> _profiles;

  public ProfileSelector(IEnumerable<IProfile> profiles)
  {
    _profiles = profiles.ToList();
  }

  public IProfile Select(GameProject project, PipelineOptions options)
  {
    if (_profiles.Count == 0)
    {
      throw new InvalidOperationException("No profiles are registered.");
    }

    if (!string.IsNullOrWhiteSpace(options.ProfileName) &&
        !string.Equals(options.ProfileName, "auto", StringComparison.OrdinalIgnoreCase))
    {
      var selected = _profiles.FirstOrDefault(x =>
        string.Equals(x.Name, options.ProfileName, StringComparison.OrdinalIgnoreCase));
      if (selected is null)
      {
        throw new InvalidOperationException($"Profile not found: {options.ProfileName}");
      }

      return selected;
    }

    var candidate = _profiles
      .Where(x => x.Supports(project))
      .OrderByDescending(x => x.Capability.Priority)
      .FirstOrDefault();

    return candidate ?? _profiles.OrderByDescending(x => x.Capability.Priority).First();
  }
}

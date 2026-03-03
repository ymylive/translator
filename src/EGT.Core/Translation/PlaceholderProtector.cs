using System.Text.RegularExpressions;
using EGT.Contracts.Translation;

namespace EGT.Core.Translation;

public sealed class PlaceholderProtector
{
  private static readonly Regex PlaceholderRegex = new(
    @"(\{[0-9]+\}|%[sdif]|<[^>]+>|\\n|\\t|\$\{[^}]+\})",
    RegexOptions.Compiled);

  public (string ProtectedText, PlaceholderMap Map) Protect(string source)
  {
    var map = new Dictionary<string, string>();
    var index = 0;

    var protectedText = PlaceholderRegex.Replace(source, match =>
    {
      var key = $"__PH_{index}__";
      map[key] = match.Value;
      index++;
      return key;
    });

    return (protectedText, new PlaceholderMap(map));
  }

  public string Restore(string translated, PlaceholderMap map)
  {
    var result = translated;
    foreach (var pair in map.Values)
    {
      result = result.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
    }

    return result;
  }
}


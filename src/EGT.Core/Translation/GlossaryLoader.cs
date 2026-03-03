using System.Text;
using EGT.Contracts.Translation;

namespace EGT.Core.Translation;

public sealed class GlossaryLoader
{
  public async Task<Glossary?> LoadAsync(string? csvPath, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
    {
      return null;
    }

    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    var lines = await File.ReadAllLinesAsync(csvPath, ct);
    foreach (var raw in lines)
    {
      if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith('#'))
      {
        continue;
      }

      var parts = SplitCsvLine(raw);
      if (parts.Count < 2)
      {
        continue;
      }

      var src = parts[0].Trim();
      var dst = parts[1].Trim();
      if (!string.IsNullOrEmpty(src))
      {
        map[src] = dst;
      }
    }

    var version = map.Count == 0
      ? "empty"
      : Manifesting.Hashing.Sha256(string.Join('\n', map.OrderBy(x => x.Key).Select(x => $"{x.Key}=>{x.Value}")));

    return new Glossary
    {
      Version = version,
      Entries = map
    };
  }

  private static List<string> SplitCsvLine(string line)
  {
    var values = new List<string>();
    var sb = new StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < line.Length; i++)
    {
      var ch = line[i];
      if (ch == '"')
      {
        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
        {
          sb.Append('"');
          i++;
        }
        else
        {
          inQuotes = !inQuotes;
        }

        continue;
      }

      if (ch == ',' && !inQuotes)
      {
        values.Add(sb.ToString());
        sb.Clear();
        continue;
      }

      sb.Append(ch);
    }

    values.Add(sb.ToString());
    return values;
  }
}


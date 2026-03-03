using EGT.Contracts.Models;
using EGT.Core.Abstractions;

namespace EGT.Core.Pipeline;

public sealed class GameProjectResolver : IGameProjectResolver
{
  private static readonly string[] LocalizationHints =
    new[] { "Localization", "locales", "lang", "i18n", "language", "translations" };

  public GameProject Resolve(string exePath)
  {
    if (string.IsNullOrWhiteSpace(exePath))
    {
      throw new ArgumentException("exePath is required.", nameof(exePath));
    }

    if (!File.Exists(exePath))
    {
      throw new FileNotFoundException($"Game executable not found: {exePath}");
    }

    var rootPath = Path.GetDirectoryName(exePath)!;
    var name = Path.GetFileNameWithoutExtension(exePath);
    var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var hint in LocalizationHints)
    {
      var path = Path.Combine(rootPath, hint);
      if (Directory.Exists(path))
      {
        hints[hint] = path;
      }
    }

    hints["exeDirectory"] = rootPath;

    return new GameProject(name, exePath, rootPath, hints);
  }
}


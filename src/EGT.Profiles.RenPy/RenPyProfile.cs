using System.Text;
using System.Text.RegularExpressions;
using EGT.Contracts.Models;
using EGT.Contracts.Profiles;
using EGT.Core.Encoding;
using EGT.Core.Manifesting;
using Microsoft.Extensions.Logging;

namespace EGT.Profiles.RenPy;

public sealed class RenPyProfile : IProfile
{
  private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".rpy", ".rpym"
  };

  private static readonly string[] IgnoredPathMarkers =
  {
    "/renpy/", "/lib/", "/cache/", "/__pycache__/", "/.git/", "/saves/"
  };

  private static readonly Regex QuoteRegex = new(
    "\"(?<value>(?:[^\"\\\\\\r\\n]|\\\\.)*)\"|'(?<single>(?:[^'\\\\\\r\\n]|\\\\.)*)'",
    RegexOptions.Compiled);

  private static readonly Regex CharacterSayRegex = new(
    @"^[A-Za-z_][A-Za-z0-9_\.]*\s+[""']",
    RegexOptions.Compiled);

  private static readonly Regex OldNewRegex = new(
    @"^(old|new)\s+[""']",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

  private static readonly Regex IdentifierRegex = new(
    @"^[A-Za-z_][A-Za-z0-9_]*$",
    RegexOptions.Compiled);

  private static readonly Regex LetterRegex = new(@"\p{L}", RegexOptions.Compiled);

  private static readonly Regex AssetPathRegex = new(
    @"\.(png|jpg|jpeg|webp|gif|bmp|mp3|ogg|wav|ttf|otf|rpy|rpym|zip|json)\b",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

  private readonly TextFileCodec _codec;
  private readonly ILogger<RenPyProfile> _logger;

  public RenPyProfile(TextFileCodec codec, ILogger<RenPyProfile> logger)
  {
    _codec = codec;
    _logger = logger;
  }

  public string Name => "renpy";

  public ProfileCapability Capability { get; } = new()
  {
    Version = "1.0.0",
    Priority = 80,
    SupportedExtensions = SupportedExtensions.ToArray(),
    EngineHints = new[] { "renpy", "visual-novel" }
  };

  public bool Supports(GameProject project)
  {
    try
    {
      var renpyFolder = Path.Combine(project.RootPath, "renpy");
      if (Directory.Exists(renpyFolder))
      {
        return true;
      }

      var gameFolder = Path.Combine(project.RootPath, "game");
      if (Directory.Exists(gameFolder) &&
          Directory.EnumerateFiles(gameFolder, "*.rpy", SearchOption.AllDirectories).Take(1).Any())
      {
        return true;
      }

      return Directory.EnumerateFiles(project.RootPath, "*.rpy", SearchOption.TopDirectoryOnly).Take(1).Any();
    }
    catch
    {
      return false;
    }
  }

  public Task<ProfileExtractionResult> ExtractAsync(
    GameProject project,
    PipelineOptions options,
    CancellationToken ct)
  {
    var files = new List<ExtractedFile>();
    var entries = new List<ExtractedEntry>();

    foreach (var filePath in EnumerateCandidateFiles(project.RootPath, options))
    {
      ct.ThrowIfCancellationRequested();
      var read = _codec.Read(filePath);
      var relative = Path.GetRelativePath(project.RootPath, filePath);
      files.Add(new ExtractedFile
      {
        AbsolutePath = filePath,
        RelativePath = relative,
        Content = read.Content,
        EncodingName = read.EncodingName
      });

      foreach (var segment in ExtractSegments(read.Content))
      {
        var id = Hashing.Sha256($"{relative}|{segment.Start}|{segment.Length}|{segment.SourceText}");
        entries.Add(new ExtractedEntry
        {
          Id = id,
          RelativePath = relative,
          Start = segment.Start,
          Length = segment.Length,
          SourceText = segment.SourceText,
          Context = segment.Context
        });
      }
    }

    _logger.LogInformation("RenPy profile extracted {Entries} entries from {Files} files.", entries.Count, files.Count);
    return Task.FromResult(new ProfileExtractionResult
    {
      Files = files,
      Entries = entries
    });
  }

  public Task<ProfileApplyResult> ApplyAsync(
    GameProject project,
    ProfileExtractionResult extraction,
    IReadOnlyDictionary<string, string> translatedEntries,
    PipelineOptions options,
    CancellationToken ct)
  {
    var patched = new List<PatchedFile>();
    var entryMap = extraction.Entries
      .GroupBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(x => x.Key, x => x.OrderByDescending(v => v.Start).ToList(), StringComparer.OrdinalIgnoreCase);

    foreach (var file in extraction.Files)
    {
      ct.ThrowIfCancellationRequested();
      if (!entryMap.TryGetValue(file.RelativePath, out var fileEntries) || fileEntries.Count == 0)
      {
        continue;
      }

      var sb = new StringBuilder(file.Content);
      var changed = false;
      foreach (var entry in fileEntries)
      {
        if (!translatedEntries.TryGetValue(entry.Id, out var translated))
        {
          continue;
        }

        var normalized = NormalizeTranslatedValue(translated, entry.Context);
        if (string.Equals(normalized, entry.SourceText, StringComparison.Ordinal))
        {
          continue;
        }

        if (entry.Start < 0 || entry.Start + entry.Length > sb.Length)
        {
          continue;
        }

        sb.Remove(entry.Start, entry.Length);
        sb.Insert(entry.Start, normalized);
        changed = true;
      }

      if (!changed)
      {
        continue;
      }

      patched.Add(new PatchedFile
      {
        RelativePath = file.RelativePath,
        OriginalAbsolutePath = file.AbsolutePath,
        EncodingName = file.EncodingName,
        OutputContent = sb.ToString()
      });
    }

    return Task.FromResult(new ProfileApplyResult
    {
      Files = patched
    });
  }

  private static IEnumerable<string> EnumerateCandidateFiles(string rootPath, PipelineOptions options)
  {
    var roots = new List<string>();
    if (options.IncludeFolders.Count > 0)
    {
      roots.AddRange(options.IncludeFolders.Select(folder => Path.Combine(rootPath, folder)));
    }

    roots.Add(Path.Combine(rootPath, "game"));
    roots.Add(rootPath);
    var distinctRoots = roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    var maxBytes = options.MaxFileSizeMb * 1024L * 1024L;
    var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var excluded = new HashSet<string>(options.ExcludeExtensions, StringComparer.OrdinalIgnoreCase);

    foreach (var basePath in distinctRoots)
    {
      foreach (var file in Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories))
      {
        if (!yielded.Add(file))
        {
          continue;
        }

        var ext = Path.GetExtension(file);
        if (!SupportedExtensions.Contains(ext) || excluded.Contains(ext))
        {
          continue;
        }

        var normalizedPath = file.Replace('\\', '/');
        if (IgnoredPathMarkers.Any(marker => normalizedPath.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
          continue;
        }

        var info = new FileInfo(file);
        if (info.Length > maxBytes)
        {
          continue;
        }

        yield return file;
      }
    }
  }

  private static IReadOnlyList<Segment> ExtractSegments(string content)
  {
    var segments = new List<Segment>();
    var lineStart = 0;
    for (var i = 0; i <= content.Length; i++)
    {
      if (i < content.Length && content[i] != '\n')
      {
        continue;
      }

      var line = content.Substring(lineStart, i - lineStart).TrimEnd('\r');
      var trimmed = line.Trim();
      if (IsCandidateLine(trimmed))
      {
        foreach (Match match in QuoteRegex.Matches(line))
        {
          var grp = match.Groups["value"].Success ? match.Groups["value"] : match.Groups["single"];
          if (!grp.Success)
          {
            continue;
          }

          if (!IsTranslatableToken(grp.Value, trimmed))
          {
            continue;
          }

          var quoteKind = match.Value.StartsWith("\"", StringComparison.Ordinal) ? "double" : "single";
          segments.Add(new Segment(
            lineStart + grp.Index,
            grp.Length,
            grp.Value,
            $"renpy:quoted:{quoteKind}"));
        }
      }

      lineStart = i + 1;
    }

    return RemoveOverlaps(segments);
  }

  private static bool IsCandidateLine(string trimmedLine)
  {
    if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
    {
      return false;
    }

    if (trimmedLine.StartsWith('"') || trimmedLine.StartsWith('\''))
    {
      return true;
    }

    if (CharacterSayRegex.IsMatch(trimmedLine) || OldNewRegex.IsMatch(trimmedLine))
    {
      return true;
    }

    if (trimmedLine.Contains("_(", StringComparison.Ordinal))
    {
      return true;
    }

    return false;
  }

  private static bool IsTranslatableToken(string rawToken, string line)
  {
    if (AssetPathRegex.IsMatch(line))
    {
      return false;
    }

    var text = rawToken
      .Replace("\\n", " ", StringComparison.Ordinal)
      .Replace("\\t", " ", StringComparison.Ordinal)
      .Replace("\\\"", "\"", StringComparison.Ordinal)
      .Replace("\\'", "'", StringComparison.Ordinal)
      .Trim();

    if (text.Length < 2 || text.Length > 2000)
    {
      return false;
    }

    if (!LetterRegex.IsMatch(text))
    {
      return false;
    }

    if (IdentifierRegex.IsMatch(text))
    {
      return false;
    }

    if (text.Contains("persistent.", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("renpy.", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("config.", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    if (LooksLikeCodeLiteral(text))
    {
      return false;
    }

    return true;
  }

  private static bool LooksLikeCodeLiteral(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return true;
    }

    var hasNoWhitespace = !text.Any(char.IsWhiteSpace);
    if (hasNoWhitespace &&
        (text.Contains('.') || text.Contains('_') || text.Contains('/') || text.Contains('\\') ||
         text.Contains('[') || text.Contains(']') || text.Contains('(') || text.Contains(')') ||
         text.Contains('{') || text.Contains('}') || text.Contains('=')))
    {
      return true;
    }

    return false;
  }

  private static IReadOnlyList<Segment> RemoveOverlaps(List<Segment> segments)
  {
    if (segments.Count == 0)
    {
      return segments;
    }

    var sorted = segments.OrderBy(x => x.Start).ThenByDescending(x => x.Length).ToList();
    var kept = new List<Segment>();
    var cursor = -1;
    foreach (var segment in sorted)
    {
      if (segment.Start < cursor)
      {
        continue;
      }

      kept.Add(segment);
      cursor = segment.Start + segment.Length;
    }

    return kept;
  }

  private static string NormalizeTranslatedValue(string translated, string? context)
  {
    var normalized = translated
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace("\r", "\n", StringComparison.Ordinal)
      .Replace("\n", "\\n", StringComparison.Ordinal)
      .Replace("\t", "\\t", StringComparison.Ordinal);

    normalized = normalized.Replace("\\", "\\\\", StringComparison.Ordinal);

    return context switch
    {
      "renpy:quoted:double" => normalized.Replace("\"", "\\\"", StringComparison.Ordinal),
      "renpy:quoted:single" => normalized.Replace("'", "\\'", StringComparison.Ordinal),
      _ => normalized
    };
  }

  private sealed record Segment(int Start, int Length, string SourceText, string Context);
}

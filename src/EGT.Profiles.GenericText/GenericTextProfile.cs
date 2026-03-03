using System.Text;
using System.Text.RegularExpressions;
using EGT.Contracts.Models;
using EGT.Contracts.Profiles;
using EGT.Core.Encoding;
using EGT.Core.Manifesting;
using Microsoft.Extensions.Logging;

namespace EGT.Profiles.GenericText;

public sealed class GenericTextProfile : IProfile
{
  private static readonly string[] DefaultLocalizationFolders =
    new[] { "Localization", "locales", "locale", "lang", "language", "i18n", "translations" };

  private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".json", ".csv", ".tsv", ".ini", ".xml", ".yaml", ".yml", ".txt", ".strings"
  };

  private static readonly Regex QuotedRegex = new(
    "\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"|'(?<single>(?:[^'\\\\]|\\\\.)*)'",
    RegexOptions.Compiled);

  private static readonly Regex KeyValueRegex = new(
    @"(?m)^(?<prefix>\s*[^#;\[\]\r\n:=]+[:=]\s*)(?<value>[^\r\n]+)$",
    RegexOptions.Compiled);

  private static readonly Regex LetterRegex = new(@"\p{L}", RegexOptions.Compiled);

  private readonly TextFileCodec _codec;
  private readonly ILogger<GenericTextProfile> _logger;

  public GenericTextProfile(TextFileCodec codec, ILogger<GenericTextProfile> logger)
  {
    _codec = codec;
    _logger = logger;
  }

  public string Name => "generic-text";

  public ProfileCapability Capability { get; } = new()
  {
    Version = "1.0.0",
    Priority = 10,
    SupportedExtensions = SupportedExtensions.ToArray(),
    EngineHints = new[] { "generic", "text", "localization" }
  };

  public bool Supports(GameProject project) => true;

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
      var extension = Path.GetExtension(filePath);
      if (!SupportedExtensions.Contains(extension))
      {
        continue;
      }

      var read = _codec.Read(filePath);
      var relative = Path.GetRelativePath(project.RootPath, filePath);
      files.Add(new ExtractedFile
      {
        AbsolutePath = filePath,
        RelativePath = relative,
        Content = read.Content,
        EncodingName = read.EncodingName
      });

      foreach (var segment in ExtractSegments(read.Content, extension))
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

    _logger.LogInformation("GenericText extracted {Entries} entries from {Files} files.", entries.Count, files.Count);
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

      var output = file.Content;
      var changed = false;
      var sb = new StringBuilder(output);

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
    var includeRoots = new List<string>();
    if (options.IncludeFolders.Count > 0)
    {
      includeRoots.AddRange(options.IncludeFolders.Select(folder => Path.Combine(rootPath, folder)));
    }

    foreach (var hint in DefaultLocalizationFolders)
    {
      var hintPath = Path.Combine(rootPath, hint);
      if (Directory.Exists(hintPath))
      {
        includeRoots.Add(hintPath);
      }
    }

    includeRoots.Add(rootPath);
    var distinctRoots = includeRoots
      .Where(Directory.Exists)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    var maxBytes = options.MaxFileSizeMb * 1024L * 1024L;
    var excluded = new HashSet<string>(options.ExcludeExtensions, StringComparer.OrdinalIgnoreCase);
    var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var basePath in distinctRoots)
    {
      foreach (var file in Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories))
      {
        if (!yielded.Add(file))
        {
          continue;
        }

        var ext = Path.GetExtension(file);
        if (excluded.Contains(ext))
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

  private static IReadOnlyList<Segment> ExtractSegments(string content, string extension)
  {
    var ext = extension.ToLowerInvariant();
    var segments = ext switch
    {
      ".txt" or ".strings" => ExtractLineSegments(content, ext),
      ".csv" => ExtractDelimitedSegments(content, ','),
      ".tsv" => ExtractDelimitedSegments(content, '\t'),
      _ => ExtractStructuredSegments(content, ext)
    };

    return RemoveOverlaps(segments);
  }

  private static List<Segment> ExtractLineSegments(string content, string extension)
  {
    var list = new List<Segment>();
    var lineStart = 0;
    for (var i = 0; i <= content.Length; i++)
    {
      if (i < content.Length && content[i] != '\n')
      {
        continue;
      }

      var lineLength = i - lineStart;
      var line = content.Substring(lineStart, lineLength).TrimEnd('\r');
      var trimmed = line.Trim();
      if (trimmed.Length > 0 &&
          !trimmed.StartsWith('#') &&
          !trimmed.StartsWith(';') &&
          !trimmed.StartsWith("//") &&
          IsTranslatable(trimmed))
      {
        var leading = line.Length - line.TrimStart().Length;
        var start = lineStart + leading;
        list.Add(new Segment(start, trimmed.Length, trimmed, $"{extension}:line"));
      }

      lineStart = i + 1;
    }

    return list;
  }

  private static List<Segment> ExtractStructuredSegments(string content, string extension)
  {
    var list = new List<Segment>();

    foreach (Match match in QuotedRegex.Matches(content))
    {
      var grp = match.Groups["value"].Success ? match.Groups["value"] : match.Groups["single"];
      if (!grp.Success || !IsTranslatable(grp.Value))
      {
        continue;
      }

      if (extension == ".json" && LooksLikeJsonKey(content, match.Index, match.Length))
      {
        continue;
      }

      list.Add(new Segment(grp.Index, grp.Length, grp.Value, $"{extension}:quoted"));
    }

    if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
    {
      foreach (Match match in KeyValueRegex.Matches(content))
      {
        var value = match.Groups["value"];
        if (!value.Success)
        {
          continue;
        }

        var trimmed = value.Value.Trim();
        if (!IsTranslatable(trimmed))
        {
          continue;
        }

        var leading = value.Value.Length - value.Value.TrimStart().Length;
        list.Add(new Segment(value.Index + leading, trimmed.Length, trimmed, $"{extension}:kv"));
      }
    }

    return list;
  }

  private static List<Segment> ExtractDelimitedSegments(string content, char delimiter)
  {
    var list = new List<Segment>();
    var lineStart = 0;
    for (var i = 0; i <= content.Length; i++)
    {
      if (i < content.Length && content[i] != '\n')
      {
        continue;
      }

      var lineLength = i - lineStart;
      var line = content.Substring(lineStart, lineLength).TrimEnd('\r');
      ParseDelimitedLine(line, lineStart, delimiter, list);
      lineStart = i + 1;
    }

    return list;
  }

  private static void ParseDelimitedLine(string line, int absoluteOffset, char delimiter, List<Segment> output)
  {
    var i = 0;
    while (i <= line.Length)
    {
      var fieldStart = i;
      var quoted = i < line.Length && line[i] == '"';
      if (quoted)
      {
        var valueStart = i + 1;
        i++;
        while (i < line.Length)
        {
          if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"')
          {
            i += 2;
            continue;
          }

          if (line[i] == '"')
          {
            break;
          }

          i++;
        }

        var valueEnd = i;
        var raw = valueEnd > valueStart ? line.Substring(valueStart, valueEnd - valueStart) : string.Empty;
        var unescaped = raw.Replace("\"\"", "\"");
        if (IsTranslatable(unescaped))
        {
          output.Add(new Segment(absoluteOffset + valueStart, raw.Length, raw, "csv:quoted"));
        }

        if (i < line.Length && line[i] == '"')
        {
          i++;
        }
      }
      else
      {
        while (i < line.Length && line[i] != delimiter)
        {
          i++;
        }

        var raw = line.Substring(fieldStart, i - fieldStart);
        var trimmed = raw.Trim();
        if (IsTranslatable(trimmed))
        {
          var leading = raw.Length - raw.TrimStart().Length;
          output.Add(new Segment(absoluteOffset + fieldStart + leading, trimmed.Length, trimmed, "csv:plain"));
        }
      }

      if (i < line.Length && line[i] == delimiter)
      {
        i++;
      }
      else if (i >= line.Length)
      {
        break;
      }
    }
  }

  private static bool LooksLikeJsonKey(string content, int tokenStart, int tokenLength)
  {
    var i = tokenStart + tokenLength;
    while (i < content.Length && char.IsWhiteSpace(content[i]))
    {
      i++;
    }

    return i < content.Length && content[i] == ':';
  }

  private static bool IsTranslatable(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    var trimmed = value.Trim();
    if (trimmed.Length < 2 || trimmed.Length > 2000)
    {
      return false;
    }

    if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    if (!LetterRegex.IsMatch(trimmed))
    {
      return false;
    }

    return true;
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
    if (string.IsNullOrEmpty(context))
    {
      return translated;
    }

    if (context.StartsWith("csv:quoted", StringComparison.OrdinalIgnoreCase))
    {
      return translated.Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    return translated;
  }

  private sealed record Segment(int Start, int Length, string SourceText, string Context);
}

namespace EGT.Contracts.Profiles;

public sealed class ProfileExtractionResult
{
  public required IReadOnlyList<ExtractedFile> Files { get; init; }
  public required IReadOnlyList<ExtractedEntry> Entries { get; init; }
}

public sealed class ProfileApplyResult
{
  public required IReadOnlyList<PatchedFile> Files { get; init; }
}

public sealed class ExtractedFile
{
  public required string RelativePath { get; init; }
  public required string AbsolutePath { get; init; }
  public required string EncodingName { get; init; }
  public required string Content { get; init; }
}

public sealed class ExtractedEntry
{
  public required string Id { get; init; }
  public required string RelativePath { get; init; }
  public required int Start { get; init; }
  public required int Length { get; init; }
  public required string SourceText { get; init; }
  public string? Context { get; init; }
}

public sealed class PatchedFile
{
  public required string RelativePath { get; init; }
  public required string OriginalAbsolutePath { get; init; }
  public required string EncodingName { get; init; }
  public required string OutputContent { get; init; }
}


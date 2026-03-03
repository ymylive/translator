namespace EGT.Contracts.Models;

public sealed record GameProject(
  string Name,
  string ExePath,
  string RootPath,
  IReadOnlyDictionary<string, string> DetectedHints);


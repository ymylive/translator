namespace EGT.Core.Abstractions;

public interface ITranslationCache
{
  Task<string?> GetAsync(string cacheFilePath, string key, CancellationToken ct);
  Task SetAsync(string cacheFilePath, string key, string translated, CancellationToken ct);
}


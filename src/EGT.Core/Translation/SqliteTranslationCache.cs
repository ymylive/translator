using Microsoft.Data.Sqlite;
using EGT.Core.Abstractions;

namespace EGT.Core.Translation;

public sealed class SqliteTranslationCache : ITranslationCache
{
  public async Task<string?> GetAsync(string cacheFilePath, string key, CancellationToken ct)
  {
    EnsureDb(cacheFilePath);
    await using var conn = new SqliteConnection($"Data Source={cacheFilePath}");
    await conn.OpenAsync(ct);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT translated FROM translation_cache WHERE cache_key = $key LIMIT 1;";
    cmd.Parameters.AddWithValue("$key", key);

    var result = await cmd.ExecuteScalarAsync(ct);
    return result as string;
  }

  public async Task SetAsync(string cacheFilePath, string key, string translated, CancellationToken ct)
  {
    EnsureDb(cacheFilePath);
    await using var conn = new SqliteConnection($"Data Source={cacheFilePath}");
    await conn.OpenAsync(ct);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText =
      """
      INSERT INTO translation_cache(cache_key, translated, updated_utc)
      VALUES($key, $translated, $updated)
      ON CONFLICT(cache_key)
      DO UPDATE SET translated = excluded.translated, updated_utc = excluded.updated_utc;
      """;
    cmd.Parameters.AddWithValue("$key", key);
    cmd.Parameters.AddWithValue("$translated", translated);
    cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));

    await cmd.ExecuteNonQueryAsync(ct);
  }

  private static void EnsureDb(string cacheFilePath)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);
    using var conn = new SqliteConnection($"Data Source={cacheFilePath}");
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText =
      """
      CREATE TABLE IF NOT EXISTS translation_cache(
        cache_key TEXT PRIMARY KEY,
        translated TEXT NOT NULL,
        updated_utc TEXT NOT NULL
      );
      """;
    cmd.ExecuteNonQuery();
  }
}


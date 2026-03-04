using System.Security.Cryptography;
using System.Text.Json;
using EGT.Contracts.Models;
using TextEncoding = System.Text.Encoding;

namespace EGT.Core.Manifesting;

public static class Hashing
{
  public static string Sha256(string text)
  {
    var bytes = TextEncoding.UTF8.GetBytes(text);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  public static string FileSha256(string filePath)
  {
    using var stream = File.OpenRead(filePath);
    var hash = SHA256.HashData(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  public static string OptionsHash(PipelineOptions options)
  {
    var normalized = new
    {
      options.ProfileName,
      options.ProviderName,
      options.FallbackProviderName,
      options.SecondFallbackProviderName,
      options.SourceLang,
      options.TargetLang,
      options.PreserveFormatting,
      options.MaxConcurrency,
      options.MaxItemsPerBatch,
      options.MaxCharsPerBatch,
      options.AiBatchSize,
      options.MaxFileSizeMb,
      options.ApplyInPlace,
      options.OverwriteOutput,
      options.ProviderEndpoint,
      options.ProviderModel,
      options.ProviderRegion,
      options.FallbackProviderEndpoint,
      options.FallbackProviderModel,
      options.FallbackProviderRegion,
      options.SecondFallbackProviderEndpoint,
      options.SecondFallbackProviderModel,
      options.SecondFallbackProviderRegion,
      options.ExcludeExtensions
    };
    var json = JsonSerializer.Serialize(normalized);
    return Sha256(json);
  }
}

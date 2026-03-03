using System.Security.Cryptography;
using EGT.Core.Abstractions;
using TextEncoding = System.Text.Encoding;

namespace EGT.Core.Pipeline;

public sealed class WindowsSecretStore : ISecretStore
{
  private static readonly byte[] Entropy = TextEncoding.UTF8.GetBytes("EGT_DPAPI_v1");
  private readonly string _root;

  public WindowsSecretStore()
  {
    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    _root = Path.Combine(localAppData, "EGT", "secrets");
  }

  public Task SaveAsync(string key, string value, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    Directory.CreateDirectory(_root);

    var encrypted = ProtectedData.Protect(
      TextEncoding.UTF8.GetBytes(value),
      Entropy,
      DataProtectionScope.CurrentUser);

    var filePath = ResolvePath(key);
    File.WriteAllBytes(filePath, encrypted);
    return Task.CompletedTask;
  }

  public Task<string?> GetAsync(string key, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    var filePath = ResolvePath(key);
    if (!File.Exists(filePath))
    {
      return Task.FromResult<string?>(null);
    }

    var encrypted = File.ReadAllBytes(filePath);
    var decrypted = ProtectedData.Unprotect(
      encrypted,
      Entropy,
      DataProtectionScope.CurrentUser);

    return Task.FromResult<string?>(TextEncoding.UTF8.GetString(decrypted));
  }

  private string ResolvePath(string key)
  {
    var safe = Manifesting.Hashing.Sha256(key);
    return Path.Combine(_root, $"{safe}.bin");
  }
}

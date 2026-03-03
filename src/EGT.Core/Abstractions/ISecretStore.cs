namespace EGT.Core.Abstractions;

public interface ISecretStore
{
  Task SaveAsync(string key, string value, CancellationToken ct);
  Task<string?> GetAsync(string key, CancellationToken ct);
}


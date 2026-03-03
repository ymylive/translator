namespace EGT.Core.Abstractions;

public interface IRestoreService
{
  Task RestoreAsync(string manifestPath, CancellationToken ct);
}


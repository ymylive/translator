using EGT.Contracts.Models;

namespace EGT.Core.Abstractions;

public interface IGameProjectResolver
{
  GameProject Resolve(string exePath);
}


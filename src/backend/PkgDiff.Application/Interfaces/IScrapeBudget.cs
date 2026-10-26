namespace PkgDiff.Application.Interfaces;

public interface IScrapeBudget
{
    Task<bool> TryReserveAsync(string ipAddress);
}

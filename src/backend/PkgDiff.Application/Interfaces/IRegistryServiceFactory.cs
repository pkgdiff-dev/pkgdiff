using System.Diagnostics.CodeAnalysis;

namespace PkgDiff.Application.Interfaces;

public interface IRegistryServiceFactory
{
    // By any spelling of the registry's name; an ArgumentException when no client serves it
    IRegistryClient GetClient(string registry);

    bool TryGetClient(string? registry, [NotNullWhen(true)] out IRegistryClient? client);
}

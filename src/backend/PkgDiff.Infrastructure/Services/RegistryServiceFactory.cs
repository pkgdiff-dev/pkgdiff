using System.Diagnostics.CodeAnalysis;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.Constants;

namespace PkgDiff.Infrastructure.Services;

public class RegistryServiceFactory : IRegistryServiceFactory
{
    private readonly Dictionary<string, IRegistryClient> _clients;

    // Two clients for one registry fail here: which one serves it is never decided at run time
    public RegistryServiceFactory(IEnumerable<IRegistryClient> clients)
    {
        _clients = clients.ToDictionary(client => client.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IRegistryClient GetClient(string registry) =>
        TryGetClient(registry, out var client)
            ? client
            : throw new ArgumentException($"Registry '{registry}' is not supported.");

    public bool TryGetClient(string? registry, [NotNullWhen(true)] out IRegistryClient? client)
    {
        client = null;
        return RegistryNames.Canonical(registry) is { } name && _clients.TryGetValue(name, out client);
    }
}

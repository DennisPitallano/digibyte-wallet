using System.Net;

namespace DigiByte.Server.Shared;

/// <summary>DNS lookup used by the guard. A seam so tests can simulate rebinding and private-address hostnames.</summary>
public interface IOutboundResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}

public sealed class DnsOutboundResolver : IOutboundResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);
}

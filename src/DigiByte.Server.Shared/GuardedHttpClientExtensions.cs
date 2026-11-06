using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Server.Shared;

/// <summary>Connect-time enforcement: resolves the host itself, refuses if ANY result is blocked, then connects to an address it checked.</summary>
public static class GuardedConnector
{
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, OutboundUrlPolicy policy, IOutboundResolver resolver, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await resolver.ResolveAsync(host, ct);
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        var blocked = addresses.FirstOrDefault(a => OutboundUrlGuard.IsBlockedAddress(a, policy.AllowLoopback));
        if (blocked is not null)
            throw new OutboundBlockedException($"{host} resolved to a blocked address ({blocked})");

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                socket.Dispose();
                last = ex;
            }
        }
        throw last ?? new SocketException((int)SocketError.HostUnreachable);
    }

    /// <summary>A handler that never follows redirects and applies the connect-time check.</summary>
    public static SocketsHttpHandler CreateHandler(OutboundUrlPolicy policy, IOutboundResolver resolver) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(policy.TimeoutSeconds),
        ConnectCallback = (context, ct) => ConnectAsync(context, policy, resolver, ct),
    };
}

public static class GuardedHttpClientExtensions
{
    /// <summary>
    /// Registers a named client for requests to user-supplied URLs: no redirects, connect-time address check, request timeout from the policy.
    /// Uses the <see cref="IOutboundResolver"/> in DI (DNS by default).
    /// </summary>
    public static IHttpClientBuilder AddGuardedHttpClient(this IServiceCollection services, string name, OutboundUrlPolicy policy)
    {
        services.AddSingleton<IOutboundResolver, DnsOutboundResolver>();
        return services.AddHttpClient(name, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiByte-Wallet/1.0");
                client.Timeout = TimeSpan.FromSeconds(policy.TimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(sp => GuardedConnector.CreateHandler(policy, sp.GetRequiredService<IOutboundResolver>()));
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> of the body as text (UTF-8, invalid bytes replaced). <c>Truncated</c> is true when the body was longer.
    /// Send the request with <c>HttpCompletionOption.ResponseHeadersRead</c> so an oversized body is never buffered.
    /// </summary>
    public static async Task<(string Text, bool Truncated)> ReadCappedAsync(this HttpContent content, int maxBytes, CancellationToken ct = default)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[maxBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0) break;
            total += read;
        }
        var truncated = total > maxBytes;
        return (Encoding.UTF8.GetString(buffer, 0, Math.Min(total, maxBytes)), truncated);
    }
}

using DigiByte.SpvGateway.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.SpvGateway.Tests.Support;

/// <summary>Runs the real gateway in-process with its data folder in a temporary directory.</summary>
internal sealed class GatewayFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;

    private readonly Action<IServiceCollection>? _configureServices;

    public GatewayFactory(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)
    {
        _configureServices = configureServices;
        _settings = settings ?? [];
        DataDirectory = Path.Combine(Path.GetTempPath(), "spvgw-" + Guid.NewGuid().ToString("N"));
    }

    public string DataDirectory { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>(_settings) { ["SpvGateway:DataDirectory"] = DataDirectory, ["SpvGateway:PeersEnabled"] = "false" };
            config.AddInMemoryCollection(values);
        });
        if (_configureServices is not null)
            builder.ConfigureTestServices(_configureServices);
    }

    public HeaderSync Sync => Services.GetRequiredService<HeaderSync>();

    public HeaderStore Store => Services.GetRequiredService<HeaderStore>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(DataDirectory))
                Directory.Delete(DataDirectory, true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is harmless.
        }
    }
}

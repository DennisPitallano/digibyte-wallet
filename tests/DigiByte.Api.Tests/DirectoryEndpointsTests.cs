using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigiByte.Api.Tests;

/// <summary>T013 (DGB-01): nobody can create or delete directory entries; lookups keep working (contracts/directory.md).</summary>
public class DirectoryEndpointsTests : IClassFixture<DirectoryEndpointsTests.Factory>
{
    private readonly HttpClient _client;

    public DirectoryEndpointsTests(Factory factory) => _client = factory.CreateClient();

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production");
    }

    private static bool NotRoutable(HttpStatusCode code) => code is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed;

    [Fact]
    public async Task Register_IsNotRoutable()
    {
        var response = await _client.PostAsJsonAsync("/api/directory/register", new { username = "attacker", address = "DAttackerAddress" });
        Assert.True(NotRoutable(response.StatusCode), $"POST /api/directory/register returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Delete_IsNotRoutable()
    {
        var response = await _client.DeleteAsync("/api/directory/x");
        Assert.True(NotRoutable(response.StatusCode), $"DELETE /api/directory/x returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task LookupOfUnknownUsername_Returns404WithTheExistingErrorBody()
    {
        var response = await _client.GetAsync("/api/directory/lookup/anything");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"error\"", body);
        Assert.Contains("not found", body);
    }

    [Fact]
    public async Task LookupOfUnknownPhone_Returns404WithTheExistingErrorBody()
    {
        var response = await _client.GetAsync("/api/directory/lookup/phone/%2B15550100");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("\"error\"", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>T033 (FR-015): /api/health reports the commit.</summary>
public class ApiHealthCommitTests : IClassFixture<DirectoryEndpointsTests.Factory>
{
    private readonly HttpClient _client;
    public ApiHealthCommitTests(DirectoryEndpointsTests.Factory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_HasACommitField()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", doc.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("commit").GetString()));
    }
}

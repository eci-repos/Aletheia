using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Repository.IntegrationTests.Fixtures;

namespace Repository.IntegrationTests;

public class ApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task AuthenticateAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = "Admin123!"
        });

        loginResponse.EnsureSuccessStatusCode();
        var json = await loginResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var token = doc.RootElement.GetProperty("accessToken").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task Search_returns_ok()
    {
        await AuthenticateAsync();
        var response = await _client.GetAsync("/api/search?pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Metadata_returns_not_found_for_unknown_file()
    {
        await AuthenticateAsync();
        var response = await _client.GetAsync($"/api/metadata?fileId={Guid.NewGuid()}&fileName=unknown.txt");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Versions_list_returns_ok()
    {
        await AuthenticateAsync();
        var response = await _client.GetAsync($"/api/versions?fileId={Guid.NewGuid()}&fileName=test.txt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

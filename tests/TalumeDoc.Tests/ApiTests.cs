using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalumeDoc.Core;
using Xunit;
namespace TalumeDoc.Tests;
public sealed class ApiTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "talumedoc-api-tests-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;
    public Task InitializeAsync()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseEnvironment("Development"); b.UseSetting("DataDirectory", root); b.UseSetting("StorageMode", "Local");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["StorageMode"] = "Local", ["DataDirectory"] = root })); });
        client = factory.CreateClient(); return Task.CompletedTask;
    }
    [Fact] public async Task UploadProcessesAndExportsReport()
    {
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent("sku,nome,quantidade,preco\nA,Nome,1,1\nA,Outro,2,3"u8.ToArray()), "file", "test.csv");
        var response = await client.PostAsync("/api/documents/", form); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var d = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        JsonElement result = default;
        for (int i = 0; i < 40; i++) { result = await client.GetFromJsonAsync<JsonElement>($"/api/documents/{d}"); if (result.GetProperty("status").GetString() == "Completed") break; await Task.Delay(100); }
        Assert.Equal("Completed", result.GetProperty("status").GetString()); Assert.Equal(1, result.GetProperty("invalidRows").GetInt32());
        var csv = await client.GetStringAsync($"/api/documents/{d}/report?download=true"); Assert.Contains("DUPLICATE", csv);
        var retry = await client.PostAsync($"/api/documents/{d}/retry", null); Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
    }
    [Fact] public async Task CrossOwnerReadReportAndRetryReturn404()
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>(); var id = Guid.CreateVersion7().ToString("N");
        await store.CreateAsync(new("other-user", id, "private.csv", 10, DateTimeOffset.UtcNow, "private"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/documents/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/documents/{id}/report")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/documents/{id}/retry", null)).StatusCode);
    }
    [Fact] public async Task WrongExtensionIsRejected()
    { using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent([1]), "file", "test.exe"); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/documents/", form)).StatusCode); }
    [Fact] public async Task OpenApiIsAvailableLocally()
    { var spec = await client.GetStringAsync("/openapi/v1.json"); Assert.Contains("/api/documents", spec); }
    [Fact] public void LocalModeCannotRunInProduction()
    {
        using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        Assert.Throws<InvalidOperationException>(() => production.CreateClient());
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}

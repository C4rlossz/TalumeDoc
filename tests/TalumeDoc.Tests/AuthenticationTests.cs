using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TalumeDoc.Core;
using TalumeDoc.Infrastructure;
using Xunit;
namespace TalumeDoc.Tests;
public sealed class AuthenticationTests : IAsyncLifetime
{
    private const string Issuer = "https://cognito-idp.us-east-1.amazonaws.com/test-pool";
    private readonly RSA rsa = RSA.Create(2048);
    private readonly string root = Path.Combine(Path.GetTempPath(), "talumedoc-auth-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;
    public Task InitializeAsync()
    {
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("COGNITO_ISSUER", Issuer); b.UseSetting("COGNITO_CLIENT_ID", "test-client"); b.UseSetting("StorageMode", "Aws");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["StorageMode"] = "Aws", ["COGNITO_ISSUER"] = Issuer, ["COGNITO_CLIENT_ID"] = "test-client" }));
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDocumentStore>(); services.AddSingleton<IDocumentStore>(new LocalDocumentStore(root, TimeProvider.System));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var config = new OpenIdConnectConfiguration { Issuer = Issuer }; config.SigningKeys.Add(key);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
                });
            });
        });
        client = factory.CreateClient(); return Task.CompletedTask;
    }
    private string Token(string clientId = "test-client", string use = "access", bool expired = false, string issuer = Issuer)
    {
        var token = new JwtSecurityToken(issuer: issuer, claims: [new("sub", Guid.NewGuid().ToString()), new("client_id", clientId), new("token_use", use)],
            notBefore: DateTime.UtcNow.AddHours(-2), expires: expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "test-key" }, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    [Fact] public async Task AnonymousRequestIsDenied() => Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/documents/")).StatusCode);
    [Fact] public async Task ValidAccessTokenCanReadOwnWorkspace()
    { client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token()); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/documents/")).StatusCode); }
    [Theory]
    [InlineData("other-client", "access", false, Issuer)]
    [InlineData("test-client", "id", false, Issuer)]
    [InlineData("test-client", "access", true, Issuer)]
    [InlineData("test-client", "access", false, "https://untrusted.invalid")]
    public async Task InvalidTokensAreDenied(string clientId, string use, bool expired, string issuer)
    { client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(clientId, use, expired, issuer)); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/documents/")).StatusCode); }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); rsa.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}

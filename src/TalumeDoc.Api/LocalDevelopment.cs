using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using TalumeDoc.Core;

namespace TalumeDoc.Api;

public sealed class LocalAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
        AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "local-demo"), new Claim("name", "Ambiente local")], Scheme.Name)), Scheme.Name)));
}
public sealed class LocalWorker(IDocumentStore store, DocumentProcessor processor, ILogger<LocalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var items = await store.ListAsync("local-demo", stoppingToken);
                foreach (var d in items.Where(d => d.Status == DocumentStatus.Queued ||
                    d.Status == DocumentStatus.Processing && d.Attempts < DocumentLimits.MaxAttempts && d.LeaseUntil <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                {
                    try { await processor.ProcessAsync(new(d.OwnerId, d.Id), stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex) { logger.LogWarning(ex, "Local processing failed for {DocumentId}", d.Id); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Local worker poll failed"); }
        }
    }
}

using TalumeDoc.Core;
using TalumeDoc.Infrastructure;
using Xunit;
namespace TalumeDoc.Tests;
public sealed class ProcessingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "talumedoc-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new();
    private LocalDocumentStore Store() => new(root, clock);
    private static DocumentRecord Document(string owner = "alice") => new(owner, Guid.CreateVersion7().ToString("N"), "test.csv", 38, DateTimeOffset.UtcNow, "inputs/test.csv");
    [Fact] public async Task DuplicateDeliveryDoesNotProcessTwice()
    {
        var store = Store(); var objects = new LocalObjectStore(root); var d = Document(); await store.CreateAsync(d);
        await objects.PutAsync(d.InputKey, new MemoryStream("sku,nome,quantidade,preco\nA,Nome,1,2"u8.ToArray()), "text/csv");
        var processor = new DocumentProcessor(store, objects, new CsvValidator());
        await processor.ProcessAsync(new(d.OwnerId, d.Id)); await processor.ProcessAsync(new(d.OwnerId, d.Id));
        var result = await store.GetAsync(d.OwnerId, d.Id);
        Assert.Equal(DocumentStatus.Completed, result!.Status); Assert.Equal(1, result.Attempts); Assert.Equal(1, result.ValidRows);
    }
    [Fact] public async Task OnlyOneConcurrentWorkerCanClaim()
    { var store = Store(); var d = Document(); await store.CreateAsync(d); var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.ClaimAsync(d.OwnerId, d.Id))); Assert.Single(claims, x => x != null); }
    [Fact] public async Task ExpiredWorkerCannotOverwriteNewWorker()
    {
        var store = Store(); var d = Document(); await store.CreateAsync(d);
        var first = (await store.ClaimAsync(d.OwnerId, d.Id))!;
        clock.Advance(100); var second = (await store.ClaimAsync(d.OwnerId, d.Id))!;
        Assert.False(await store.CompleteAsync(first, new(1, 1, 0, []), "old"));
        Assert.True(await store.CompleteAsync(second, new(2, 2, 0, []), "new"));
        Assert.Equal("new", (await store.GetAsync(d.OwnerId, d.Id))!.ReportKey);
    }
    [Fact] public async Task FailureExhaustionAllowsExplicitRetry()
    {
        var store = Store(); var d = Document(); await store.CreateAsync(d);
        for (var i = 0; i < 5; i++) { var claim = (await store.ClaimAsync(d.OwnerId, d.Id))!; await store.FailAsync(claim); clock.Advance(100); }
        Assert.Equal(DocumentStatus.Failed, (await store.GetAsync(d.OwnerId, d.Id))!.Status);
        Assert.True(await store.RetryAsync(d.OwnerId, d.Id));
        Assert.Equal(0, (await store.GetAsync(d.OwnerId, d.Id))!.Attempts);
    }
    [Fact] public async Task OwnersAreIsolated()
    { var store = Store(); var d = Document(); await store.CreateAsync(d); Assert.Null(await store.GetAsync("bob", d.Id)); Assert.Empty(await store.ListAsync("bob")); Assert.False(await store.RetryAsync("bob", d.Id)); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Clock : TimeProvider { private DateTimeOffset now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => now; public void Advance(int seconds) => now = now.AddSeconds(seconds); }
}

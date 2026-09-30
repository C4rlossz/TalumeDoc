using System.Text.Json;
using System.Text.Json.Serialization;

namespace TalumeDoc.Core;

public enum DocumentStatus { Queued, Processing, Completed, Rejected, Failed }
public sealed record DocumentRecord(
    string OwnerId, string Id, string FileName, long SizeBytes, DateTimeOffset CreatedAt,
    string InputKey, DocumentStatus Status = DocumentStatus.Queued, int Attempts = 0,
    long LeaseUntil = 0, string? LeaseToken = null, string Version = "",
    int TotalRows = 0, int ValidRows = 0, int InvalidRows = 0,
    string? ReportKey = null, string? Error = null, DateTimeOffset? FinishedAt = null);
public sealed record ValidationIssue(int Row, string Code, string Message);
public sealed record ValidationReport(int TotalRows, int ValidRows, int InvalidRows,
    IReadOnlyList<ValidationIssue> Issues, bool Rejected = false);
public sealed record ProcessingJob(string OwnerId, string DocumentId);
public static class DocumentLimits
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxRows = 10_000;
    public const int MaxAttempts = 5;
    public const int LeaseSeconds = 90;
}
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
public interface IDocumentStore
{
    Task CreateAsync(DocumentRecord document, CancellationToken ct = default);
    Task<DocumentRecord?> GetAsync(string owner, string id, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentRecord>> ListAsync(string owner, CancellationToken ct = default);
    Task<DocumentRecord?> ClaimAsync(string owner, string id, CancellationToken ct = default);
    Task<bool> CompleteAsync(DocumentRecord claimed, ValidationReport report, string reportKey, CancellationToken ct = default);
    Task FailAsync(DocumentRecord claimed, CancellationToken ct = default);
    Task<bool> RetryAsync(string owner, string id, CancellationToken ct = default);
}
public interface IObjectStore
{
    Task PutAsync(string key, Stream data, string contentType, CancellationToken ct = default);
    Task<byte[]> ReadAsync(string key, CancellationToken ct = default);
}
public abstract class DocumentStoreBase(TimeProvider clock) : IDocumentStore
{
    public abstract Task CreateAsync(DocumentRecord document, CancellationToken ct = default);
    public abstract Task<DocumentRecord?> GetAsync(string owner, string id, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<DocumentRecord>> ListAsync(string owner, CancellationToken ct = default);
    protected abstract Task<bool> CompareExchangeAsync(DocumentRecord before, DocumentRecord after, CancellationToken ct);

    private async Task<DocumentRecord?> ChangeAsync(string owner, string id,
        Func<DocumentRecord, DocumentRecord?> change, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var old = await GetAsync(owner, id, ct);
            if (old is null) return null;
            var next = change(old);
            if (next is null) return null;
            next = next with { Version = Guid.NewGuid().ToString("N") };
            if (await CompareExchangeAsync(old, next, ct)) return next;
        }
        throw new InvalidOperationException("Concurrent document update. Retry the operation.");
    }
    public Task<DocumentRecord?> ClaimAsync(string owner, string id, CancellationToken ct = default) =>
        ChangeAsync(owner, id, d =>
        {
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            if (d.Attempts >= DocumentLimits.MaxAttempts) return null;
            if (d.Status != DocumentStatus.Queued && !(d.Status == DocumentStatus.Processing && d.LeaseUntil <= now)) return null;
            return d with { Status = DocumentStatus.Processing, Attempts = d.Attempts + 1,
                LeaseToken = Guid.NewGuid().ToString("N"), LeaseUntil = now + DocumentLimits.LeaseSeconds, Error = null };
        }, ct);

    public async Task<bool> CompleteAsync(DocumentRecord claimed, ValidationReport report, string reportKey, CancellationToken ct = default) =>
        await ChangeAsync(claimed.OwnerId, claimed.Id, d => d.LeaseToken != claimed.LeaseToken || d.Status != DocumentStatus.Processing ? null :
            d with { Status = report.Rejected ? DocumentStatus.Rejected : DocumentStatus.Completed,
                TotalRows = report.TotalRows, ValidRows = report.ValidRows, InvalidRows = report.InvalidRows,
                ReportKey = reportKey, FinishedAt = clock.GetUtcNow(), LeaseUntil = 0, LeaseToken = null }, ct) is not null;

    public async Task FailAsync(DocumentRecord claimed, CancellationToken ct = default) =>
        _ = await ChangeAsync(claimed.OwnerId, claimed.Id, d => d.LeaseToken != claimed.LeaseToken ? null :
            d with { Status = d.Attempts >= DocumentLimits.MaxAttempts ? DocumentStatus.Failed : DocumentStatus.Processing,
                Error = "O processamento encontrou uma falha temporária. Novas tentativas são automáticas." }, ct);

    public async Task<bool> RetryAsync(string owner, string id, CancellationToken ct = default) =>
        await ChangeAsync(owner, id, d => !(d.Status == DocumentStatus.Failed || (d.Status == DocumentStatus.Processing && d.Attempts >= DocumentLimits.MaxAttempts && d.LeaseUntil <= clock.GetUtcNow().ToUnixTimeSeconds())) ? null :
            d with { Status = DocumentStatus.Queued, Attempts = 0, LeaseToken = null, LeaseUntil = 0, Error = null }, ct) is not null;
}

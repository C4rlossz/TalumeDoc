using System.Text.Json;

namespace TalumeDoc.Core;

public sealed class DocumentProcessor(IDocumentStore documents, IObjectStore objects, CsvValidator validator)
{
    public async Task ProcessAsync(ProcessingJob job, CancellationToken ct = default)
    {
        var claimed = await documents.ClaimAsync(job.OwnerId, job.DocumentId, ct);
        if (claimed is null)
        {
            var existing = await documents.GetAsync(job.OwnerId, job.DocumentId, ct);
            if (existing?.Status == DocumentStatus.Processing)
                throw new InvalidOperationException("Document has an active processing lease.");
            return; // Missing or terminal document: duplicate delivery is safe.
        }
        try
        {
            var bytes = await objects.ReadAsync(claimed.InputKey, ct);
            var report = validator.Validate(bytes);
            // Attempt-specific key prevents an expired worker from overwriting the winning report.
            var key = $"reports/{claimed.OwnerId}/{claimed.Id}/{claimed.LeaseToken}.json";
            await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(report, JsonDefaults.Options));
            await objects.PutAsync(key, stream, "application/json", ct);
            if (!await documents.CompleteAsync(claimed, report, key, ct))
                throw new InvalidOperationException("Processing lease was superseded.");
        }
        catch
        {
            await documents.FailAsync(claimed, ct);
            throw;
        }
    }
}

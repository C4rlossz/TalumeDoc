using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TalumeDoc.Core;

namespace TalumeDoc.Infrastructure;

/// <summary>Single-process development adapter. Never used in AWS.</summary>
public sealed class LocalDocumentStore(string root, TimeProvider clock) : DocumentStoreBase(clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string OwnerPath(string owner) => Path.Combine(root, "documents", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))));
    private string FilePath(string owner, string id) => Path.Combine(OwnerPath(owner), Guid.Parse(id).ToString("N") + ".json");
    private async Task SaveAsync(DocumentRecord d, CancellationToken ct)
    {
        var path = FilePath(d.OwnerId, d.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(d, JsonDefaults.Options), ct);
        File.Move(temp, path, true);
    }
    public override async Task CreateAsync(DocumentRecord document, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(FilePath(document.OwnerId, document.Id))) throw new InvalidOperationException("Document exists.");
            await SaveAsync(document with { Version = Guid.NewGuid().ToString("N") }, ct);
        }
        finally { gate.Release(); }
    }
    public override async Task<DocumentRecord?> GetAsync(string owner, string id, CancellationToken ct = default)
    {
        var path = FilePath(owner, id);
        return File.Exists(path) ? JsonSerializer.Deserialize<DocumentRecord>(await File.ReadAllTextAsync(path, ct), JsonDefaults.Options) : null;
    }
    public override async Task<IReadOnlyList<DocumentRecord>> ListAsync(string owner, CancellationToken ct = default)
    {
        var path = OwnerPath(owner);
        if (!Directory.Exists(path)) return [];
        var result = new List<DocumentRecord>();
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
            result.Add(JsonSerializer.Deserialize<DocumentRecord>(await File.ReadAllTextAsync(file, ct), JsonDefaults.Options)!);
        return result.OrderByDescending(x => x.CreatedAt).Take(200).ToArray();
    }
    protected override async Task<bool> CompareExchangeAsync(DocumentRecord before, DocumentRecord after, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var current = await GetAsync(before.OwnerId, before.Id, ct);
            if (current?.Version != before.Version) return false;
            await SaveAsync(after, ct);
            return true;
        }
        finally { gate.Release(); }
    }
}
public sealed class LocalObjectStore(string root) : IObjectStore
{
    private string Resolve(string key)
    {
        var basePath = Path.GetFullPath(Path.Combine(root, "objects")) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(basePath, key));
        if (!path.StartsWith(basePath, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid storage key.");
        return path;
    }
    public async Task PutAsync(string key, Stream data, string contentType, CancellationToken ct = default)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var output = File.Create(path);
        await data.CopyToAsync(output, ct);
    }
    public Task<byte[]> ReadAsync(string key, CancellationToken ct = default) => File.ReadAllBytesAsync(Resolve(key), ct);
}

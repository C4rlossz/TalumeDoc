using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
using TalumeDoc.Core;

namespace TalumeDoc.Infrastructure;

public sealed class DynamoDocumentStore(IAmazonDynamoDB client, string table, TimeProvider clock) : DocumentStoreBase(clock)
{
    private static Dictionary<string, AttributeValue> Key(string owner, string id) => new()
    { ["ownerId"] = new() { S = owner }, ["id"] = new() { S = id } };
    private static Dictionary<string, AttributeValue> Item(DocumentRecord d)
    {
        var item = Key(d.OwnerId, d.Id);
        item["status"] = new() { S = d.Status.ToString() };
        item["version"] = new() { S = d.Version };
        item["payload"] = new() { S = JsonSerializer.Serialize(d, JsonDefaults.Options) };
        item["expiresAt"] = new() { N = d.CreatedAt.AddDays(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return item;
    }
    private static DocumentRecord Parse(Dictionary<string, AttributeValue> item) => JsonSerializer.Deserialize<DocumentRecord>(item["payload"].S, JsonDefaults.Options)!;
    public override async Task CreateAsync(DocumentRecord document, CancellationToken ct = default) =>
        await client.PutItemAsync(new PutItemRequest { TableName = table,
            Item = Item(document with { Version = Guid.NewGuid().ToString("N") }), ConditionExpression = "attribute_not_exists(id)" }, ct);
    public override async Task<DocumentRecord?> GetAsync(string owner, string id, CancellationToken ct = default)
    {
        var result = await client.GetItemAsync(new GetItemRequest { TableName = table, Key = Key(owner, id), ConsistentRead = true }, ct);
        return result.Item is { Count: > 0 } ? Parse(result.Item) : null;
    }
    public override async Task<IReadOnlyList<DocumentRecord>> ListAsync(string owner, CancellationToken ct = default)
    {
        var result = await client.QueryAsync(new QueryRequest { TableName = table, KeyConditionExpression = "ownerId = :owner",
            ExpressionAttributeValues = new() { [":owner"] = new() { S = owner } }, ScanIndexForward = false, Limit = 200, ConsistentRead = true }, ct);
        return result.Items?.Select(Parse).ToArray() ?? [];
    }
    protected override async Task<bool> CompareExchangeAsync(DocumentRecord before, DocumentRecord after, CancellationToken ct)
    {
        try
        {
            await client.PutItemAsync(new PutItemRequest { TableName = table, Item = Item(after),
                ConditionExpression = "#version = :old", ExpressionAttributeNames = new() { ["#version"] = "version" },
                ExpressionAttributeValues = new() { [":old"] = new() { S = before.Version } } }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException) { return false; }
    }
}
public sealed class S3ObjectStore(IAmazonS3 client, string bucket) : IObjectStore
{
    public async Task PutAsync(string key, Stream data, string contentType, CancellationToken ct = default) =>
        await client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, InputStream = data, ContentType = contentType }, ct);
    public async Task<byte[]> ReadAsync(string key, CancellationToken ct = default)
    {
        using var response = await client.GetObjectAsync(bucket, key, ct);
        if (response.ContentLength > 16 * 1024 * 1024) throw new InvalidOperationException("Stored object is too large.");
        using var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}

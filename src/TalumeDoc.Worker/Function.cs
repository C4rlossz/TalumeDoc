using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using System.Text.Json;
using TalumeDoc.Core;
using TalumeDoc.Infrastructure;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
namespace TalumeDoc.Worker;

public sealed class Function
{
    private readonly DocumentProcessor processor;
    public Function() : this(new DocumentProcessor(
        new DynamoDocumentStore(new AmazonDynamoDBClient(), Required("DOCUMENT_TABLE"), TimeProvider.System),
        new S3ObjectStore(new AmazonS3Client(), Required("DOCUMENT_BUCKET")), new CsvValidator())) { }
    public Function(DocumentProcessor processor) => this.processor = processor;
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Missing {name}");
    public async Task<SQSBatchResponse> Handle(SQSEvent input, ILambdaContext context)
    {
        var failed = new List<SQSBatchResponse.BatchItemFailure>();
        foreach (var message in input.Records)
        {
            try
            {
                var job = JsonSerializer.Deserialize<ProcessingJob>(message.Body, JsonDefaults.Options)
                    ?? throw new InvalidOperationException("Invalid job.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, context.RemainingTime.TotalSeconds - 5)));
                await processor.ProcessAsync(job, timeout.Token);
                context.Logger.LogInformation("Processed message {0}", message.MessageId);
            }
            catch (Exception ex)
            {
                context.Logger.LogError("Message {0} failed: {1}", message.MessageId, ex.GetType().Name);
                failed.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = message.MessageId });
            }
        }
        return new SQSBatchResponse(failed);
    }
}

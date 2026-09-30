using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using TalumeDoc.Api;
using TalumeDoc.Core;
using TalumeDoc.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var local = builder.Configuration["StorageMode"] == "Local";
if (local && (!builder.Environment.IsDevelopment() || Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME") is not null))
    throw new InvalidOperationException("Local mode is permitted only in Development outside AWS Lambda.");
string Required(string key) => builder.Configuration[key] ?? throw new InvalidOperationException($"Missing configuration: {key}");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CsvValidator>();
builder.Services.AddSingleton<DocumentProcessor>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = DocumentLimits.MaxBytes + 65536);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = DocumentLimits.MaxBytes + 65536);
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
if (local)
{
    var root = Path.GetFullPath(builder.Configuration["DataDirectory"] ?? ".talumedoc-data");
    builder.Services.AddSingleton<IDocumentStore>(new LocalDocumentStore(root, TimeProvider.System));
    builder.Services.AddSingleton<IObjectStore>(new LocalObjectStore(root));
    builder.Services.AddHostedService<LocalWorker>();
    builder.Services.AddAuthentication("Local").AddScheme<AuthenticationSchemeOptions, LocalAuthentication>("Local", _ => { });
}
else
{
    builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
    builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
    builder.Services.AddSingleton<IDocumentStore>(s => new DynamoDocumentStore(s.GetRequiredService<IAmazonDynamoDB>(), Required("DOCUMENT_TABLE"), TimeProvider.System));
    builder.Services.AddSingleton<IObjectStore>(s => new S3ObjectStore(s.GetRequiredService<IAmazonS3>(), Required("DOCUMENT_BUCKET")));
    var issuer = Required("COGNITO_ISSUER");
    var clientId = Required("COGNITO_CLIENT_ID");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    {
        o.Authority = issuer;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = issuer,
            ValidateAudience = false, ValidateLifetime = true, RequireSignedTokens = true, ClockSkew = TimeSpan.FromSeconds(30) };
        o.Events = new JwtBearerEvents { OnTokenValidated = context =>
        {
            // Cognito access tokens identify the app with client_id, not aud.
            if (context.Principal?.FindFirstValue("token_use") != "access" ||
                context.Principal.FindFirstValue("client_id") != clientId ||
                !Guid.TryParse(context.Principal.FindFirstValue("sub"), out _))
                context.Fail("Invalid access token.");
            return Task.CompletedTask;
        } };
    });
}
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("upload", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue("sub") ?? "anonymous", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self' https://*.amazoncognito.com; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "TalumeDoc" }));
app.MapGet("/config", () => Results.Ok(new { local, maxBytes = DocumentLimits.MaxBytes, maxRows = DocumentLimits.MaxRows,
    clientId = builder.Configuration["COGNITO_CLIENT_ID"], domain = builder.Configuration["COGNITO_DOMAIN"] }));
app.MapOpenApi().RequireAuthorization();
var api = app.MapGroup("/api/documents").RequireAuthorization();
api.MapGet("/", async (ClaimsPrincipal user, IDocumentStore store, CancellationToken ct) =>
    Results.Ok((await store.ListAsync(Owner(user), ct)).Select(PublicDocument)));
api.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, IDocumentStore store, CancellationToken ct) =>
    await store.GetAsync(Owner(user), id.ToString("N"), ct) is { } d ? Results.Ok(PublicDocument(d)) : Results.NotFound());
api.MapPost("/", async (HttpRequest request, ClaimsPrincipal user, IDocumentStore store, IObjectStore objects, CancellationToken ct) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { detail = "Envie um formulário com o campo file." });
    IFormCollection form;
    try { form = await request.ReadFormAsync(ct); }
    catch (InvalidDataException) { return Results.BadRequest(new { detail = "Arquivo maior que o limite de 2 MiB." }); }
    var file = form.Files.GetFile("file");
    if (form.Files.Count != 1 || file is null || file.Length is <= 0 or > DocumentLimits.MaxBytes)
        return Results.BadRequest(new { detail = "Envie um arquivo CSV de até 2 MiB." });
    var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
    if (name.Length > 120 || name.Any(char.IsControl) || !name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { detail = "Use um arquivo .csv com nome de até 120 caracteres." });
    var owner = Owner(user);
    var id = Guid.CreateVersion7().ToString("N");
    var key = $"inputs/{owner}/{id}.csv";
    await using var content = file.OpenReadStream();
    await objects.PutAsync(key, content, "text/csv", ct);
    var document = new DocumentRecord(owner, id, name, file.Length, DateTimeOffset.UtcNow, key);
    // DynamoDB Streams + EventBridge Pipes enqueue only after this durable write succeeds.
    await store.CreateAsync(document, ct);
    return Results.Accepted($"/api/documents/{id}", PublicDocument(document));
}).RequireRateLimiting("upload");
api.MapPost("/{id:guid}/retry", async (Guid id, ClaimsPrincipal user, IDocumentStore store, CancellationToken ct) =>
{
    var owner = Owner(user);
    if (await store.GetAsync(owner, id.ToString("N"), ct) is null) return Results.NotFound();
    return await store.RetryAsync(owner, id.ToString("N"), ct) ? Results.Accepted() : Results.Conflict(new { detail = "Somente falhas de processamento podem ser reenviadas." });
}).RequireRateLimiting("upload");
api.MapGet("/{id:guid}/report", async (Guid id, bool? download, ClaimsPrincipal user, IDocumentStore store, IObjectStore objects, CancellationToken ct) =>
{
    var d = await store.GetAsync(Owner(user), id.ToString("N"), ct);
    if (d is null) return Results.NotFound();
    if (d.ReportKey is null) return Results.Conflict(new { detail = "O relatório ainda não está disponível." });
    var report = JsonSerializer.Deserialize<ValidationReport>(await objects.ReadAsync(d.ReportKey, ct), JsonDefaults.Options)!;
    if (download == true) return Results.File(CsvValidator.ToCsv(report), "text/csv; charset=utf-8", $"talumedoc-{d.Id}-erros.csv");
    return Results.Ok(new { report.TotalRows, report.ValidRows, report.InvalidRows, report.Rejected,
        totalIssues = report.Issues.Count, issues = report.Issues.Take(100) });
});
app.Run();
static string Owner(ClaimsPrincipal user) => user.FindFirstValue("sub") ?? throw new InvalidOperationException("Missing subject.");
static object PublicDocument(DocumentRecord d) => new { d.Id, d.FileName, d.SizeBytes, d.CreatedAt, Status = d.Status == DocumentStatus.Processing && d.Attempts >= DocumentLimits.MaxAttempts && d.LeaseUntil <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? DocumentStatus.Failed : d.Status,
    d.Attempts, d.TotalRows, d.ValidRows, d.InvalidRows, d.FinishedAt, d.Error, hasReport = d.ReportKey is not null };
public partial class Program { }

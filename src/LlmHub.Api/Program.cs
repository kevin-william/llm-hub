using LlmHub.Api.Endpoints;
using LlmHub.Api.McpEventsProbe;
using LlmHub.Api.Mcp;
using LlmHub.Api.Authentication;
using LlmHub.Infrastructure.Persistence;
using LlmHub.Application.Messaging;
using ModelContextProtocol.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<WorkerIdentityValidator>();
builder.Services.AddSingleton(new HubQuotaOptions(
    builder.Configuration.GetValue<int?>("Hub:Quotas:MaxMessageBytes") ?? 65_536,
    builder.Configuration.GetValue<int?>("Hub:Quotas:MaxAttachments") ?? 16,
    builder.Configuration.GetValue<int?>("Hub:Quotas:MaxActiveRunsPerPrincipal") ?? 10,
    builder.Configuration.GetValue<int?>("Hub:Quotas:MaxChannelsPerPrincipal") ?? 100,
    builder.Configuration.GetValue<int?>("Hub:Quotas:MaxActiveSubscriptionsPerPrincipal") ?? 20));
var oidcIssuer = builder.Configuration["Authentication:Issuer"];
var oidcAudience = builder.Configuration["Authentication:Audience"];
var oidcEnabled = !string.IsNullOrWhiteSpace(oidcIssuer) && !string.IsNullOrWhiteSpace(oidcAudience);
if (oidcEnabled)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = oidcIssuer;
            options.Audience = oidcAudience;
            options.RequireHttpsMetadata = true;
        });
    builder.Services.AddAuthorization(options => options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build());
}
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<HubMcpTools>();
builder.Services.AddSingleton<IMcpEventsProbeCallbackClient>(_ => new HttpMcpEventsProbeCallbackClient(new HttpClient
{
    Timeout = TimeSpan.FromSeconds(10),
}));
builder.Services.AddSingleton<IMcpEventsProbeService, McpEventsProbeService>();
if (string.Equals(builder.Configuration["Hub:PersistenceProvider"], "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHubInMemoryPersistence(
        builder.Configuration["Hub:InMemoryDatabase"] ?? "llmhub",
        builder.Configuration["Webhook:SecretEncryptionKey"],
        builder.Configuration["Webhook:PreviousSecretEncryptionKeys"]);
}
else
{
    builder.Services.AddHubPersistence(
        builder.Configuration.GetConnectionString("Hub") ?? "Host=localhost;Port=5432;Database=llmhub;Username=llmhub;Password=local-development-only",
        builder.Configuration["Webhook:SecretEncryptionKey"],
        builder.Configuration["Webhook:PreviousSecretEncryptionKeys"]);
}

if (builder.Configuration["ArtifactStorage:Endpoint"] is { Length: > 0 } artifactEndpoint
    && builder.Configuration["ArtifactStorage:AccessKey"] is { Length: > 0 } artifactAccessKey
    && builder.Configuration["ArtifactStorage:SecretKey"] is { Length: > 0 } artifactSecretKey)
{
    builder.Services.AddHubArtifactStorage(new LlmHub.Infrastructure.Artifacts.ArtifactStorageOptions(
        artifactEndpoint,
        artifactAccessKey,
        artifactSecretKey,
        builder.Configuration["ArtifactStorage:Bucket"] ?? "llm-hub-artifacts",
        builder.Configuration.GetValue<bool>("ArtifactStorage:UseSsl")));
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HubDbContext>>();
    await using var database = await context.CreateDbContextAsync();
    if (database.Database.IsRelational())
    {
        await database.Database.MigrateAsync();
    }
}

if (oidcEnabled)
{
    app.UseAuthentication();
    app.UseMiddleware<ScopeAuthorizationMiddleware>();
    app.UseMiddleware<TenantAuthorizationMiddleware>();
    app.UseAuthorization();
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapOpenApi().AllowAnonymous();
app.MapHubEndpoints();
app.MapWorkerEndpoints();
app.MapSubscriptionEndpoints();
app.MapMcp("/mcp");
if (app.Environment.IsDevelopment())
{
    app.MapMcpEventsProbe();
}

app.Run();

public partial class Program;

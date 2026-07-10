using Microsoft.EntityFrameworkCore;
using RealLifeServer.Api.BackgroundServices;
using RealLifeServer.Api.Controllers;
using RealLifeServer.Api.Extensions;
using RealLifeServer.Api.Hubs;
using RealLifeServer.Api.Middleware;
using RealLifeServer.Api.Realtime;
using RealLifeServer.Application;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Infrastructure;
using RealLifeServer.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// --- Services -----------------------------------------------------------------------------

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhooks"));
builder.Services.AddScoped<ISceneEventBroadcaster, HubSceneEventBroadcaster>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddApiRateLimiting();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddResponseCompression();

var signalR = builder.Services.AddSignalR();
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    // Required once more than one API instance runs behind the load balancer (docs/CONCEPT.md,
    // chapter 8 and 11.2) so SignalR broadcasts reach clients connected to a different instance.
    signalR.AddStackExchangeRedis(redisConnectionString);
}

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres", tags: ["ready"]);

builder.Services.AddHostedService<SceneMonitorHostedService>();
builder.Services.AddHostedService<MetricsRetentionHostedService>();

var app = builder.Build();

// --- Startup: optional one-shot migration ("--migrate") ------------------------------------

if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    return;
}

// --- Startup: fail fast on invalid configuration --------------------------------------------
// Forces IEncryptionService (AesGcmEncryptionService) to construct now, before any hosted
// service starts. Without this, an invalid KEY_PROTECTION_MASTER_KEY only surfaces the first
// time something resolves IStreamOrchestrator - inside SceneMonitorHostedService's 2-second
// tick loop, whose catch-all logs and continues (correct for transient errors, wrong for a
// permanent misconfiguration) - producing the same exception forever instead of one clear,
// immediate startup failure.
using (var startupScope = app.Services.CreateScope())
{
    startupScope.ServiceProvider.GetRequiredService<IEncryptionService>();
}

// --- Middleware pipeline --------------------------------------------------------------------

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<StreamHub>("/hubs/streams");

app.MapHealthChecks("/healthz");
app.MapHealthChecks("/healthz/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;

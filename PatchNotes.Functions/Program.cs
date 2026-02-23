using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PatchNotes.Data;
using PatchNotes.Sync.Core;
using PatchNotes.Sync.Core.AI;
using PatchNotes.Sync.Core.GitHub;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddPatchNotesDbContext(builder.Configuration);

builder.Services.AddGitHubClient(options =>
{
    var token = builder.Configuration["GitHub:Token"];
    if (!string.IsNullOrEmpty(token))
        options.Token = token;
});

builder.Services.AddAiClient(options =>
{
    var section = builder.Configuration.GetSection(AiClientOptions.SectionName);
    var baseUrl = section["BaseUrl"];
    if (!string.IsNullOrEmpty(baseUrl))
        options.BaseUrl = baseUrl;
    options.ApiKey = section["ApiKey"];
    var model = section["Model"];
    if (!string.IsNullOrEmpty(model))
        options.Model = model;
});

builder.Services.AddTransient<ChangelogResolver>();
builder.Services.AddTransient<VersionGroupingService>();
builder.Services.AddTransient<SyncService>();
builder.Services.AddTransient<SummaryGenerationService>();
builder.Services.AddTransient<SyncPipeline>();

var app = builder.Build();

// Validate required configuration and emit startup event
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var config = builder.Configuration;
var missingKeys = new List<string>();

if (string.IsNullOrEmpty(config["GitHub:Token"])) missingKeys.Add("GitHub:Token");
if (string.IsNullOrEmpty(config["AI:ApiKey"])) missingKeys.Add("AI:ApiKey");
if (string.IsNullOrEmpty(config["ConnectionStrings:PatchNotes"]))
    missingKeys.Add("ConnectionStrings:PatchNotes");

foreach (var key in missingKeys)
    logger.LogError("Missing required configuration: {ConfigKey}", key);

var telemetry = app.Services.GetRequiredService<TelemetryClient>();
telemetry.TrackEvent("SyncFunctionStarted", new Dictionary<string, string>
{
    ["configValid"] = (missingKeys.Count == 0).ToString(),
    ["missingKeys"] = string.Join(", ", missingKeys),
});

app.Run();

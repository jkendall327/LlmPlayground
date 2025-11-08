using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoleplaySim.Chatroom;
using RoleplaySim.Llm;
using RoleplaySim.Simulation;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions();
builder.Services.Configure<ChatroomConfig>(builder.Configuration.GetSection("Chatroom"));
builder.Services.Configure<SimulationOptions>(builder.Configuration.GetSection("Simulation"));

var apiKey = builder.Configuration["OpenAI:ApiKey"];
var model = builder.Configuration["OpenAI:Model"] ?? "gpt-5";

if (!string.IsNullOrWhiteSpace(apiKey))
{
    builder.Services.AddSingleton<ILlmClient>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<SemanticKernelLlmClient>>();
        return SemanticKernelLlmClient.CreateOpenAI(apiKey, model, logger);
    });
}
else
{
    builder.Services.AddSingleton<ILlmClient, MockLlmClient>();
}

builder.Services.AddSingleton<Chatroom>();
builder.Services.AddSingleton<ChatSessionRunner>();

using var host = builder.Build();

using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;
var loggerFactory = services.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("Application");

try
{
    logger.LogInformation("Starting roleplay simulation.");

    var runner = services.GetRequiredService<ChatSessionRunner>();
    await runner.RunAsync();

    logger.LogInformation("Simulation completed.");
}
catch (Exception ex)
{
    logger.LogError(ex, "The simulation terminated unexpectedly.");
    throw;
}

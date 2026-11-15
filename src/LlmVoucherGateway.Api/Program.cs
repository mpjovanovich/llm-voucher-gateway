using LlmVoucherGateway.Api.Configuration;
using LlmVoucherGateway.Api.Consumption;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Api.ExceptionHandlers;
using LlmVoucherGateway.Api.Inference;
using LlmVoucherGateway.Api.Middleware;
using LlmVoucherGateway.Api.Observability;
using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Api.Time;
using LlmVoucherGateway.Domain.Abstractions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddSerilogLogging();

builder.Services.AddGatewayOptions();

builder.Services.AddPersistence();

builder.Services.AddInference();

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

builder.Services.AddScoped<ConsumptionRecorder>();

builder.Services.AddProblemDetails();

builder.Services.AddExceptionHandler<ExceptionHandler>();

WebApplication app = builder.Build();

await app.MigrateDatabaseAsync();

// Ahead of the exception handler, so the request line reports the status the handler
// chose and the handler's own log line carries the correlation id.
app.UseRequestLogging();

// Before everything else that can throw, including the credential middleware.
app.UseExceptionHandler();

app.UseMiddleware<CredentialMiddleware>();

app.MapHealthChecks("/health");

app.MapAdminEndpoints();

app.MapModels();

app.MapChatCompletions();

await app.RunAsync();

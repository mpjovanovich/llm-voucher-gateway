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

// First in the pipeline so it covers everything downstream, including the
// credential middleware.
app.UseExceptionHandler();

app.UseMiddleware<CredentialMiddleware>();

app.MapChatCompletions();

app.MapModels();

app.MapAdminEndpoints();

await app.RunAsync();

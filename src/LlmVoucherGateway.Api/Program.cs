using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Api.ExceptionHandlers;
using LlmVoucherGateway.Api.Inference;
using LlmVoucherGateway.Api.Middleware;
using LlmVoucherGateway.Api.Time;
using LlmVoucherGateway.Domain.Abstractions;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// TODO: Extract the DI registrations into theor own classes if this starts to turn this Program file into a mess.
builder.Services
    .AddOptions<AdminOptions>()
    .BindConfiguration(AdminOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<InferenceOptions>()
    .BindConfiguration(InferenceOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExceptionHandler>();

builder.Services.AddHttpClient<IInferenceClient, VllmInferenceClient>((serviceProvider, httpClient) =>
{
    InferenceOptions options = serviceProvider
        .GetRequiredService<IOptions<InferenceOptions>>().Value;

    httpClient.BaseAddress = new Uri(options.BaseUrl);

    // HttpClient's 100-second default would cancel long generations mid-stream and
    // surface as a TaskCanceledException that reads like a network fault. Request
    // lifetime is bounded by the caller's own cancellation instead.
    httpClient.Timeout = Timeout.InfiniteTimeSpan;
});

WebApplication app = builder.Build();

// First in the pipeline so it covers everything downstream, including the
// credential middleware.
app.UseExceptionHandler();

app.UseMiddleware<CredentialMiddleware>();

app.MapChatCompletions();

await app.RunAsync();

using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Api.Inference;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<InferenceOptions>()
    .BindConfiguration(InferenceOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

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

app.MapChatCompletions();

await app.RunAsync();

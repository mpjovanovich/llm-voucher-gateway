using LlmVoucherGateway.Api.Inference;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<InferenceOptions>()
    .BindConfiguration(InferenceOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

WebApplication app = builder.Build();

await app.RunAsync();

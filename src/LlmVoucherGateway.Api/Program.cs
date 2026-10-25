using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Api.ExceptionHandlers;
using LlmVoucherGateway.Api.Inference;
using LlmVoucherGateway.Api.Middleware;
using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Api.Time;
using LlmVoucherGateway.Domain.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

builder.Services
    .AddOptions<VoucherIssuanceOptions>()
    .BindConfiguration(VoucherIssuanceOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<VoucherDbContext>((serviceProvider, dbContextOptions) =>
{
    DatabaseOptions databaseOptions = serviceProvider
        .GetRequiredService<IOptions<DatabaseOptions>>().Value;

    // Built rather than concatenated so a path containing a space or a semicolon
    // cannot alter the rest of the connection string.
    SqliteConnectionStringBuilder connectionString = new()
    {
        DataSource = databaseOptions.Path,
    };

    dbContextOptions.UseSqlite(connectionString.ConnectionString);
});

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

using (IServiceScope scope = app.Services.CreateScope())
{
    DatabaseOptions databaseOptions = scope.ServiceProvider
        .GetRequiredService<IOptions<DatabaseOptions>>().Value;

    // SQLite creates the database file on demand but not the directory holding it.
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databaseOptions.Path))!);

    // Applied on boot so the container is self-sufficient. This is right for one
    // instance and wrong for two, because both would race to apply the same migration.
    await scope.ServiceProvider.GetRequiredService<VoucherDbContext>().Database.MigrateAsync();
}

// First in the pipeline so it covers everything downstream, including the
// credential middleware.
app.UseExceptionHandler();

app.UseMiddleware<CredentialMiddleware>();

app.MapChatCompletions();

// Unprotected until Milestone 5c adds the admin requirement.
app.MapVoucherIssuance();

await app.RunAsync();

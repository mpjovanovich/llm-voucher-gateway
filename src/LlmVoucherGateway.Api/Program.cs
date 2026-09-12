WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

WebApplication app = builder.Build();

await app.RunAsync();

// Make Program visible to the integration tests so that they can instantiate it.
// Otherwise the Program class will be scoped to "internal".
public partial class Program { }
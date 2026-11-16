using B2B.Contracts.Weather;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Typed gRPC client for the partner API (GRPC_B2B). Injected as WeatherB2B.WeatherB2BClient.
var b2b = builder.Configuration.GetSection("B2BClient");
builder.Services
    .AddGrpcClient<WeatherB2B.WeatherB2BClient>(o =>
    {
        o.Address = new Uri(b2b["Address"] ?? throw new InvalidOperationException("B2BClient:Address is not configured"));
    })
    // Attach the partner API key to every call (call credentials require TLS, i.e. an https address).
    .AddCallCredentials((context, metadata) =>
    {
        metadata.Add("x-api-key", b2b["ApiKey"] ?? "");
        return Task.CompletedTask;
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

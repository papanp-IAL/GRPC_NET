# GRPC_NET

A .NET 10 sample solution showing how two ASP.NET Core services talk to each other over **gRPC** in a business-to-business (B2B) setup:

- **GRPC_B2B** is the *partner API*. It hosts a gRPC server that serves weather forecasts and checks every call for a valid API key.
- **GRPC** is the *consumer API*. It exposes ordinary REST endpoints and, behind them, calls GRPC_B2B through a typed gRPC client.

Both services share one contract file, `Protos/weather_b2b.proto`.

```
 HTTP/JSON client                 GRPC (consumer)                     GRPC_B2B (partner)
 (browser, .http, curl)   --->   REST controllers        --gRPC-->    WeatherB2BService
                                  B2BWeatherController    HTTP/2+TLS   + ApiKeyInterceptor
                                  https://localhost:7130  x-api-key    https://localhost:7053
```

## Solution layout

```
GRPC.slnx                      Solution file (both projects)
Protos/
  weather_b2b.proto            Shared gRPC contract (package b2b.weather.v1)
GRPC/                          Consumer: REST API + gRPC client
  Controllers/
    B2BWeatherController.cs    REST endpoints that call GRPC_B2B
    WeatherForecastController.cs  Default template endpoint (local data)
  Program.cs                   Registers the typed gRPC client + API-key call credentials
  GRPC.http                    Ready-made requests for testing
GRPC_B2B/                      Partner: gRPC server
  Services/
    WeatherB2BService.cs       Implements the WeatherB2B service
    ApiKeyInterceptor.cs       Validates the x-api-key header on every call
  Program.cs                   Registers gRPC, the interceptor and the service
docs/
  architecture-prototype.html  Architecture notes / prototype
```

## The gRPC contract

`Protos/weather_b2b.proto` defines one service with two RPCs:

| RPC | Type | Purpose |
|---|---|---|
| `GetForecast(ForecastRequest) → ForecastReply` | Unary | Returns all forecasts for the next *N* days in one reply |
| `StreamForecast(ForecastRequest) → stream Forecast` | Server streaming | Pushes forecasts one at a time (suited to large result sets) |

`ForecastRequest` carries `days` (1–14) and a `partner_ref` correlation id. Each `Forecast` has a date (`google.protobuf.Timestamp`), temperature in °C and °F, and a summary.

The file is compiled into each project by `Grpc.Tools`: GRPC_B2B generates **server** stubs and GRPC generates **client** stubs (see the `<Protobuf>` item in each `.csproj`). C# types live in the `B2B.Contracts.Weather` namespace.

**Evolving the contract:** only add new fields with new field numbers. Never reuse or renumber existing fields, so older partners keep working.

## Tech stack

- .NET 10 (`net10.0`), ASP.NET Core Web API
- `Grpc.AspNetCore` 2.85.0 (server)
- `Grpc.Net.ClientFactory` 2.85.0, `Google.Protobuf` 3.36.2, `Grpc.Tools` 2.85.0 (client)
- `Microsoft.AspNetCore.OpenApi` 10.0.15 (OpenAPI document in Development)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A trusted HTTPS development certificate: `dotnet dev-certs https --trust`
  (gRPC call credentials only work over TLS, so the client must use an `https` address.)
- Optional: Visual Studio 2022+/VS Code with the REST Client extension to run the `.http` files.

## Getting started

1. **Restore and build**

   ```bash
   dotnet build GRPC.slnx
   ```

2. **Start the partner service (GRPC_B2B) first**

   ```bash
   dotnet run --project GRPC_B2B --launch-profile https
   ```

   Listens on `https://localhost:7053` (gRPC + REST) and `http://localhost:5214`.

3. **Start the consumer service (GRPC)** in a second terminal

   ```bash
   dotnet run --project GRPC --launch-profile https
   ```

   Listens on `https://localhost:7130` and `http://localhost:5218`.

   In Visual Studio you can instead open `GRPC.slnx` and use the multi-project launch profile (`GRPC.slnLaunch.user`) to start both at once.

4. **Call the API** using `GRPC/GRPC.http` or curl:

   ```bash
   curl -k "https://localhost:7130/B2BWeather?days=3"
   ```

## REST endpoints (GRPC service)

| Method & path | What it does |
|---|---|
| `GET /B2BWeather?days=5` | Calls `GetForecast` (unary) on GRPC_B2B, 5-second deadline |
| `GET /B2BWeather/stream?days=5` | Calls `StreamForecast` (server streaming), collects results, 10-second deadline |
| `GET /WeatherForecast` | Template endpoint; returns local random data, no gRPC |

Each call forwards the ASP.NET Core `TraceIdentifier` as `partner_ref`, so requests can be correlated in the partner's logs.

Example response:

```json
[
  { "date": "2026-11-18", "temperatureC": 21, "temperatureF": 69, "summary": "Mild" }
]
```

### Error mapping

gRPC errors from the partner are translated into HTTP problem responses:

| gRPC status | HTTP status | Meaning |
|---|---|---|
| `InvalidArgument` | 400 Bad Request | e.g. `days` outside 1–14 |
| `Unauthenticated` / `PermissionDenied` | 502 Bad Gateway | Partner rejected our API key |
| `DeadlineExceeded` | 504 Gateway Timeout | Partner did not answer in time |
| `Unavailable` | 503 Service Unavailable | Partner is down or unreachable |
| anything else | 502 Bad Gateway | Unexpected partner error |

## Configuration

### GRPC (consumer) – `B2BClient` section

| Key | Description |
|---|---|
| `B2BClient:Address` | Base URL of GRPC_B2B. Must be `https`. Startup fails if missing. |
| `B2BClient:ApiKey` | Key sent in the `x-api-key` metadata header on every gRPC call |

### GRPC_B2B (partner) – `B2B` section

| Key | Description |
|---|---|
| `B2B:AllowedApiKeys` | Array of accepted partner keys. Empty by default, so all calls are rejected until keys are configured. |

In Development both sides use the key `dev-partner-key-123` (see the `appsettings.Development.json` files). Detailed gRPC errors are enabled only in Development.

Settings can be overridden with environment variables, e.g.:

```bash
B2BClient__Address=https://partner.example.com
B2BClient__ApiKey=<secret>
B2B__AllowedApiKeys__0=<secret>
```

## Security

- The `ApiKeyInterceptor` checks the `x-api-key` header on both unary and streaming calls, using a constant-time comparison to avoid timing attacks. Missing or unknown keys get `Unauthenticated`.
- Transport is HTTPS/HTTP2 (Kestrel negotiates HTTP/2 via ALPN on the same port).
- **Keep real keys out of source control.** Store production keys in user secrets, environment variables or a secret store (e.g. Azure Key Vault), not in `appsettings.Production.json`. Rotate any key that has already been committed.
- For a production B2B integration, consider mutual TLS (client certificates) or OAuth 2.0 client credentials instead of, or in addition to, API keys.

## Publishing

Each project has a folder publish profile (`Properties/PublishProfiles/FolderProfile.pubxml`, Release configuration). Publish from Visual Studio, or from the command line:

```bash
dotnet publish GRPC_B2B -c Release -o ./publish/GRPC_B2B
dotnet publish GRPC     -c Release -o ./publish/GRPC
```

Run with `ASPNETCORE_ENVIRONMENT=Production` and point `B2BClient:Address` at the deployed GRPC_B2B URL.

## Troubleshooting

- **503 Partner API unavailable** – GRPC_B2B isn't running, or `B2BClient:Address` is wrong.
- **502 Partner API rejected our credentials** – the client's `ApiKey` is not in the server's `AllowedApiKeys` for the current environment.
- **SSL / certificate errors** – run `dotnet dev-certs https --trust`.
- **Call credentials error about insecure channel** – the client address must be `https://`.

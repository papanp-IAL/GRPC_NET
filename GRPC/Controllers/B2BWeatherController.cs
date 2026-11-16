using B2B.Contracts.Weather;
using Grpc.Core;
using GrpcStatus = Grpc.Core.StatusCode;
using Microsoft.AspNetCore.Mvc;

namespace GRPC.Controllers
{
    // REST endpoints on this API that fetch data from the partner API (GRPC_B2B) over gRPC.
    [ApiController]
    [Route("[controller]")]
    public class B2BWeatherController(WeatherB2B.WeatherB2BClient client) : ControllerBase
    {
        // GET /B2BWeather?days=5  -> unary gRPC call
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] int days = 5, CancellationToken ct = default)
        {
            try
            {
                var reply = await client.GetForecastAsync(
                    new ForecastRequest { Days = days, PartnerRef = HttpContext.TraceIdentifier },
                    deadline: DateTime.UtcNow.AddSeconds(5),   // always set a deadline on B2B calls
                    cancellationToken: ct);

                return Ok(reply.Forecasts.Select(ToDto));
            }
            catch (RpcException ex)
            {
                return MapError(ex);
            }
        }

        // GET /B2BWeather/stream?days=5  -> server-streaming gRPC call
        [HttpGet("stream")]
        public async Task<IActionResult> Stream([FromQuery] int days = 5, CancellationToken ct = default)
        {
            try
            {
                using var call = client.StreamForecast(
                    new ForecastRequest { Days = days, PartnerRef = HttpContext.TraceIdentifier },
                    deadline: DateTime.UtcNow.AddSeconds(10),
                    cancellationToken: ct);

                var results = new List<WeatherForecast>();
                await foreach (var f in call.ResponseStream.ReadAllAsync(ct))
                    results.Add(ToDto(f));

                return Ok(results);
            }
            catch (RpcException ex)
            {
                return MapError(ex);
            }
        }

        private static WeatherForecast ToDto(Forecast f) => new()
        {
            Date = DateOnly.FromDateTime(f.Date.ToDateTime()),
            TemperatureC = f.TemperatureC,
            Summary = f.Summary
        };

        private ObjectResult MapError(RpcException ex) => ex.StatusCode switch
        {
            GrpcStatus.InvalidArgument => Problem(ex.Status.Detail, statusCode: StatusCodes.Status400BadRequest),
            GrpcStatus.Unauthenticated or GrpcStatus.PermissionDenied => Problem("Partner API rejected our credentials", statusCode: StatusCodes.Status502BadGateway),
            GrpcStatus.DeadlineExceeded => Problem("Partner API timed out", statusCode: StatusCodes.Status504GatewayTimeout),
            GrpcStatus.Unavailable => Problem("Partner API unavailable", statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Problem(ex.Status.Detail, statusCode: StatusCodes.Status502BadGateway)
        };
    }
}

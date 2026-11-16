using B2B.Contracts.Weather;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GRPC_B2B.Services
{
    // gRPC implementation of the shared B2B contract (Protos/weather_b2b.proto).
    public class WeatherB2BService(ILogger<WeatherB2BService> logger) : WeatherB2B.WeatherB2BBase
    {
        private static readonly string[] Summaries =
        [
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        ];

        public override Task<ForecastReply> GetForecast(ForecastRequest request, ServerCallContext context)
        {
            Validate(request);
            logger.LogInformation("GetForecast for partner ref {Ref}, {Days} days", request.PartnerRef, request.Days);

            var reply = new ForecastReply { PartnerRef = request.PartnerRef };
            reply.Forecasts.AddRange(Build(request.Days));
            return Task.FromResult(reply);
        }

        public override async Task StreamForecast(ForecastRequest request, IServerStreamWriter<Forecast> responseStream, ServerCallContext context)
        {
            Validate(request);
            foreach (var forecast in Build(request.Days))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await responseStream.WriteAsync(forecast, context.CancellationToken);
            }
        }

        private static void Validate(ForecastRequest request)
        {
            if (request.Days is < 1 or > 14)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "days must be between 1 and 14"));
        }

        private static IEnumerable<Forecast> Build(int days) =>
            Enumerable.Range(1, days).Select(i =>
            {
                var tempC = Random.Shared.Next(-20, 55);
                return new Forecast
                {
                    Date = Timestamp.FromDateTime(DateTime.UtcNow.Date.AddDays(i)),
                    TemperatureC = tempC,
                    TemperatureF = 32 + (int)(tempC / 0.5556),
                    Summary = Summaries[Random.Shared.Next(Summaries.Length)]
                };
            });
    }
}

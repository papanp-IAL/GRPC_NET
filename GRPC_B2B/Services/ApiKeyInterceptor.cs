using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace GRPC_B2B.Services
{
    // Rejects any gRPC call that doesn't carry a known partner API key in the "x-api-key" metadata header.
    // For production B2B, consider mTLS (client certificates) or OAuth client-credentials instead of / on top of this.
    public class ApiKeyInterceptor(IConfiguration config) : Interceptor
    {
        public const string HeaderName = "x-api-key";

        private void Authorize(ServerCallContext context)
        {
            var provided = context.RequestHeaders.GetValue(HeaderName);
            var allowed = config.GetSection("B2B:AllowedApiKeys").Get<string[]>() ?? [];

            var ok = provided is not null && allowed.Any(k =>
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(provided)));

            if (!ok)
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid or missing API key"));
        }

        public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            Authorize(context);
            return continuation(request, context);
        }

        public override Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
            ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            Authorize(context);
            return continuation(request, responseStream, context);
        }
    }
}

using System;
using System.Net.Http;

namespace TsuruLauncher.Services
{
    public static class HttpClientFactory
    {
        private static readonly Lazy<HttpClient> _lazyClient = new(() =>
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 20,
                EnableMultipleHttp2Connections = true
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TsuruLauncher/2.0");
            return client;
        }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public static HttpClient Client => _lazyClient.Value;
    }
}
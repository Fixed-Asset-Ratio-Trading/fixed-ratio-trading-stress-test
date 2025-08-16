using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Abstractions;

namespace FixedRatioStressTest.Api.Middleware
{
    public sealed class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var request = context.Request;
            var sw = Stopwatch.StartNew();

            string path = request.Path.HasValue ? request.Path.Value! : "/";
            string query = request.QueryString.HasValue ? request.QueryString.Value! : string.Empty;
            string method = request.Method;

            string? bodyPreview = null;
            if (request.ContentLength is > 0 &&
                string.Equals(request.Method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.Method, HttpMethods.Put, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    request.EnableBuffering();
                    using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    char[] buffer = new char[2048];
                    int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                    bodyPreview = new string(buffer, 0, read);
                    request.Body.Position = 0;
                }
                catch
                {
                    bodyPreview = "<unable to read request body>";
                }
            }

            if (bodyPreview is null)
            {
                _logger.LogInformation("HTTP {Method} {Path}{Query}", method, path, query);
            }
            else
            {
                _logger.LogInformation("HTTP {Method} {Path}{Query} Body: {Body}", method, path, query, bodyPreview);
            }

            await _next(context);

            sw.Stop();
            int status = context.Response?.StatusCode ?? 0;
            _logger.LogInformation("HTTP {Method} {Path}{Query} -> {Status} in {ElapsedMs}ms", method, path, query, status, sw.ElapsedMilliseconds);
        }
    }
}



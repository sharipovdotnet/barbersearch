using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет настройки сжатия ответов и таймаутов запроса (R-14).
/// </summary>
/// <remarks>
/// Тесты не поднимают Kestrel: параметры middleware проверяются напрямую, а health-эндпоинты —
/// через собранный <see cref="WebApplication"/>, у которого ещё не запущен сервер.
/// </remarks>
public class HttpPipelineTests
{
    [Fact]
    public void ResponseCompression_AllowsJsonAndTextButNotImagesOrArchives()
    {
        var options = new ResponseCompressionOptions();

        ResponseCompressionSettings.Configure(options);

        Assert.True(options.EnableForHttps);
        Assert.Contains("application/json", options.MimeTypes);
        Assert.Contains("application/problem+json", options.MimeTypes);
        Assert.Contains("text/plain", options.MimeTypes);
        Assert.Contains("text/html", options.MimeTypes);

        // Wildcard-типы middleware не понимает, поэтому список точный; изображения и уже
        // сжатые форматы в него попасть не должны.
        Assert.DoesNotContain("image/png", options.MimeTypes);
        Assert.DoesNotContain("image/jpeg", options.MimeTypes);
        Assert.DoesNotContain("application/zip", options.MimeTypes);
        Assert.DoesNotContain("application/gzip", options.MimeTypes);
        Assert.DoesNotContain("font/woff2", options.MimeTypes);
    }

    [Fact]
    public void ResponseCompression_CompressesJsonOverHttpsAndSkipsImage()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddResponseCompression(ResponseCompressionSettings.Configure);
        using var provider = services.BuildServiceProvider();
        var compression = provider.GetRequiredService<IResponseCompressionProvider>();

        var json = CreateContext("gzip", "application/json; charset=utf-8", isHttps: true);
        Assert.True(compression.ShouldCompressResponse(json));

        var html = CreateContext("br", "text/html; charset=utf-8", isHttps: false);
        Assert.True(compression.ShouldCompressResponse(html));

        var image = CreateContext("gzip", "image/png", isHttps: false);
        Assert.False(compression.ShouldCompressResponse(image));

        var archive = CreateContext("gzip", "application/zip", isHttps: false);
        Assert.False(compression.ShouldCompressResponse(archive));
    }

    [Fact]
    public void RequestTimeout_DefaultPolicyIsThirtySeconds()
    {
        var options = new RequestTimeoutOptions();

        RequestTimeoutSettings.Configure(options);

        Assert.NotNull(options.DefaultPolicy);
        Assert.Equal(TimeSpan.FromSeconds(30), options.DefaultPolicy.Timeout);
    }

    [Fact]
    public async Task HealthEndpoints_AreExcludedFromDefaultRequestTimeout()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRequestTimeouts(RequestTimeoutSettings.Configure);
        builder.Services.AddHealthChecks();
        await using var app = builder.Build();
        app.MapAppHealthChecks();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is "/health/live" or "/health/ready")
            .ToArray();

        Assert.Equal(2, endpoints.Length);

        foreach (var endpoint in endpoints)
        {
            var policy = endpoint.Metadata.GetMetadata<RequestTimeoutPolicy>();
            Assert.NotNull(policy);
            Assert.Equal(Timeout.InfiniteTimeSpan, policy.Timeout);
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }

    private static DefaultHttpContext CreateContext(string acceptEncoding, string contentType, bool isHttps)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = isHttps ? "https" : "http";
        context.Request.Headers.AcceptEncoding = acceptEncoding;
        context.Response.ContentType = contentType;

        return context;
    }
}

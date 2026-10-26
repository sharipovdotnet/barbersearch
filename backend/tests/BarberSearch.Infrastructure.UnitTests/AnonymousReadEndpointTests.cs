using BarberSearch.Web.Controllers;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет витрину без входа (R-25): три GET-действия <c>BarbersController</c> и список
/// отзывов помечены <c>[AllowAnonymous]</c> и ограничены политикой <c>reads</c>, а
/// <c>GET api/bookings</c>, создание отзывов и остальные write-эндпоинты остаются закрытыми.
/// </summary>
/// <remarks>
/// Kestrel не поднимается: <see cref="WebApplication"/> собирается и маршрутизируется, но
/// сервер не запускается. Metadata читается с <see cref="RouteEndpoint"/> после
/// <c>MapControllers</c> — то же, что видит middleware в рантайме.
/// </remarks>
public class AnonymousReadEndpointTests
{
    private const string GetMethod = "GET";
    private const string PostMethod = "POST";

    private static readonly string[] AnonymousReadRoutes =
    [
        "api/barbers",
        "api/barbers/{barberProfileId:guid}",
        "api/barbers/{barberProfileId:guid}/slots",
        "api/reviews"
    ];

    [Fact]
    public void AnonymousReadEndpoints_AreAllowAnonymousAndUseReadsPolicy()
    {
        var endpoints = BuildEndpoints();

        foreach (var route in AnonymousReadRoutes)
        {
            var endpoint = FindEndpoint(endpoints, GetMethod, route);

            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());

            var rateLimiting = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
            Assert.NotNull(rateLimiting);
            Assert.Equal(RateLimitSettings.ReadsPolicyName, rateLimiting.PolicyName);
        }
    }

    [Fact]
    public void ClosedEndpoints_AreNotAllowAnonymousAndDoNotUseReadsPolicy()
    {
        var endpoints = BuildEndpoints();

        // GET api/bookings — личные брони, POST api/reviews — запись от имени пользователя:
        // оба должны остаться за fallback-политикой, без публичной политики чтения.
        foreach (var (method, route) in new[]
                 {
                     (GetMethod, "api/bookings"),
                     (PostMethod, "api/reviews")
                 })
        {
            var endpoint = FindEndpoint(endpoints, method, route);

            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());

            var rateLimiting = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
            Assert.True(
                rateLimiting is null || rateLimiting.PolicyName != RateLimitSettings.ReadsPolicyName,
                $"{method} {route} не должен использовать политику reads.");
        }
    }

    private static RouteEndpoint FindEndpoint(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route)
    {
        // RawText у атрибутной маршрутизации приходит без ведущего слэша; TrimStart делает
        // проверку устойчивой к обеим формам, а HttpMethodMetadata отделяет GET от POST
        // на одном и том же шаблоне.
        var matches = endpoints
            .Where(endpoint => string.Equals(
                endpoint.RoutePattern.RawText?.TrimStart('/'),
                route,
                StringComparison.Ordinal))
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                .Contains(method, StringComparer.Ordinal) == true)
            .ToArray();

        Assert.True(matches.Length == 1, $"Ожидался один {method} {route}, найдено {matches.Length}.");

        return matches[0];
    }

    private static RouteEndpoint[] BuildEndpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(BarbersController).Assembly);
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(RateLimitSettings.Configure);

        using var app = builder.Build();
        app.MapControllers();

        // DataSources материализуют endpoints лениво; перечисляем их до Dispose приложения.
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
    }
}

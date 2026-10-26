using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет настройки rate limiting (R-22, R-25): лимит записи 30/мин на <c>sub</c> или IP,
/// публичное чтение 120/мин на IP, неизменный auth-лимит 10/мин и ответ <c>429</c>
/// с <c>Retry-After</c>.
/// </summary>
/// <remarks>
/// Kestrel не поднимается: партиции проверяются через <see cref="PartitionedRateLimiter{TResource}"/>,
/// а <see cref="RateLimitSettings.OnRejected"/> вызывается напрямую с <see cref="DefaultHttpContext"/>
/// и <c>AddProblemDetails()</c> в <see cref="HttpContext.RequestServices"/>.
/// </remarks>
public class RateLimitSettingsTests
{
    private const string UserId = "11111111-1111-1111-1111-111111111111";
    private const string OtherUserId = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void Limits_AreTenAuthOneHundredTwentyReadsAndThirtyWritesPerMinute()
    {
        Assert.Equal("auth", RateLimitSettings.AuthPolicyName);
        Assert.Equal(10, RateLimitSettings.AuthPermitLimit);
        Assert.Equal("reads", RateLimitSettings.ReadsPolicyName);
        Assert.Equal(120, RateLimitSettings.ReadPermitLimit);
        Assert.Equal(30, RateLimitSettings.WritePermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), RateLimitSettings.Window);
    }

    [Fact]
    public void IsWriteMethod_TrueOnlyForPostPutPatchDelete()
    {
        Assert.True(RateLimitSettings.IsWriteMethod(HttpMethods.Post));
        Assert.True(RateLimitSettings.IsWriteMethod(HttpMethods.Put));
        Assert.True(RateLimitSettings.IsWriteMethod(HttpMethods.Patch));
        Assert.True(RateLimitSettings.IsWriteMethod(HttpMethods.Delete));

        Assert.False(RateLimitSettings.IsWriteMethod(HttpMethods.Get));
        Assert.False(RateLimitSettings.IsWriteMethod(HttpMethods.Head));
        Assert.False(RateLimitSettings.IsWriteMethod(HttpMethods.Options));
        Assert.False(RateLimitSettings.IsWriteMethod(HttpMethods.Trace));

        // String-перегрузка читает тот же список методов, HttpMethod-перегрузка — тоже.
        Assert.True(RateLimitSettings.IsWriteMethod("POST"));
        Assert.False(RateLimitSettings.IsWriteMethod("GET"));
        Assert.True(RateLimitSettings.IsWriteMethod(HttpMethod.Put));
        Assert.False(RateLimitSettings.IsWriteMethod(HttpMethod.Head));
    }

    [Fact]
    public void WritePartitionKey_PrefersUserClaimThenIpThenUnknown()
    {
        var authenticated = CreateContext(HttpMethods.Post);
        authenticated.User = CreatePrincipal(UserId);

        var userKey = RateLimitSettings.WritePartitionKey(authenticated);
        Assert.Equal("user:" + UserId, userKey);
        Assert.Equal(userKey, RateLimitSettings.WritePartition(authenticated).PartitionKey);

        var byIp = CreateContext(HttpMethods.Post);
        byIp.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Equal("ip:203.0.113.7", RateLimitSettings.WritePartitionKey(byIp));
        Assert.Equal("ip:203.0.113.7", RateLimitSettings.WritePartition(byIp).PartitionKey);

        var withoutIp = CreateContext(HttpMethods.Post);
        withoutIp.Connection.RemoteIpAddress = null;

        Assert.Equal("unknown", RateLimitSettings.WritePartitionKey(withoutIp));
        Assert.Equal("unknown", RateLimitSettings.WritePartition(withoutIp).PartitionKey);
    }

    [Fact]
    public void WritePartition_LimitsThirtyWritesPerMinutePerClient()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RateLimitSettings.WritePartition);

        var user = CreateContext(HttpMethods.Post);
        user.User = CreatePrincipal(UserId);

        for (var attempt = 1; attempt <= RateLimitSettings.WritePermitLimit; attempt++)
        {
            using var lease = limiter.AttemptAcquire(user);
            Assert.True(lease.IsAcquired, $"Запрос {attempt} должен пройти.");
        }

        using (var rejected = limiter.AttemptAcquire(user))
        {
            Assert.False(rejected.IsAcquired);
        }

        // Партиция другого пользователя не расходуется лимитом первого.
        var otherUser = CreateContext(HttpMethods.Post);
        otherUser.User = CreatePrincipal(OtherUserId);

        using var otherLease = limiter.AttemptAcquire(otherUser);
        Assert.True(otherLease.IsAcquired);
    }

    [Fact]
    public void WritePartition_DoesNotLimitSafeMethods()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RateLimitSettings.WritePartition);

        var read = CreateContext(HttpMethods.Get);
        read.User = CreatePrincipal(UserId);

        for (var attempt = 1; attempt <= 100; attempt++)
        {
            using var lease = limiter.AttemptAcquire(read);
            Assert.True(lease.IsAcquired, $"Чтение {attempt} не должно ограничиваться.");
        }

        Assert.Equal("no-limit", RateLimitSettings.WritePartition(read).PartitionKey);
    }

    [Fact]
    public void AuthPartition_LimitsTenAttemptsPerMinutePerIp()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RateLimitSettings.AuthPartition);

        var fromOneIp = CreateContext(HttpMethods.Post);
        fromOneIp.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");

        for (var attempt = 1; attempt <= RateLimitSettings.AuthPermitLimit; attempt++)
        {
            using var lease = limiter.AttemptAcquire(fromOneIp);
            Assert.True(lease.IsAcquired, $"Попытка {attempt} должна пройти.");
        }

        using (var rejected = limiter.AttemptAcquire(fromOneIp))
        {
            Assert.False(rejected.IsAcquired);
        }

        var fromOtherIp = CreateContext(HttpMethods.Post);
        fromOtherIp.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.2");

        using var otherLease = limiter.AttemptAcquire(fromOtherIp);
        Assert.True(otherLease.IsAcquired);
    }

    [Fact]
    public void ReadsPartition_LimitsOneHundredTwentyReadsPerMinutePerIp()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RateLimitSettings.ReadsPartition);

        var fromOneIp = CreateContext(HttpMethods.Get);
        fromOneIp.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");

        for (var attempt = 1; attempt <= RateLimitSettings.ReadPermitLimit; attempt++)
        {
            using var lease = limiter.AttemptAcquire(fromOneIp);
            Assert.True(lease.IsAcquired, $"Чтение {attempt} должно пройти.");
        }

        using (var rejected = limiter.AttemptAcquire(fromOneIp))
        {
            Assert.False(rejected.IsAcquired);
        }

        // Партиция другого IP не расходуется лимитом первого.
        var fromOtherIp = CreateContext(HttpMethods.Get);
        fromOtherIp.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.11");

        using var otherLease = limiter.AttemptAcquire(fromOtherIp);
        Assert.True(otherLease.IsAcquired);
    }

    [Fact]
    public async Task Configure_OnRejectedWrites429ProblemDetailsWithRetryAfterAndErrorCode()
    {
        var options = new RateLimiterOptions();
        RateLimitSettings.Configure(options);

        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);
        Assert.NotNull(options.GlobalLimiter);
        Assert.NotNull(options.OnRejected);

        await using var provider = CreateProblemDetailsProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();

        await options.OnRejected!(
            new OnRejectedContext
            {
                HttpContext = context,
                Lease = new RejectedLease(TimeSpan.FromSeconds(30))
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("30", context.Response.Headers.RetryAfter.ToString());
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.Contains(ApiErrorCodes.Common.RateLimitExceeded, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status429TooManyRequests, root.GetProperty("status").GetInt32());

        var errorCodes = root.GetProperty("errorCodes")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();

        Assert.Contains(ApiErrorCodes.Common.RateLimitExceeded, errorCodes);
    }

    private static DefaultHttpContext CreateContext(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;

        return context;
    }

    private static ClaimsPrincipal CreatePrincipal(string userId) => new(
        new ClaimsIdentity([new Claim(ClaimsPrincipalFactory.SUB_CLAIM, userId)], "test"));

    private static ServiceProvider CreateProblemDetailsProvider()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddProblemDetails();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Отклонённый lease с метаданными <c>RetryAfter</c>: ровно то, что middleware получает
    /// от fixed window limiter, исчерпавшего лимит.
    /// </summary>
    private sealed class RejectedLease : RateLimitLease
    {
        private readonly TimeSpan _retryAfter;

        public RejectedLease(TimeSpan retryAfter) => _retryAfter = retryAfter;

        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = _retryAfter;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}

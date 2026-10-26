using System.Reflection;
using BarberSearch.Application.Users.LoginUser;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.UnitTests.Fakes;
using BarberSearch.Web.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// <see cref="JwtTokenIssuer"/> берёт «сейчас» из <see cref="TimeProvider"/> (R-27): и <c>iat</c>,
/// и <c>exp</c> считаются от подменяемых часов, а не от системных.
/// </summary>
/// <remarks>
/// JWT хранит время в секундах, поэтому сравнение идёт с точностью до секунды. Фиксированное
/// время теста (2025-03-04) заведомо отличается от системного: если вернуть
/// <c>DateTime.UtcNow</c>, первый тест падает на <c>iat</c>/<c>exp</c>.
/// </remarks>
public class JwtTokenIssuerTests
{
    private static readonly DateTimeOffset FakeNow =
        new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Fact]
    public void Issue_FillsIssuedAtAndExpiresFromTimeProvider()
    {
        var issuer = CreateIssuer(FakeNow);

        var response = issuer.Issue(CreateUser());
        var token = new JsonWebToken(response.AccessToken);

        var expectedIssuedAt = FakeNow.UtcDateTime;
        var expectedExpires = FakeNow.AddMinutes(15).UtcDateTime;

        AssertWithinOneSecond(expectedIssuedAt, token.IssuedAt);
        AssertWithinOneSecond(expectedExpires, token.ValidTo);

        // Срок в ответе по-прежнему считается из настроек, а не из разницы часов.
        Assert.Equal(900, response.ExpiresInSeconds);
    }

    [Fact]
    public void Issue_WhenClockMovesForwardOneHour_BothIatAndExpMoveByOneHour()
    {
        var baseline = new JsonWebToken(
            CreateIssuer(FakeNow).Issue(CreateUser()).AccessToken);
        var shifted = new JsonWebToken(
            CreateIssuer(FakeNow.AddHours(1)).Issue(CreateUser()).AccessToken);

        // Ровно 3600 секунд: часы взяты из TimeProvider, а не из системного времени,
        // которое при двух вызовах подряд сдвинулось бы на секунды, а не на час.
        Assert.Equal(3600d, (shifted.ValidTo - baseline.ValidTo).TotalSeconds);
        Assert.Equal(3600d, (shifted.IssuedAt - baseline.IssuedAt).TotalSeconds);
    }

    /// <summary>
    /// Обработчик JWT — один на процесс, а не на токен (R-31): <see cref="JsonWebTokenHandler"/>
    /// потокобезопасен, поэтому поле обязано быть <c>static readonly</c>. Тест падает, если поле
    /// убрать, сделать экземплярным или изменяемым: тогда обработчик снова создаётся на каждый
    /// выпуск токена (или его можно подменить).
    /// </summary>
    [Fact]
    public void Handler_IsSingleStaticReadonlyInstance()
    {
        var handlerFields = typeof(JwtTokenIssuer)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(JsonWebTokenHandler))
            .ToList();

        var handlerField = Assert.Single(handlerFields);

        Assert.True(handlerField.IsStatic);
        Assert.True(handlerField.IsInitOnly);

        var handler = handlerField.GetValue(null);
        Assert.NotNull(handler);
        Assert.IsType<JsonWebTokenHandler>(handler);
    }

    private static JwtTokenIssuer CreateIssuer(DateTimeOffset now)
    {
        var options = new AuthOptions
        {
            Issuer = "BarberSearch.Tests",
            Audience = "BarberSearch.Tests.Api",
            SigningKey = new string('k', 32),
            AccessTokenMinutes = 15
        };

        return new JwtTokenIssuer(
            options,
            new JwtSigningKeys(options),
            new FakeTimeProvider(now));
    }

    private static AuthenticatedUser CreateUser()
        => new(
            Guid.CreateVersion7(),
            "customer@example.com",
            UserRole.Customer,
            null,
            IsEmailVerified: true,
            SecurityStamp: "stamp");

    private static void AssertWithinOneSecond(DateTime expected, DateTime actual)
        => Assert.True(
            Math.Abs((actual - expected).TotalSeconds) <= 1,
            $"Expected {expected:O} ± 1s, actual {actual:O}.");
}

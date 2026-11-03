using System.Net;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Запись согласия как значение: нормализация версии и времени, допустимый <c>null</c> IP
/// и отказ на пустую или слишком длинную редакцию.
/// </summary>
public class UserConsentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsPolicyVersion()
    {
        var result = UserConsent.Create("  2026-11-03  ", Now, null);

        Assert.True(result.IsSuccess);
        Assert.Equal("2026-11-03", result.Value.PolicyVersion);
    }

    [Fact]
    public void Create_ConvertsTimeToUtc()
    {
        var local = new DateTimeOffset(2026, 11, 3, 17, 5, 0, TimeSpan.FromHours(3));

        var result = UserConsent.Create("2026-11-03", local, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.Zero, result.Value.ConsentedAt.Offset);
        Assert.Equal(local.ToUniversalTime(), result.Value.ConsentedAt);
    }

    [Fact]
    public void Create_AllowsNullIpAddress()
    {
        var result = UserConsent.Create("2026-11-03", Now, null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.IpAddress);
    }

    [Fact]
    public void Create_KeepsIpAddress()
    {
        var ip = IPAddress.Parse("203.0.113.7");

        var result = UserConsent.Create("2026-11-03", Now, ip);

        Assert.True(result.IsSuccess);
        Assert.Equal(ip, result.Value.IpAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyPolicyVersion_ReturnsValidationFailure(string? policyVersion)
    {
        var result = UserConsent.Create(policyVersion, Now, null);

        Assert.True(result.IsFailure);
        Assert.Equal("user.consent.policyVersion", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithTooLongPolicyVersion_ReturnsValidationFailure()
    {
        var tooLong = new string('a', UserConsent.MAX_POLICY_VERSION_LENGTH + 1);

        var result = UserConsent.Create(tooLong, Now, null);

        Assert.True(result.IsFailure);
        Assert.Equal("user.consent.policyVersion", result.Error.Errors[0].Code);
    }
}

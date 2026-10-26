using System.Security.Claims;
using BarberSearch.Application.Abstractions;
using BarberSearch.Web.Auth;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Веб-адаптер <see cref="ICurrentUser"/> не бросает исключение на анонимном запросе (R-29).
/// </summary>
public class HttpContextCurrentUserTests
{
    [Fact]
    public void TryGetUserId_WithoutSubClaim_ReturnsFalseAndDoesNotThrow()
    {
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        });

        var found = currentUser.TryGetUserId(out var userId);

        Assert.False(found);
        Assert.Equal(Guid.Empty, userId);
    }

    [Fact]
    public void TryGetUserId_WithoutHttpContext_ReturnsFalseAndDoesNotThrow()
    {
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor());

        var found = currentUser.TryGetUserId(out var userId);

        Assert.False(found);
        Assert.Equal(Guid.Empty, userId);
    }

    [Fact]
    public void TryGetUserId_WithSubClaim_ReturnsUserId()
    {
        var expected = Guid.CreateVersion7();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimsPrincipalFactory.SUB_CLAIM, expected.ToString())],
                authenticationType: "Test"))
        };
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = context
        });

        var found = currentUser.TryGetUserId(out var userId);

        Assert.True(found);
        Assert.Equal(expected, userId);
    }
}

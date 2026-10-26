using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Http;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Маппинг <see cref="ErrorType"/> → HTTP (R-40): каждое значение перечисления имеет явный
/// код состояния, а набор ключей совпадает с набором значений enum.
/// </summary>
/// <remarks>
/// Проект ссылается на <c>BarberSearch.Web</c>, поэтому тест проверяет настоящий
/// <see cref="FailureProblemDetails.ToStatusCode"/>, а не его копию. Тест на совпадение наборов
/// падает при добавлении нового <see cref="ErrorType"/> без соответствующей записи в словаре.
/// </remarks>
public class ErrorMappingTests
{
    private static readonly IReadOnlyDictionary<ErrorType, int> ExpectedStatusCodes =
        new Dictionary<ErrorType, int>
        {
            [ErrorType.VALIDATION] = StatusCodes.Status400BadRequest,
            [ErrorType.UNAUTHORIZED] = StatusCodes.Status401Unauthorized,
            [ErrorType.FORBIDDEN] = StatusCodes.Status403Forbidden,
            [ErrorType.NOT_FOUND] = StatusCodes.Status404NotFound,
            [ErrorType.CONFLICT] = StatusCodes.Status409Conflict,
            [ErrorType.FAILURE] = StatusCodes.Status500InternalServerError
        };

    [Fact]
    public void ToStatusCode_MapsEveryEnumValue()
    {
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            Assert.True(
                ExpectedStatusCodes.ContainsKey(type),
                $"No expected HTTP status is registered for ErrorType.{type}.");
            Assert.Equal(ExpectedStatusCodes[type], FailureProblemDetails.ToStatusCode(type));
        }
    }

    [Fact]
    public void ExpectedStatusCodes_CoverEveryEnumValue()
    {
        var enumValues = Enum.GetValues<ErrorType>().OrderBy(value => value);
        var mappedValues = ExpectedStatusCodes.Keys.OrderBy(value => value);

        Assert.Equal(enumValues, mappedValues);
    }

    [Fact]
    public void Failure_MapsToInternalServerError()
    {
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            FailureProblemDetails.ToStatusCode(ErrorType.FAILURE));
    }

    [Fact]
    public void UndefinedEnumValue_MapsToInternalServerError()
    {
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            FailureProblemDetails.ToStatusCode((ErrorType)int.MaxValue));
    }
}

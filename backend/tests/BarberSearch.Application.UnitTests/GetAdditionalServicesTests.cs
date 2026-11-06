using BarberSearch.Application.BarberProfiles.GetAdditionalServices;
using BarberSearch.Application.UnitTests.Fakes;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Подсказки фильтра «дополнительные услуги»: обработчик отдаёт значения репозитория как есть.
/// </summary>
public class GetAdditionalServicesTests
{
    private static readonly string[] Expected =
        ["Бритьё головы", "Уход за бородой"];

    [Fact]
    public async Task Handle_ReturnsRepositoryValuesUnchanged()
    {
        var repository = new FakeBarberProfileRepository
        {
            AdditionalServices = Expected
        };
        var handler = new GetAdditionalServicesHandler(repository);

        var result = await handler.Handle(
            new GetAdditionalServicesQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Expected, result.Value);
    }
}

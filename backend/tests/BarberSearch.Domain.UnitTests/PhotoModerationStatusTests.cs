using BarberSearch.Domain.Common;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Числовые значения общего статуса премодерации стабильны для базы: они лежат в
/// <c>smallint</c>-колонках и закреплены check-ограничениями, поэтому перенумерация —
/// это миграция данных, а не рефакторинг.
/// </summary>
public class PhotoModerationStatusTests
{
    [Fact]
    public void Values_AreStableForTheDatabase()
    {
        Assert.Equal(1, (int)PhotoModerationStatus.Pending);
        Assert.Equal(2, (int)PhotoModerationStatus.Approved);
        Assert.Equal(3, (int)PhotoModerationStatus.Rejected);
    }

    [Fact]
    public void Values_AreTheOnlyDefinedOnes()
    {
        Assert.Equal(
            [
                PhotoModerationStatus.Pending,
                PhotoModerationStatus.Approved,
                PhotoModerationStatus.Rejected
            ],
            Enum.GetValues<PhotoModerationStatus>());
    }
}

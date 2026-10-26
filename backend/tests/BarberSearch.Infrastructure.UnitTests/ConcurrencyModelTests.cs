using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.BarberSpecializations;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.FavoriteBarbers;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.RefreshTokens;
using BarberSearch.Domain.Reviews;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Specializations;
using BarberSearch.Domain.TimeOffs;
using BarberSearch.Domain.Users;
using BarberSearch.Domain.WorkingSchedules;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет, что модель EF Core действительно содержит токены конкурентности <c>xmin</c>
/// у сущностей агрегатов (R-05), — без подключения к базе.
/// </summary>
public class ConcurrencyModelTests
{
    [Fact]
    public void AggregateEntities_HaveXminConcurrencyToken()
    {
        using var context = new ApplicationDbContext(CreateOptions());
        var model = context.Model;

        // Корни и их дочерние строки: UPDATE ребёнка (расписание, фото, услуга) не трогает
        // строку корня, поэтому корневого токена для агрегата недостаточно.
        Type[] aggregateEntities =
        [
            typeof(User),
            typeof(FavoriteBarber),
            typeof(BarberProfile),
            typeof(BarberSalon),
            typeof(WorkingSchedule),
            typeof(BarberService),
            typeof(BarberSpecialization),
            typeof(PortfolioPhoto),
            typeof(TimeOff),
            typeof(Booking),
            typeof(Review),
            typeof(Salon),
            typeof(HairService),
            typeof(Specialization)
        ];

        foreach (var aggregateEntity in aggregateEntities)
        {
            var entityType = model.FindEntityType(aggregateEntity);

            Assert.NotNull(entityType);

            var xmin = entityType.FindProperty("xmin");

            Assert.NotNull(xmin);
            Assert.Equal(typeof(uint), xmin.ClrType);
            Assert.True(xmin.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, xmin.ValueGenerated);
            Assert.Equal("xmin", xmin.GetColumnName());
        }
    }

    [Fact]
    public void RefreshToken_HasNoXminToken()
    {
        // Refresh-токены не входят в R-05: их гонку сериализует условный UPDATE (R-07),
        // а не оптимистическая блокировка на уровне EF Core.
        using var context = new ApplicationDbContext(CreateOptions());
        var refreshToken = context.Model.FindEntityType(typeof(RefreshToken));

        Assert.NotNull(refreshToken);
        Assert.Null(refreshToken.FindProperty("xmin"));
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test")
            .Options;
}

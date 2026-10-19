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
using Microsoft.EntityFrameworkCore;

namespace BarberSearch.Infrastructure.Postgresql;

/// <summary>
/// Контекст базы данных BarberSearch (PostgreSQL).
/// </summary>
/// <remarks>
/// Провайдер подключается снаружи — при регистрации в DI:
/// <c>options.UseNpgsql(connectionString)</c>. Здесь его нет сознательно: строка подключения
/// не должна попадать в код инфраструктуры.
/// Конфигурации таблиц лежат в папке <c>Configurations</c> и подключаются целиком —
/// добавлять новую сущность достаточно вместе с её <c>IEntityTypeConfiguration</c>.
/// </remarks>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Агрегатные корни
    public DbSet<User> Users => Set<User>();
    public DbSet<BarberProfile> BarberProfiles => Set<BarberProfile>();
    public DbSet<Salon> Salons => Set<Salon>();
    public DbSet<HairService> HairServices => Set<HairService>();
    public DbSet<Specialization> Specializations => Set<Specialization>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Review> Reviews => Set<Review>();

    // Аутентификация
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Дочерние сущности агрегатов
    public DbSet<PortfolioPhoto> PortfolioPhotos => Set<PortfolioPhoto>();
    public DbSet<TimeOff> TimeOffs => Set<TimeOff>();
    public DbSet<FavoriteBarber> FavoriteBarbers => Set<FavoriteBarber>();
    public DbSet<BarberSalon> BarberSalons => Set<BarberSalon>();
    public DbSet<WorkingSchedule> WorkingSchedules => Set<WorkingSchedule>();
    public DbSet<BarberService> BarberServices => Set<BarberService>();
    public DbSet<BarberSpecialization> BarberSpecializations => Set<BarberSpecialization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // citext нужен имени специализации: уникальность без учёта регистра
        // («Стрижка» / «стрижка» — один и тот же тег справочника).
        modelBuilder.HasPostgresExtension("citext");

        // btree_gist нужен exclusion-ограничению броней: равенство barber_profile_id
        // должно стоять в одном GiST-индексе с пересечением интервалов.
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}

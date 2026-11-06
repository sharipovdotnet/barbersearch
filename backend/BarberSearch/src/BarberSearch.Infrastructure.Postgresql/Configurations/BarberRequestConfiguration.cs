using BarberSearch.Domain;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.BarberProfiles.ValueObjects;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>barber_requests</c> — агрегатного корня заявки в барберы.
/// </summary>
/// <remarks>
/// <para>
/// Объекты-значения <see cref="DisplayName"/> и <see cref="YearsOfExperience"/> — owned-типы
/// в этой же таблице: те же правила 1..120 и 1..70, что у профиля барбера, поэтому домен
/// переиспользует готовые VO, а не дублирует проверки.
/// </para>
/// <para>
/// Частичный уникальный индекс <c>ux_barber_requests_pending_user</c> — «одна активная заявка
/// на пользователя». Он ловит гонку двух одновременных подач, которую не видит предпроверка;
/// <c>README</c>-комментарий к коду ошибки — <c>barberRequest.create.alreadyPending</c>.
/// </para>
/// <para>
/// Заявка сознательно **не** входит в allowlist <c>audit_log</c> (R-34). Смена роли при
/// одобрении уже пишется как <c>user.updated</c> с полем <c>role</c>, а сама заявка хранит
/// персональные данные — город и текст «о себе», — которым не место в журнале изменений.
/// Поэтому <c>AuditTrail</c> не расширяется, и запись заявки строк аудита не создаёт.
/// </para>
/// </remarks>
public class BarberRequestConfiguration : IEntityTypeConfiguration<BarberRequest>
{
    public void Configure(EntityTypeBuilder<BarberRequest> builder)
    {
        builder.ToTable("barber_requests", table =>
        {
            table.HasCheckConstraint(
                "ck_barber_requests_status",
                CheckConstraintSql.EnumIn<BarberRequestStatus>("status"));
            table.HasCheckConstraint(
                "ck_barber_requests_display_name",
                FormattableString.Invariant(
                    $"char_length(display_name) BETWEEN 1 AND {DisplayName.MAX_LENGTH}"));
            table.HasCheckConstraint(
                "ck_barber_requests_years_of_experience",
                FormattableString.Invariant(
                    $"years_of_experience BETWEEN {Constants.MIN_YEARS_OF_EXPERIENCE} AND {Constants.MAX_YEARS_OF_EXPERIENCE}"));
            table.HasCheckConstraint(
                "ck_barber_requests_city",
                FormattableString.Invariant(
                    $"char_length(city) BETWEEN 1 AND {Constants.MAX_BARBER_REQUEST_CITY_LENGTH}"));
            table.HasCheckConstraint(
                "ck_barber_requests_about",
                FormattableString.Invariant(
                    $"about IS NULL OR char_length(about) BETWEEN 1 AND {Constants.MAX_TEXT_LENGTH}"));
            // Длина причины отказа — правило домена и одновременно последняя линия защиты
            // в БД: пустая строка не должна попасть в таблицу в обход домена.
            table.HasCheckConstraint(
                "ck_barber_requests_rejection_reason",
                FormattableString.Invariant(
                    $"rejection_reason IS NULL OR char_length(rejection_reason) BETWEEN 1 AND {Constants.MAX_BARBER_REQUEST_REJECTION_REASON_LENGTH}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_barber_requests");

        // Оптимистическая блокировка: два администратора не решат одну заявку одновременно.
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.City)
            .HasColumnName("city")
            .HasMaxLength(Constants.MAX_BARBER_REQUEST_CITY_LENGTH)
            .IsRequired();
        builder.Property(x => x.About)
            .HasColumnName("about")
            .HasMaxLength(Constants.MAX_TEXT_LENGTH);
        builder.Property(x => x.HasSalon).HasColumnName("has_salon");
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(Constants.MAX_BARBER_REQUEST_REJECTION_REASON_LENGTH);

        builder.OwnsOne(x => x.DisplayName, displayName =>
            displayName.Property(value => value.Value)
                .HasColumnName("display_name")
                .HasMaxLength(DisplayName.MAX_LENGTH)
                .IsRequired());

        builder.OwnsOne(x => x.YearsOfExperience, experience =>
            experience.Property(value => value.Value)
                .HasColumnName("years_of_experience")
                .IsRequired());

        // Заявитель удаляется — его заявки уходят вместе с ним: история заявки не имеет
        // смысла без аккаунта, а каскад не оставляет висящих строк.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_barber_requests_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Решение принимает администратор; удаление аккаунта администратора не должно
        // удалять заявку, поэтому ссылка обнуляется — так же, как у аудита.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .HasConstraintName("fk_barber_requests_users_reviewed_by_user_id")
            .OnDelete(DeleteBehavior.SetNull);

        // Одна активная (Pending = 1) заявка на пользователя. Частичный индекс, а не
        // обычный уникальный: после отказа клиент может подать заявку снова.
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter(FormattableString.Invariant(
                $"status = {(int)BarberRequestStatus.Pending}"))
            .HasDatabaseName("ux_barber_requests_pending_user");

        // Очередь администратора: оба направления keyset-выдачи (ASC для pending,
        // DESC для all) читаются по одному btree-индексу, в том числе обратным сканом.
        builder.HasIndex(x => new { x.CreatedAt, x.Id })
            .HasDatabaseName("ix_barber_requests_created_at_id");

        // «Моя последняя заявка»: фильтр по заявителю плюс порядок created_at DESC, id DESC.
        // Ведущая колонка user_id обслуживает и каскадное удаление аккаунта заявителя.
        builder.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_barber_requests_user_id_created_at");

        // FK на автора решения: имя задано явно, иначе EF Core создаёт IX_… в верхнем
        // регистре, а в этом репозитории все индексы названы ix_….
        builder.HasIndex(x => x.ReviewedByUserId)
            .HasDatabaseName("ix_barber_requests_reviewed_by_user_id");
    }
}

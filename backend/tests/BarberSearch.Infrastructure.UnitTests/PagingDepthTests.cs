using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Предел глубины offset-выдачи в «админских» списках (R-32).
/// </summary>
/// <remarks>
/// <para>
/// <c>Skip((page - 1) * pageSize)</c> деградирует с глубиной, поэтому вместо перевода списков
/// на keyset глубина ограничена: <c>page * pageSize</c> не больше 10 000
/// (<c>RepositoryBase.MAX_PAGE_OFFSET</c>). Превышение — <c>400</c> с кодом
/// <c>*.paging.depth</c>.
/// </para>
/// <para>
/// Проводка проверяется на неоткрытом <see cref="ApplicationDbContext"/>: строка подключения
/// указывает на несуществующую базу, поэтому обращение к БД бросило бы исключение, а не вернуло
/// <see cref="Failure"/>. Именно <see cref="Failure"/> с кодом глубины доказывает, что проверка
/// стоит до запроса.
/// Ровно 10 000 (<c>page = 50</c>, <c>pageSize = 200</c>) глубина не отсекает — это проверяется
/// helper'ом напрямую, без соединения.
/// </para>
/// </remarks>
public class PagingDepthTests
{
    [Fact]
    public void ValidatePageOffset_AtMaxOffset_ReturnsNull()
    {
        // 50 * 200 = 10 000 — ровно предел, значит допустимо.
        var failure = ValidatePageOffset(50, 200, ApiErrorCodes.User.PagingDepthInvalid);

        Assert.Null(failure);
    }

    [Fact]
    public void ValidatePageOffset_AboveMaxOffset_ReturnsValidationFailure()
    {
        // 10 001 * 1 = 10 001 — на единицу больше предела.
        var failure = ValidatePageOffset(10_001, 1, ApiErrorCodes.Review.PagingDepthInvalid);

        Assert.NotNull(failure);

        var error = Assert.Single(failure.Errors);

        // 400: Error.Validation, а не Error.Failure/Conflict.
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal(ApiErrorCodes.Review.PagingDepthInvalid, error.Code);
        Assert.Equal("page", error.InvalidField);
    }

    [Fact]
    public void ValidatePageOffset_WithIntMaxValuePage_DoesNotOverflow()
    {
        // int.MaxValue * 200 в int переполнилось бы в отрицательное число и прошло бы проверку;
        // long-произведение обязано вернуть Failure.
        var failure = ValidatePageOffset(
            int.MaxValue,
            200,
            ApiErrorCodes.BarberProfile.PagingDepthInvalid);

        Assert.NotNull(failure);
        Assert.Equal(
            ApiErrorCodes.BarberProfile.PagingDepthInvalid,
            Assert.Single(failure.Errors).Code);
    }

    [Fact]
    public async Task UserRepository_GetPagedAsync_WhenDepthExceeded_ReturnsDepthFailureBeforeQuery()
    {
        // 51 * 200 = 10 200 > 10 000: ошибка должна вернуться до SQL.
        await using var context = CreateContext();
        var repository = new UserRepository(context);

        var result = await repository.GetPagedAsync(51, 200, CancellationToken.None);

        AssertDepthFailure(result, ApiErrorCodes.User.PagingDepthInvalid);
    }

    [Fact]
    public async Task BarberProfileRepository_GetPagedAsync_WhenDepthExceeded_ReturnsDepthFailureBeforeQuery()
    {
        await using var context = CreateContext();
        var repository = new BarberProfileRepository(context);

        var result = await repository.GetPagedAsync(
            51,
            200,
            onlyActive: true,
            CancellationToken.None);

        AssertDepthFailure(result, ApiErrorCodes.BarberProfile.PagingDepthInvalid);
    }

    [Fact]
    public async Task ReviewRepository_GetByBarberProfileIdAsync_WhenDepthExceeded_ReturnsDepthFailureBeforeQuery()
    {
        await using var context = CreateContext();
        var repository = new ReviewRepository(context);

        var result = await repository.GetByBarberProfileIdAsync(
            Guid.CreateVersion7(),
            51,
            200,
            onlyPublished: true,
            CancellationToken.None);

        AssertDepthFailure(result, ApiErrorCodes.Review.PagingDepthInvalid);
    }

    [Fact]
    public async Task UserRepository_GetPagedAsync_WhenPageSizeInvalid_ReturnsPageSizeFailureFirst()
    {
        // 51 * 201 тоже глубже предела, но размер страницы проверяется раньше: клиент должен
        // получить прежний код user.paging.pageSize, а не код глубины.
        await using var context = CreateContext();
        var repository = new UserRepository(context);

        var result = await repository.GetPagedAsync(51, 201, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.User.PagingPageSizeInvalid,
            Assert.Single(result.Error.Errors).Code);
    }

    /// <summary>
    /// Проверка глубины — internal-helper базового класса; тестовой сборке он виден
    /// через <c>InternalsVisibleTo</c>.
    /// </summary>
    private static Failure? ValidatePageOffset(int page, int pageSize, string depthCode)
        => RepositoryBase<Booking>.ValidatePageOffset(page, pageSize, depthCode);

    private static void AssertDepthFailure<T>(
        Result<IReadOnlyList<T>, Failure> result,
        string expectedCode)
    {
        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal(expectedCode, error.Code);
    }

    /// <summary>
    /// Контекст без открытого соединения: если проверка глубины пропущена, запрос уйдёт в
    /// несуществующую базу и тест упадёт исключением, а не «зелёным» успехом.
    /// </summary>
    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=postgres;Password=postgres")
            .Options);
}

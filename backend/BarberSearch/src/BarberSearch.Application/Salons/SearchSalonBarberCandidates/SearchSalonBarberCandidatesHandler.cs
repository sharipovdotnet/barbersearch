using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.GetSalonBarbers;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Salons.SearchSalonBarberCandidates;

/// <summary>
/// Обработчик запроса <see cref="SearchSalonBarberCandidatesQuery"/>.
/// </summary>
/// <remarks>
/// Сначала проверяется доступ к салону, затем строка поиска: чужой салон должен
/// получить <c>403</c>, а не подсказку о формате запроса. Минимум три символа
/// защищает от выдачи всех барберов города, максимум <c>100</c> совпадает
/// с остальными поисками проекта. Десять строк — подсказка формы, не справочник.
/// </remarks>
public sealed class SearchSalonBarberCandidatesHandler
    : IQueryHandler<IReadOnlyList<SalonBarberResponse>, SearchSalonBarberCandidatesQuery>
{
    /// <summary>Минимальная длина поисковой строки.</summary>
    public const int MIN_SEARCH_LENGTH = 3;

    /// <summary>Максимальная длина поисковой строки.</summary>
    public const int MAX_SEARCH_LENGTH = 100;

    /// <summary>Сколько строк возвращает подсказка.</summary>
    public const int RESULT_LIMIT = 10;

    private readonly ISalonRepository _salons;
    private readonly IBarberProfileRepository _profiles;
    private readonly ICurrentUser _currentUser;

    public SearchSalonBarberCandidatesHandler(
        ISalonRepository salons,
        IBarberProfileRepository profiles,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(salons);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(currentUser);

        _salons = salons;
        _profiles = profiles;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<SalonBarberResponse>, Failure>> Handle(
        SearchSalonBarberCandidatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var salonResult = await _salons.GetByIdAsync(query.SalonId, cancellationToken);
        if (salonResult.IsFailure)
        {
            return salonResult.Error;
        }

        var allowed = _currentUser.EnsureCanManageSalon(salonResult.Value);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var search = query.Search?.Trim() ?? string.Empty;

        if (search.Length < MIN_SEARCH_LENGTH)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Salon.BarberSearchTooShort,
                $"A barber search must contain at least {MIN_SEARCH_LENGTH} characters.",
                nameof(query.Search)));
        }

        if (search.Length > MAX_SEARCH_LENGTH)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Salon.BarberSearchTooLong,
                $"A barber search cannot be longer than {MAX_SEARCH_LENGTH} characters.",
                nameof(query.Search)));
        }

        return await _profiles.SearchSalonBarberCandidatesAsync(
            query.SalonId,
            search,
            RESULT_LIMIT,
            cancellationToken);
    }
}

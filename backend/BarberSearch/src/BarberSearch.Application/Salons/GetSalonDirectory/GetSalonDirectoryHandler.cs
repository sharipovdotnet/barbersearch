using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Salons.GetSalonDirectory;

/// <summary>
/// Обработчик запроса <see cref="GetSalonDirectoryQuery"/>.
/// </summary>
/// <remarks>
/// Обработчик только нормализует фильтры: пустая строка после обрезки — «без фильтра»,
/// а строка длиннее <c>100</c> символов — <c>400</c>. Поиск и фильтр по городу
/// выполняет репозиторий (<c>ILIKE</c> с экранированием служебных символов).
/// </remarks>
public sealed class GetSalonDirectoryHandler
    : IQueryHandler<IReadOnlyList<SalonDirectoryItemResponse>, GetSalonDirectoryQuery>
{
    /// <summary>
    /// Максимальная длина каждого фильтра: длиннее — не опечатка, а мусор в запросе.
    /// </summary>
    private const int MAX_FILTER_LENGTH = 100;

    private readonly ISalonRepository _repository;

    public GetSalonDirectoryHandler(ISalonRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<SalonDirectoryItemResponse>, Failure>> Handle(
        GetSalonDirectoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var city = Normalize(query.City);
        if (city?.Length > MAX_FILTER_LENGTH)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Salon.DirectoryCityInvalid,
                $"City filter cannot be longer than {MAX_FILTER_LENGTH} characters.",
                nameof(query.City)));
        }

        var search = Normalize(query.Search);
        if (search?.Length > MAX_FILTER_LENGTH)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Salon.DirectorySearchInvalid,
                $"Search filter cannot be longer than {MAX_FILTER_LENGTH} characters.",
                nameof(query.Search)));
        }

        return await _repository.GetDirectoryAsync(city, search, cancellationToken);
    }

    /// <summary>Обрезка пробелов; пустая строка — отсутствие фильтра.</summary>
    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

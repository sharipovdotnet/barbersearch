using BarberSearch.Contracts.Addresses.SuggestAddresses;

namespace BarberSearch.Mobile.Core.Api.Addresses;

/// <summary>
/// Авторизованные подсказки адреса для формы салона.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный интерфейс, а не метод в <see cref="IBarberSearchApi"/>: города подсказываются
/// анонимно и работают на главной вкладке до входа, а адреса — только авторизованному
/// клиенту. Смешивать два разных клиента в одном интерфейсе значило бы дать экрану
/// возможность позвать адреса без токена и получить <c>401</c> без объяснения.
/// </para>
/// <para>
/// <c>providerId</c> подсказки — непрозрачный токен провайдера: он нужен только как параметр
/// запроса и никогда не попадает в тело создания салона.
/// </para>
/// </remarks>
public interface IAddressesApi
{
    /// <summary>
    /// Подсказки улицы и дома внутри выбранного города.
    /// </summary>
    /// <remarks>
    /// <paramref name="cityProviderId"/> важнее <paramref name="city"/>: сервер игнорирует
    /// свободный текст, когда пришёл провайдерский токен. Пустые значения в адрес не
    /// попадают — сервер понимает их как «фильтра нет».
    /// </remarks>
    /// <param name="query">Начало улицы или дома; тримится и экранируется.</param>
    /// <param name="city">Свободный текст города, когда подсказка города не выбрана.</param>
    /// <param name="cityProviderId">Провайдерский токен выбранного города.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Подсказки адреса в порядке сервера; пустой список — «ничего не найдено».</returns>
    /// <exception cref="ArgumentException">Запрос пуст или состоит из пробелов.</exception>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<IReadOnlyList<AddressSuggestionResponse>> GetAddressSuggestionsAsync(
        string query,
        string? city,
        string? cityProviderId,
        CancellationToken cancellationToken = default);
}

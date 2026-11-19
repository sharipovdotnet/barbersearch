using BarberSearch.Contracts.Admin;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// Админская сводка: счётчики очередей модерации одним запросом.
/// </summary>
/// <remarks>
/// <para>
/// Сводка — единственное агрегатное чтение админки. Ею живут и список разделов
/// телефона, и бейдж «сколько ждёт решения» в «Профиле», поэтому своих счётчиков
/// клиент не считает: числа приходят теми же предикатами, что фильтры разделов
/// (<c>frontend/src/lib/admin-summary.ts</c>, <c>selectSectionCounts</c>).
/// </para>
/// <para>
/// Мутаций у сводки нет, а действия остаются в разделах: после действия достаточно
/// прочитать сводку заново. Доступ закрыт политикой <c>AdminOnly</c> на сервере,
/// поэтому <c>403</c> здесь означает «не администратор», а <c>401</c> — конец сессии.
/// </para>
/// </remarks>
public interface IAdminSummaryApi
{
    /// <summary>
    /// Читает снимок очередей модерации.
    /// </summary>
    /// <remarks>
    /// Снимок не кэшируется: числа меняются от действий и от чужих решений,
    /// и экран перечитывает его при каждом появлении.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Счётчики очередей разделов.</returns>
    /// <exception cref="BarberSearchApiException">Сессия истекла, роль не администратор или API ответил ошибкой.</exception>
    Task<AdminSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);
}

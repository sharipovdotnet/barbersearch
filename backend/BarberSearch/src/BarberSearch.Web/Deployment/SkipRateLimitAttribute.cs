namespace BarberSearch.Web.Deployment;

/// <summary>
/// Исключает действие из глобального лимита записи.
/// </summary>
/// <remarks>
/// <para>
/// Маркер нужен вебхуку Telegram: Bot API не присылает cookie и не умеет ждать
/// <c>Retry-After</c>, а его повтор после <c>429</c> — это потерянное обновление привязки.
/// Проверка секрета заголовка и идемпотентность по <c>update_id</c> защищают эндпоинт лучше,
/// чем квота на IP (за прокси все обновления приходят с адресов Telegram).
/// </para>
/// <para>
/// Глобальный лимитер проверяет метаданные endpoint'а: атрибут читается через
/// <c>HttpContext.GetEndpoint()</c>, поэтому маркер действует только на действие, а не на
/// весь контроллер и не на другие <c>POST</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipRateLimitAttribute : Attribute
{
}

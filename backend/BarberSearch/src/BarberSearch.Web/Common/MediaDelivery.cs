using BarberSearch.Application.PortfolioPhotos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BarberSearch.Web.Common;

/// <summary>
/// Общая раздача media-файлов: разбор <c>?size=</c>, политика кэша, <c>ETag</c> и <c>304</c>.
/// </summary>
/// <remarks>
/// <para>
/// Два контроллера раздачи (<c>PortfolioMediaController</c> и <c>AvatarMediaController</c>)
/// отличаются только проверкой доступа. Всё, что после неё — выбор представления, заголовки и
/// условный ответ, — обязано жить в одном месте: дублирование <c>304</c> и <c>Cache-Control</c>
/// в двух файлах рано или поздно разойдётся.
/// </para>
/// <para>
/// Представление выбирает URL, а не <c>Accept</c>: <c>?size=sm|md|lg</c> — это то же
/// изображение в другом размере, и <c>Vary</c> не ставится.
/// </para>
/// <para>
/// Кэш честный, а не суточный: видимый кадр обязан перепроверяться (<c>max-age=0,
/// must-revalidate</c> + сильный <c>ETag</c>), чтобы скрытый, отклонённый или заменённый кадр
/// исчезал сразу после перезагрузки страницы. Повторный показ того же кадра не перекачивает
/// файл: браузер шлёт <c>If-None-Match</c> и получает <c>304</c>. Непубличный кадр
/// (владелец или администратор) и любой <c>404</c> — <c>no-store</c>.
/// </para>
/// </remarks>
public static class MediaDelivery
{
    /// <summary>
    /// Кэш видимого кадра: кэшировать можно, показывать без перепроверки — нельзя.
    /// </summary>
    public const string CACHE_CONTROL_PUBLIC = "public, max-age=0, must-revalidate";

    /// <summary>
    /// Кэш непубличного кадра: владельцу и администратору ответ не кэшируется вовсе.
    /// </summary>
    public const string CACHE_CONTROL_PRIVATE = "private, no-store";

    /// <summary>
    /// Кэш любого <c>404</c>: «кадра нет» не должно залипать после одобрения или разблокировки.
    /// </summary>
    public const string CACHE_CONTROL_NO_STORE = "no-store";

    /// <summary>
    /// Запрет на угадывание типа содержимого браузером.
    /// </summary>
    public const string NOSNIFF_HEADER = "X-Content-Type-Options";

    /// <summary>
    /// Единственное допустимое значение <see cref="NOSNIFF_HEADER"/>.
    /// </summary>
    public const string NOSNIFF_VALUE = "nosniff";

    /// <summary>
    /// Разбирает <c>?size=</c> в представление файла.
    /// </summary>
    /// <remarks>
    /// Отсутствие параметра — исходник. Ровно одно значение из <c>sm</c>, <c>md</c>, <c>lg</c>
    /// в нижнем регистре — вариант. Всё остальное (пустое значение, неизвестное имя,
    /// повторённый параметр) — отказ: пользовательских размеров нет, а «первое из двух
    /// значений» — это уже угадывание.
    /// </remarks>
    /// <param name="query">Query-строка запроса.</param>
    /// <param name="variant">Разобранное представление при успехе.</param>
    /// <returns><c>false</c>, если значение параметра недопустимо.</returns>
    public static bool TryParseVariant(IQueryCollection query, out PhotoVariant variant)
    {
        ArgumentNullException.ThrowIfNull(query);

        variant = PhotoVariant.Original;

        if (!query.TryGetValue(PhotoThumbnails.SIZE_QUERY_PARAMETER, out var values)
            || values.Count == 0)
        {
            return true;
        }

        if (values.Count > 1)
        {
            return false;
        }

        switch (values[0])
        {
            case "sm":
                variant = PhotoVariant.Small;

                return true;
            case "md":
                variant = PhotoVariant.Medium;

                return true;
            case "lg":
                variant = PhotoVariant.Large;

                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Отдаёт открытый файл с заголовками своей политики кэша либо <c>304</c> без тела.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ETag</c> приходит из хранилища: только оно знает, отдан ли исходник или вариант.
    /// При совпадении <c>If-None-Match</c> (включая <c>*</c>) отдаётся <c>304</c> с теми же
    /// <c>ETag</c> и <c>Cache-Control</c> и без тела; поток при этом закрывается здесь,
    /// потому что в ответ он не попадёт.
    /// </para>
    /// <para>
    /// Если клиент просит <c>Range</c>, <c>304</c> не отдаётся: работает обычный <c>206</c>
    /// (или <c>200</c> для полного файла) — докачка байтов важнее экономии запроса.
    /// </para>
    /// </remarks>
    /// <param name="request">Текущий запрос: заголовки <c>Range</c> и <c>If-None-Match</c>.</param>
    /// <param name="response">Текущий ответ: в него пишутся общие заголовки.</param>
    /// <param name="content">Открытый файл из хранилища.</param>
    /// <param name="isPublic">Публично ли виден кадр: от этого зависит политика кэша.</param>
    /// <returns>Результат для контроллера: файл с поддержкой range либо <c>304</c>.</returns>
    public static IActionResult CreateFileResult(
        HttpRequest request,
        HttpResponse response,
        PhotoContent content,
        bool isPublic)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(content);

        response.Headers.CacheControl = isPublic ? CACHE_CONTROL_PUBLIC : CACHE_CONTROL_PRIVATE;
        response.Headers[NOSNIFF_HEADER] = NOSNIFF_VALUE;
        response.Headers.ETag = content.EntityTag;

        if (IsNotModified(request, content.EntityTag))
        {
            // Тело не уходит — поток не станет частью ответа, поэтому закрываем его здесь.
            content.Stream.Dispose();

            return new StatusCodeResult(StatusCodes.Status304NotModified);
        }

        return new FileStreamResult(content.Stream, content.ContentType)
        {
            EnableRangeProcessing = true
        };
    }

    /// <summary>
    /// Проставляет <c>Cache-Control: no-store</c> отказа раздачи.
    /// </summary>
    /// <param name="response">Текущий ответ.</param>
    public static void MarkNotFound(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers.CacheControl = CACHE_CONTROL_NO_STORE;
    }

    /// <summary>
    /// Проверяет, совпадает ли <c>If-None-Match</c> с выданным <c>ETag</c>.
    /// </summary>
    /// <remarks>
    /// Сравнение сильное (без префикса <c>W/</c>, который клиент прислать не может, потому что
    /// слабых ETag сервер не выдаёт), <c>*</c> считается совпадением. Заголовок разбирается
    /// штатным typed header: самодельный split по запятой ломался бы на списках с пробелами.
    /// </remarks>
    private static bool IsNotModified(HttpRequest request, string entityTag)
    {
        if (request.Headers.ContainsKey(HeaderNames.Range))
        {
            return false;
        }

        foreach (var candidate in request.GetTypedHeaders().IfNoneMatch)
        {
            if (string.Equals(candidate.Tag.Value, "*", StringComparison.Ordinal)
                || string.Equals(candidate.Tag.Value, entityTag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

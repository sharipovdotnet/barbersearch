using System.Net;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// Сводит исключение запроса к <see cref="ApiFailureKind"/> — единому решению «ошибка это,
/// повторять её или молчать».
/// </summary>
/// <remarks>
/// <para>
/// Раньше каждый ViewModel сам ловил <see cref="HttpRequestException"/> и
/// <see cref="BarberSearchApiException"/> и сам выбирал текст, поэтому одинаковые сбои
/// получали разные подписи. Классификация собрана в одном месте: экран, цикл повторов и
/// сообщение человеку говорят об одном и том же.
/// </para>
/// <para>
/// Отмена отделена от сбоя. Если токен отменён — это решение экрана или человека, ошибку
/// показывать нельзя. Но если <see cref="OperationCanceledException"/> пришёл при живом
/// токене, отмены не было: так <c>HttpClient.Timeout</c> сообщает о своём таймауте, и это
/// «нет связи с сервером». Сегодня ViewModels глушат такой случай в
/// <c>catch (OperationCanceledException)</c>, и человек не видит, что ответа не было.
/// </para>
/// </remarks>
public static class ApiFailureClassifier
{
    /// <summary>
    /// Классифицирует сбой запроса.
    /// </summary>
    /// <param name="exception">Исключение запроса; <c>null</c> — запрос не падал.</param>
    /// <param name="cancellationToken">Токен, которым отменяли запрос.</param>
    /// <returns>Вид сбоя, по которому экран и цикл повторов принимают решение.</returns>
    public static ApiFailureKind Classify(
        Exception? exception,
        CancellationToken cancellationToken = default)
    {
        if (exception is null)
        {
            return ApiFailureKind.None;
        }

        if (exception is HttpRequestException)
        {
            // До ответа дело не дошло: DNS, TCP, TLS, обрыв или таймаут HttpClient.
            return ApiFailureKind.Transport;
        }

        if (exception is BarberSearchApiException apiException)
        {
            var statusCode = (int)apiException.StatusCode;

            // 5xx — сервер не справился; 408 и 429 — просьба повторить позже. Остальные
            // ответы окончательны: повтор лишь повторит отказ.
            return statusCode >= 500
                || statusCode == (int)HttpStatusCode.RequestTimeout
                || statusCode == (int)HttpStatusCode.TooManyRequests
                ? ApiFailureKind.RetryableServer
                : ApiFailureKind.Terminal;
        }

        if (exception is OperationCanceledException)
        {
            // Отменил экран или человек — это не сбой. Живой токен означает, что отмены не
            // было: таймаут HttpClient, и для человека это «нет связи с сервером».
            return cancellationToken.IsCancellationRequested
                ? ApiFailureKind.None
                : ApiFailureKind.Transport;
        }

        // Незнакомый сбой повторять вслепую нельзя: решение остаётся за экраном.
        return ApiFailureKind.Terminal;
    }

    /// <summary>
    /// Может ли повтор этого сбоя помочь.
    /// </summary>
    /// <param name="kind">Вид сбоя.</param>
    /// <returns>
    /// <c>true</c> для <see cref="ApiFailureKind.Transport"/> и
    /// <see cref="ApiFailureKind.RetryableServer"/>; отмену и окончательный отказ повторять
    /// нельзя.
    /// </returns>
    public static bool IsRetryable(this ApiFailureKind kind) =>
        kind is ApiFailureKind.Transport or ApiFailureKind.RetryableServer;
}

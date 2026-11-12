using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace BarberSearch.Web.Storage;

/// <summary>
/// <see cref="IPhotoObjectStore"/> поверх AWS SDK: один приватный бакет, ключи без префикса.
/// </summary>
/// <remarks>
/// <para>
/// Клиент (<see cref="IAmazonS3"/>) приходит из DI и настроен на S3-совместимый эндпоинт
/// (<c>ServiceURL</c> + <c>ForcePathStyle</c> для MinIO). Здесь живёт вся S3-специфика:
/// запросы, статусы, переименование ошибок. Верхний адаптер <see cref="S3PhotoStorage"/>
/// видит только <see cref="PhotoObject"/>/<see cref="IOException"/> и не знает про SDK.
/// </para>
/// <para>
/// Ошибки наружу уходят как <see cref="IOException"/> с общим текстом: ключ объекта содержит
/// идентификаторы владельца и кадра, а сообщение AWS SDK может нести бакет и эндпоинт — ни то,
/// ни другое не должно попадать в ответ клиенту. В журнал пишутся только HTTP-статус и код
/// ошибки S3: их достаточно, чтобы отличить «нет прав» от «сервис недоступен».
/// </para>
/// <para>
/// Бакет приватный: публичных ACL, подписанных URL и прямой раздачи из S3 нет. Доступ
/// по-прежнему решает медиа-контроллер по базе.
/// </para>
/// </remarks>
internal sealed class S3ObjectStore : IPhotoObjectStore
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly ILogger<S3ObjectStore> _logger;

    /// <summary>
    /// Создаёт хранилище объектов над настроенным клиентом и бакетом.
    /// </summary>
    /// <param name="client">Клиент S3, зарегистрированный в DI.</param>
    /// <param name="options">Настройки: имя бакета уже проверено на старте.</param>
    /// <param name="logger">Журнал: статус и код ошибки без ключа и секретов.</param>
    public S3ObjectStore(
        IAmazonS3 client,
        PhotoStorageOptions options,
        ILogger<S3ObjectStore> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _bucket = options.S3.Bucket;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PhotoObject?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetObjectAsync(
                new GetObjectRequest { BucketName = _bucket, Key = key },
                cancellationToken);

            // Поток ответа закрывается сразу после копирования: наружу уходят байты, а не
            // соединение SDK, — иначе ответ контроллера держал бы его до конца раздачи.
            await using var stream = response.ResponseStream;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            return new PhotoObject(
                buffer.ToArray(),
                response.Headers.ContentType ?? PhotoContentTypes.UNKNOWN);
        }
        catch (AmazonServiceException exception) when (IsNotFound(exception))
        {
            return null;
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException)
        {
            throw StorageFailure(exception);
        }
    }

    /// <inheritdoc />
    public async Task PutAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            var request = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = stream,
                ContentType = contentType,

                // Потоком владеет этот метод: SDK не должен закрывать его до Dispose.
                AutoCloseStream = false
            };

            await _client.PutObjectAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException)
        {
            throw StorageFailure(exception);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = _bucket, Key = key },
                cancellationToken);
        }
        catch (AmazonServiceException exception) when (IsNotFound(exception))
        {
            // S3 идемпотентен: удаление отсутствующего ключа — успех.
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException)
        {
            throw StorageFailure(exception);
        }
    }

    /// <summary>
    /// Проверяет, что S3 ответил «объекта (или бакета) нет», а не другой ошибкой.
    /// </summary>
    private static bool IsNotFound(AmazonServiceException exception)
        => exception.StatusCode == HttpStatusCode.NotFound;

    /// <summary>
    /// Переводит ошибку SDK в <see cref="IOException"/> без ключа, бакета и эндпоинта.
    /// </summary>
    private IOException StorageFailure(Exception exception)
    {
        var status = exception is AmazonServiceException service
            ? (int)service.StatusCode
            : 0;
        var errorCode = (exception as AmazonServiceException)?.ErrorCode;

        _logger.LogWarning(
            "Object storage request failed with status {StatusCode} and error code {ErrorCode}.",
            status,
            errorCode);

        return new IOException("The object storage request failed.");
    }
}

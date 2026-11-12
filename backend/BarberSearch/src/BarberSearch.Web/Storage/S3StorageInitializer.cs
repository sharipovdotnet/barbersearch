using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Проверяет (и при <c>EnsureBucket=true</c> создаёт) бакет на старте хоста.
/// </summary>
/// <remarks>
/// <para>
/// Проверка асинхронна и требует сети, поэтому не может жить в
/// <see cref="PhotoStorageOptions.FromConfiguration"/>: тот синхронный и вызывается до
/// построения хоста. Ошибка здесь роняет старт — так же, как недоступный <c>Storage:RootPath</c>
/// у локального провайдера: лучше узнать о недоступном хранилище на старте, чем на первой
/// загрузке барбера.
/// </para>
/// <para>
/// В текстах исключений нет имени бакета, эндпоинта и значений ключей доступа: первые два
/// раскрывают инфраструктуру, третьи — секреты. В журнал пишутся только HTTP-статус и код
/// ошибки S3.
/// </para>
/// </remarks>
internal sealed class S3StorageInitializer : IHostedService
{
    private const string START_FAILURE_MESSAGE =
        "Storage:Provider=S3 is selected, but the object storage is unavailable: the configured "
        + "bucket could not be checked or created. Check Storage:S3:ServiceUrl and the "
        + "credentials; the values are not printed.";

    private const string MISSING_BUCKET_MESSAGE =
        "Storage:Provider=S3 is selected, but the configured bucket does not exist. Create it, "
        + "or set Storage:S3:EnsureBucket=true in development. The start fails instead of the "
        + "first upload.";

    private readonly IAmazonS3 _client;
    private readonly S3StorageOptions _options;
    private readonly ILogger<S3StorageInitializer> _logger;

    /// <summary>
    /// Создаёт проверку бакета над клиентом из DI.
    /// </summary>
    /// <param name="client">Клиент S3, настроенный на эндпоинт и регион.</param>
    /// <param name="options">Настройки: имя бакета и признак <c>EnsureBucket</c>.</param>
    /// <param name="logger">Журнал: статус и код ошибки без секретов.</param>
    public S3StorageInitializer(
        IAmazonS3 client,
        PhotoStorageOptions options,
        ILogger<S3StorageInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _options = options.S3;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        bool exists;

        try
        {
            exists = await AmazonS3Util.DoesS3BucketExistV2Async(_client, _options.Bucket);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException)
        {
            LogFailure(exception);

            throw new InvalidOperationException(START_FAILURE_MESSAGE, exception);
        }

        if (!exists && _options.EnsureBucket)
        {
            try
            {
                await _client.PutBucketAsync(
                    new PutBucketRequest { BucketName = _options.Bucket },
                    cancellationToken);

                exists = true;
            }
            catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException)
            {
                LogFailure(exception);

                throw new InvalidOperationException(START_FAILURE_MESSAGE, exception);
            }
        }

        if (!exists)
        {
            throw new InvalidOperationException(MISSING_BUCKET_MESSAGE);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Пишет статус и код ошибки S3: имени бакета, эндпоинта и ключей здесь нет.
    /// </summary>
    private void LogFailure(Exception exception)
    {
        var status = exception is AmazonServiceException service
            ? (int)service.StatusCode
            : 0;
        var errorCode = (exception as AmazonServiceException)?.ErrorCode;

        _logger.LogWarning(
            "Object storage bucket check failed with status {StatusCode} and error code {ErrorCode}.",
            status,
            errorCode);
    }
}

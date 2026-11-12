namespace BarberSearch.Web.Storage;

/// <summary>
/// Настройки S3-совместимого хранилища из подсекции <c>Storage:S3</c>.
/// </summary>
/// <remarks>
/// <para>
/// Один бакет и один префикс на вид файла: ключи повторяют локальную раскладку
/// (<c>portfolio/{profileId:N}/…</c> и <c>avatars/{userId:N}/…</c>), поэтому смена места
/// хранения не меняет ни URL, ни <c>?size=</c>, ни миграции.
/// </para>
/// <para>
/// Ключи доступа и секрет приходят только из окружения (user secrets / переменные
/// <c>Storage__S3__*</c>): в <c>appsettings*.json</c> их нет. В тексты исключений значения
/// <see cref="AccessKey"/> и <see cref="SecretKey"/> не попадают — только имена ключей
/// конфигурации.
/// </para>
/// <para>
/// <see cref="ForcePathStyle"/> обязателен для MinIO: он ждёт бакет в пути
/// (<c>http://host/bucket/key</c>), а не в поддомене. <see cref="EnsureBucket"/> остаётся
/// <c>false</c> по умолчанию: прод не должен молча создавать ресурсы, в dev/CI его
/// включают явно.
/// </para>
/// </remarks>
public sealed record S3StorageOptions
{
    /// <summary>
    /// Имя бакета: обязательное поле при <c>Storage:Provider=S3</c>.
    /// </summary>
    public string Bucket { get; init; } = string.Empty;

    /// <summary>
    /// Адрес S3 API, например <c>http://localhost:9000</c> для MinIO.
    /// </summary>
    /// <remarks>
    /// Обязательное поле. Адрес — эндпоинт API, а не веб-консоль MinIO (та живёт на 9001).
    /// </remarks>
    public string ServiceUrl { get; init; } = string.Empty;

    /// <summary>
    /// Регион подписи запросов; по умолчанию <c>us-east-1</c>.
    /// </summary>
    /// <remarks>
    /// MinIO принимает любое значение, но подпись требует непустой регион. Для настоящего
    /// AWS S3 здесь должен стоять регион бакета.
    /// </remarks>
    public string Region { get; init; } = "us-east-1";

    /// <summary>
    /// Ключ доступа: обязательное поле, значение в тексты ошибок не попадает.
    /// </summary>
    public string AccessKey { get; init; } = string.Empty;

    /// <summary>
    /// Секретный ключ: обязательное поле, значение в тексты ошибок не попадает.
    /// </summary>
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// Стиль адресации бакета в пути (<c>ForcePathStyle</c>); по умолчанию <c>true</c> — для MinIO.
    /// </summary>
    public bool ForcePathStyle { get; init; } = true;

    /// <summary>
    /// Создавать бакет на старте, если его нет: <c>true</c> в dev/CI, <c>false</c> в проде.
    /// </summary>
    public bool EnsureBucket { get; init; }
}

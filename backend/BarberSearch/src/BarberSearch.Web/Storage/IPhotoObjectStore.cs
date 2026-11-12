namespace BarberSearch.Web.Storage;

/// <summary>
/// Шов между логикой хранилища и SDK object storage: операции над ключом, без S3-специфики.
/// </summary>
/// <remarks>
/// <para>
/// Зачем отдельный интерфейс: в тестах нет mocking-библиотеки, а ручной фейк <c>IAmazonS3</c>
/// (десятки методов, конфигурация, ответы) был бы дороже самой реализации. <see cref="S3PhotoStorage"/>
/// проверяется на фейковом хранилище в памяти, а живой MinIO — опциональными тестами по
/// env-флагу.
/// </para>
/// <para>
/// Контракт ошибок: отсутствие объекта — не ошибка (<see cref="GetAsync"/> возвращает
/// <c>null</c>, <see cref="DeleteAsync"/> завершается успешно); любая другая ошибка хранилища
/// приходит как <see cref="IOException"/> с текстом без ключа, бакета, эндпоинта и секретов.
/// Отмена пробрасывается как <see cref="OperationCanceledException"/>. Так адаптер ловит
/// ошибки так же, как локальный, и не зависит от типов AWS SDK.
/// </para>
/// </remarks>
internal interface IPhotoObjectStore
{
    /// <summary>
    /// Читает объект целиком.
    /// </summary>
    /// <param name="key">Ключ объекта, собранный из разобранного media-URL.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Объект либо <c>null</c>, если его нет.</returns>
    /// <exception cref="IOException">Хранилище ответило ошибкой, отличной от «не найдено».</exception>
    Task<PhotoObject?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Записывает объект; повторная запись того же ключа идемпотентна.
    /// </summary>
    /// <param name="key">Ключ объекта.</param>
    /// <param name="content">Содержимое целиком.</param>
    /// <param name="contentType">MIME-тип для метаданных объекта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="IOException">Запись не удалась.</exception>
    Task PutAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Удаляет объект; отсутствие объекта — успех.
    /// </summary>
    /// <param name="key">Ключ объекта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="IOException">Удаление не удалось.</exception>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

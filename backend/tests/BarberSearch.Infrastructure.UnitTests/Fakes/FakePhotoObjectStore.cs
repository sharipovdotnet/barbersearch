using BarberSearch.Web.Storage;

namespace BarberSearch.Infrastructure.UnitTests.Fakes;

/// <summary>
/// Хранилище объектов в памяти: словарь «ключ → байты и MIME», счётчики и управляемые сбои.
/// </summary>
/// <remarks>
/// Ручной фейк вместо мока <c>IAmazonS3</c>: у SDK десятки членов и своя конфигурация, а
/// <see cref="S3PhotoStorage"/> проверяется на уровне ключей и байтов. Сбои задаются
/// предикатом по ключу, поэтому тест может уронить ровно запись варианта, не трогая исходник.
/// </remarks>
internal sealed class FakePhotoObjectStore : IPhotoObjectStore
{
    private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);

    private Func<string, bool>? _failPuts;

    /// <summary>
    /// Все ключи, которые читали, в порядке вызовов: по ним считается «один Get варианта».
    /// </summary>
    public List<string> GetKeys { get; } = [];

    /// <summary>
    /// Все ключи, которые удаляли, в порядке вызовов.
    /// </summary>
    public List<string> DeleteKeys { get; } = [];

    /// <summary>
    /// Все ключи, которые писали, в порядке вызовов.
    /// </summary>
    public List<string> PutKeys { get; } = [];

    /// <summary>
    /// Когда <c>true</c>, удаление бросает <see cref="IOException"/>.
    /// </summary>
    public bool FailDeletes { get; set; }

    /// <summary>
    /// Количество объектов сейчас.
    /// </summary>
    public int Count => _objects.Count;

    /// <summary>
    /// Ключи всех объектов сейчас.
    /// </summary>
    public IReadOnlyCollection<string> Keys => _objects.Keys;

    /// <summary>
    /// Задаёт предикат сбоя записи: исключение <see cref="IOException"/> для подходящих ключей.
    /// </summary>
    public void FailPutsWhere(Func<string, bool> predicate)
    {
        _failPuts = predicate;
    }

    /// <summary>
    /// Сбрасывает счётчики вызовов, оставляя содержимое: нужно для «одного чтения» в тестах.
    /// </summary>
    public void ResetCalls()
    {
        GetKeys.Clear();
        PutKeys.Clear();
        DeleteKeys.Clear();
    }

    /// <summary>
    /// Кладёт объект напрямую, минуя счётчики: подготовка состояния теста.
    /// </summary>
    public void Seed(string key, byte[] content, string contentType)
    {
        _objects[key] = new StoredObject(content, contentType);
    }

    /// <summary>
    /// Удаляет объект напрямую, минуя счётчики: подготовка состояния теста.
    /// </summary>
    public void Remove(string key) => _objects.Remove(key);

    /// <summary>
    /// Проверяет наличие объекта по ключу.
    /// </summary>
    public bool Contains(string key) => _objects.ContainsKey(key);

    /// <summary>
    /// Читает байты объекта по ключу; отсутствие — ошибка теста.
    /// </summary>
    public byte[] Content(string key) => _objects[key].Content;

    /// <summary>
    /// Читает сохранённый MIME объекта по ключу; отсутствие — ошибка теста.
    /// </summary>
    public string ContentType(string key) => _objects[key].ContentType;

    /// <inheritdoc />
    public Task<PhotoObject?> GetAsync(string key, CancellationToken cancellationToken)
    {
        GetKeys.Add(key);

        return Task.FromResult(
            _objects.TryGetValue(key, out var stored)
                ? new PhotoObject(stored.Content, stored.ContentType)
                : null);
    }

    /// <inheritdoc />
    public Task PutAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        PutKeys.Add(key);

        if (_failPuts?.Invoke(key) == true)
        {
            throw new IOException("The fake object store rejected the write.");
        }

        _objects[key] = new StoredObject(content.ToArray(), contentType);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        DeleteKeys.Add(key);

        if (FailDeletes)
        {
            throw new IOException("The fake object store rejected the delete.");
        }

        _objects.Remove(key);

        return Task.CompletedTask;
    }

    private sealed record StoredObject(byte[] Content, string ContentType);
}

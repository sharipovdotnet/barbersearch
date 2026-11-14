using System.Text.Json;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Auth;

namespace BarberSearch.Mobile.Auth;

/// <summary>
/// Хранит пару токенов в <see cref="SecureStorage"/> одной JSON-строкой.
/// </summary>
/// <remarks>
/// <para>
/// Почему одной строкой: между двумя записями приложение может быть убито, и на диске
/// остались бы новый access рядом со старым refresh. Сервер счёл бы такой refresh повторно
/// использованным, отозвал всю цепочку, и пользователь потерял бы вход без объяснения.
/// </para>
/// <para>
/// Почему чтение обёрнуто в <c>catch</c>: Android Auto Backup переносит shared preferences
/// на новое устройство, но не переносит ключ шифрования. Восстановленное значение
/// расшифровать уже нельзя, и платформа бросает исключение вместо <c>null</c>. Правильная
/// реакция — считать, что сессии нет, и почистить хранилище, а не падать на старте.
/// </para>
/// </remarks>
public sealed class SecureStorageTokenStore : ITokenStore
{
    /// <summary>
    /// Ключ записи; префикс отделяет пару от будущих значений вроде настроек уведомлений.
    /// </summary>
    private const string StorageKey = "auth.tokens";

    /// <inheritdoc />
    public async Task<StoredTokens?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? raw;

        try
        {
            raw = await SecureStorage.Default.GetAsync(StorageKey).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Значение повреждено или ключ шифрования потерян: сессии нет, мусор убираем.
            RemoveEverything();

            return null;
        }

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoredTokens>(raw, ApiJson.Options);
        }
        catch (JsonException)
        {
            // Запись сделана другой версией приложения и не разбирается — тот же ответ.
            RemoveEverything();

            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await SecureStorage.Default
                .SetAsync(StorageKey, JsonSerializer.Serialize(tokens, ApiJson.Options))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Вход уже состоялся, и сбой записи не должен его отменять: сессия живёт в памяти
            // до конца запуска, а на следующем старте человек войдёт снова. Показать ошибку
            // здесь нечем — экран уже показал бы профиль, а причина в устройстве, не в сервере.
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RemoveEverything();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Стирает всё содержимое защищённого хранилища.
    /// </summary>
    /// <remarks>
    /// <c>RemoveAll</c>, а не <c>Remove(StorageKey)</c>: повреждённую запись платформа может
    /// не отдать и не удалить по ключу, а другого содержимого у приложения там нет.
    /// </remarks>
    private static void RemoveEverything()
    {
        try
        {
            SecureStorage.Default.RemoveAll();
        }
        catch (Exception)
        {
            // Устройство без рабочего хранилища: стирать нечего, запись и так недоступна.
        }
    }
}

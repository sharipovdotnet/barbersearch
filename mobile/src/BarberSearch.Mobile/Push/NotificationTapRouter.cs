using BarberSearch.Mobile.Core.Push;

namespace BarberSearch.Mobile.Push;

/// <summary>
/// Переход по нажатию на push-напоминание.
/// </summary>
/// <remarks>
/// <para>
/// Нажатие приходит двумя путями: холодный старт отдаёт extras в
/// <c>MainActivity.OnCreate</c>, тёплый — в <c>OnNewIntent</c>. В обоих случаях Shell может
/// быть ещё не собран, поэтому цель запоминается и выполняется, когда появляется
/// <see cref="Shell.Current"/>: <see cref="ScheduleFlush"/> ставит переход в очередь главного
/// потока, а <see cref="FlushAsync"/> проверяет, что Shell уже есть.
/// </para>
/// <para>
/// Одна цель — один переход: после успешного <c>GoToAsync</c> ожидание стирается, чтобы
/// возврат на вкладку «Профиль» и обратно не переключал вкладки повторно. Deep link на
/// конкретную запись не делается: <c>GET api/bookings/{id}</c> не существует, а список
/// «Записи» и так показывает нужную запись.
/// </para>
/// </remarks>
public sealed class NotificationTapRouter
{
    private string? _pendingRoute;

    /// <summary>
    /// Есть ли отложенный переход.
    /// </summary>
    public bool HasPending => Volatile.Read(ref _pendingRoute) is not null;

    /// <summary>
    /// Разбирает extras intent'а и запоминает цель перехода.
    /// </summary>
    /// <remarks>
    /// Неизвестное сообщение (например, из будущего среза) молча игнорируется: приложение
    /// не должно открывать вкладку, которую это уведомление не имело в виду.
    /// </remarks>
    /// <param name="extras">Extras intent'а либо <c>null</c>.</param>
    public void HandleIntent(IDictionary<string, string>? extras)
    {
        if (!PushNotificationPayload.TryParse(extras, out var payload) || payload.Route is null)
        {
            return;
        }

        Volatile.Write(ref _pendingRoute, payload.Route);
    }

    /// <summary>
    /// Ставит отложенный переход в очередь главного потока.
    /// </summary>
    /// <remarks>
    /// Вызывается из <c>MainActivity</c> и <c>App.CreateWindow</c>: к моменту выполнения
    /// задача Shell уже собрана, даже если в момент нажатия её ещё не было.
    /// </remarks>
    public void ScheduleFlush()
    {
        if (!HasPending)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(FlushPending);
    }

    /// <summary>
    /// Выполняет отложенный переход, если Shell уже существует.
    /// </summary>
    /// <remarks>
    /// Если Shell ещё не собран, цель остаётся: следующий вызов (из <c>Window.Created</c>
    /// или из следующего нажатия) доведёт переход до конца.
    /// </remarks>
    public async Task FlushAsync()
    {
        var route = Volatile.Read(ref _pendingRoute);

        if (route is null)
        {
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => NavigateAsync(route));
        }
        catch (Exception)
        {
            // Сбой перехода не должен закрывать приложение: уведомление остаётся в шторке.
        }
    }

    private async Task NavigateAsync(string route)
    {
        var shell = Shell.Current;

        if (shell is null)
        {
            return;
        }

        // Цель снимается до перехода: повторный flush не должен переключать вкладку дважды.
        if (Interlocked.CompareExchange(ref _pendingRoute, null, route) != route)
        {
            return;
        }

        await shell.GoToAsync(route);
    }

    private async void FlushPending() => await FlushAsync();
}

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Держит пару «страница видна / страница скрыта» на всё время, пока она на экране.
/// </summary>
/// <remarks>
/// <para>
/// Экрану нужно знать не только про появление и уход, но и про уход приложения в фон: в фоне
/// <see cref="Page.OnDisappearing"/> не вызывается, а запросы из невидимого окна — это сеть
/// без человека и разряженная батарея. Поэтому пара собирается здесь, а не повторяется в
/// каждой странице: подписка на <c>Window.Stopped</c> и <c>Window.Resumed</c>
/// живёт ровно столько, сколько возвращённый <see cref="IDisposable"/>.
/// </para>
/// <para>
/// Если <c>Page.Window</c> на момент создания ещё <c>null</c> (страница создана, но
/// окна у неё нет), события окна не подписываются вовсе: пара «видна/скрыта» всё равно
/// обрамляет страницу через <c>Dispose</c>, просто фон и возврат из него не
/// различаются. Это не штатный путь из <c>OnAppearing</c>, но падать из-за него нельзя.
/// </para>
/// <para>
/// Это не ViewModel: страница и её окно — типы MAUI, и Core про них не знает.
/// </para>
/// </remarks>
public static class PageVisibility
{
    /// <summary>
    /// Подписывается на жизненный цикл окна и сразу сообщает, что страница видна.
    /// </summary>
    /// <param name="page">Страница, чьё окно слушаем.</param>
    /// <param name="shown">Страница видна: появление, возврат из фона.</param>
    /// <param name="hidden">Страница скрыта: уход, уход приложения в фон.</param>
    /// <returns>
    /// Область жизни подписки; <see cref="IDisposable.Dispose"/> снимает её и один раз сообщает
    /// «скрыта».
    /// </returns>
    public static IDisposable Watch(Page page, Action shown, Action hidden)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(hidden);

        return new Scope(page, shown, hidden);
    }

    /// <summary>
    /// Одна область жизни: подписка на окно и гарантия одного «скрыта» при закрытии.
    /// </summary>
    private sealed class Scope : IDisposable
    {
        private readonly Action _shown;
        private readonly Action _hidden;

        private Window? _window;
        private bool _disposed;

        /// <summary>
        /// Подписывается на окно (если оно уже есть) и сообщает «видна».
        /// </summary>
        /// <remarks>
        /// «Видна» идёт после подписки: иначе возврат из фона, пришедший между этими двумя
        /// шагами, остался бы незамеченным.
        /// </remarks>
        public Scope(Page page, Action shown, Action hidden)
        {
            _shown = shown;
            _hidden = hidden;

            if (page.Window is { } window)
            {
                _window = window;
                window.Stopped += OnStopped;
                window.Resumed += OnResumed;
            }

            shown();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Идемпотентность важна: OnDisappearing и закрытие области могут случиться дважды,
            // а двойное «скрыта» остановило бы цикл, который уже запустили заново.
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_window is not null)
            {
                _window.Stopped -= OnStopped;
                _window.Resumed -= OnResumed;
                _window = null;
            }

            _hidden();
        }

        private void OnStopped(object? sender, EventArgs e)
        {
            if (!_disposed)
            {
                _hidden();
            }
        }

        private void OnResumed(object? sender, EventArgs e)
        {
            if (!_disposed)
            {
                _shown();
            }
        }
    }
}

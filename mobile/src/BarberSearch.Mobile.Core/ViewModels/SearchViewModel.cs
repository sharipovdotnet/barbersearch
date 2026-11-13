using System.Collections.ObjectModel;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Mobile.Core.Api;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран поиска: город, список найденных барберов и состояние загрузки.
/// </summary>
/// <remarks>
/// ViewModel не знает про MAUI: ни про страницы, ни про <c>Dispatcher</c>. Это позволяет
/// проверять его обычным xunit без эмулятора. Состояний ровно три — грузим, показали список,
/// показали ошибку; пустой результат — это список без элементов, а не отдельное состояние.
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly IBarberSearchApi _api;

    /// <summary>
    /// Создаёт ViewModel поиска.
    /// </summary>
    /// <param name="api">Чтения API.</param>
    public SearchViewModel(IBarberSearchApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
    }

    /// <summary>
    /// Найденные барберы в порядке выдачи сервера.
    /// </summary>
    public ObservableCollection<BarberSearchItemResponse> Barbers { get; } = [];

    /// <summary>
    /// Город поиска; пусто — сервер не фильтрует.
    /// </summary>
    [ObservableProperty]
    private string _city = string.Empty;

    /// <summary>
    /// Идёт запрос: кнопка блокируется, показывается индикатор.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Текст ошибки для пользователя; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// Есть ли ошибка, которую надо показать; вычисляется из <see cref="ErrorMessage"/>.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Загружает первую страницу выдачи для текущего города.
    /// </summary>
    /// <remarks>
    /// Ошибки API и сети превращаются в текст для пользователя, отмена — штатный выход без
    /// сообщения: пользователь сам нажал «отменить», объяснять нечего.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var page = await _api.SearchBarbersAsync(City, cancellationToken: cancellationToken);

            Barbers.Clear();

            foreach (var barber in page.Items)
            {
                Barbers.Add(barber);
            }
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка: пользователь сам остановил поиск.
        }
        catch (BarberSearchApiException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Нет связи с сервером. Проверьте подключение и повторите.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

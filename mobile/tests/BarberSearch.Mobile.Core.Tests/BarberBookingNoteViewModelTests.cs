using System.Net;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Заметка к записи» кабинета мастера: начальное значение, счётчик,
/// предел тысячи символов, обрезка при сохранении, пустая заметка и разбор отказа.
/// </summary>
/// <remarks>
/// Сохранение — запись, поэтому повтора у экрана нет вовсе: после сбоя исход неизвестен,
/// и тест проверяет, что второй запрос сам не уходит. Сеть и время не участвуют: ответ
/// клиента подменён, строка записи строится из фикстуры.
/// </remarks>
public sealed class BarberBookingNoteViewModelTests
{
    private const string TooLongText = "Слишком длинная заметка — максимум 1000 символов.";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    [Fact]
    public void Initialize_TakesNoteHeaderAndCounterFromItem()
    {
        var api = new FakeBarberBookingsApi();
        var viewModel = new BarberBookingNoteViewModel(api);
        var item = BarberCabinetFixtures.Item(BarberCabinetFixtures.Booking(
            barberNote: "старая заметка",
            startAt: new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(3))));

        viewModel.Initialize(item);

        Assert.True(viewModel.IsInitialized);
        Assert.Equal("старая заметка", viewModel.Note);
        Assert.Equal("Иван Петров", viewModel.CustomerName);
        Assert.Equal(item.MomentText, viewModel.MomentText);
        Assert.Equal("14 / 1000", viewModel.CounterText);
        Assert.False(viewModel.IsTooLong);
        Assert.True(viewModel.CanSave);
        Assert.Equal("Сохранить", viewModel.SubmitText);
    }

    [Fact]
    public void NoteText_AtLimit_AllowsSaveAndOverLimit_DisablesIt()
    {
        var viewModel = new BarberBookingNoteViewModel(new FakeBarberBookingsApi());

        viewModel.Initialize(BarberCabinetFixtures.Item(BarberCabinetFixtures.Booking()));

        viewModel.Note = new string('а', BarberBookingNoteViewModel.MaxNoteLength);

        Assert.False(viewModel.IsTooLong);
        Assert.Equal(string.Empty, viewModel.LengthError);
        Assert.True(viewModel.CanSave);
        Assert.True(viewModel.SaveCommand.CanExecute(null));

        viewModel.Note = new string('а', BarberBookingNoteViewModel.MaxNoteLength + 1);

        Assert.True(viewModel.IsTooLong);
        Assert.Equal(TooLongText, viewModel.LengthError);
        Assert.False(viewModel.CanSave);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.Equal("1001 / 1000", viewModel.CounterText);
    }

    [Fact]
    public async Task SaveAsync_TextWithSpaces_SendsTrimmedTextAndRaisesSaved()
    {
        var api = new FakeBarberBookingsApi();
        var viewModel = new BarberBookingNoteViewModel(api);
        var item = BarberCabinetFixtures.Item(BarberCabinetFixtures.Booking());
        string? saved = null;

        viewModel.Saved += (_, note) => saved = note;
        viewModel.Initialize(item);
        viewModel.Note = "  Покороче сзади  ";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(api.NoteRequests);

        Assert.Equal(item.Id, request.BookingId);
        Assert.Equal("Покороче сзади", request.Note);
        Assert.Equal("Покороче сзади", saved);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsSaving);
        Assert.Equal("Сохранить", viewModel.SubmitText);
    }

    [Fact]
    public async Task SaveAsync_WhitespaceOnly_SavesEmptyStringAndRaisesEmptyPayload()
    {
        var api = new FakeBarberBookingsApi();
        var viewModel = new BarberBookingNoteViewModel(api);
        string? saved = null;

        viewModel.Saved += (_, note) => saved = note;
        viewModel.Initialize(BarberCabinetFixtures.Item(BarberCabinetFixtures.Booking(
            barberNote: "старая заметка")));
        viewModel.Note = "   ";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, Assert.Single(api.NoteRequests).Note);
        Assert.Equal(string.Empty, saved);
    }

    [Fact]
    public async Task SaveAsync_TooLongCode_ShowsMappedMessage()
    {
        var api = new FakeBarberBookingsApi
        {
            SaveNote = (_, _, _) => Task.FromException(
                BarberCabinetApiError.Create(
                    HttpStatusCode.BadRequest,
                    "booking.barberNote.tooLong")),
        };

        var viewModel = new BarberBookingNoteViewModel(api);

        viewModel.Initialize(BarberCabinetFixtures.Item(BarberCabinetFixtures.Booking()));
        viewModel.Note = "Покороче";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(TooLongText, viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsSaving);
        Assert.True(viewModel.CanSave);
    }

    [Fact]
    public async Task SaveAsync_NetworkFailure_ShowsReasonAndDoesNotRetryWrite()
    {
        var api = new FakeBarberBookingsApi
        {
            SaveNote = (_, _, _) => Task.FromException(new HttpRequestException("offline")),
        };

        var viewModel = new BarberBookingNoteViewModel(api);

        viewModel.Initialize(BarberCabinetFixtures.Item(BarberCabinetFixtures.Booking()));
        viewModel.Note = "Покороче";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(NetworkText, viewModel.ErrorMessage);
        Assert.Equal("Покороче", Assert.Single(api.NoteRequests).Note);

        // Повтор записи не запускается сам: после сбоя известно только то, что ответа не было.
        Assert.Single(api.NoteRequests);
        Assert.False(viewModel.IsSaving);
    }
}

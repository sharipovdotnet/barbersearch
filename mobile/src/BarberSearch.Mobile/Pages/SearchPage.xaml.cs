using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

public partial class SearchPage : ContentPage
{
	public SearchPage(SearchViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(viewModel);

		InitializeComponent();

		BindingContext = viewModel;
	}

	/// <summary>
	/// Открывает карточку выбранного барбера.
	/// </summary>
	/// <remarks>
	/// Выбор сбрасывается сразу, а не после возврата: иначе повторное нажатие по той же
	/// строке не подняло бы событие и карточка не открылась бы второй раз.
	/// </remarks>
	private async void OnBarberSelected(object? sender, SelectionChangedEventArgs e)
	{
		var barber = e.CurrentSelection.Count > 0
			? e.CurrentSelection[0] as BarberSearchItemResponse
			: null;

		if (sender is CollectionView collectionView)
		{
			collectionView.SelectedItem = null;
		}

		if (barber is null)
		{
			return;
		}

		await Shell.Current.GoToAsync(string.Concat(BarberPage.Route, "?id=", barber.Id.ToString("D")));
	}
}

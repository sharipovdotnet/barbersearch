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
}

using BarberSearch.Mobile.Pages;

namespace BarberSearch.Mobile;

public partial class AppShell : Shell
{
	public AppShell(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);

		InitializeComponent();

		SearchTab.Content = services.GetRequiredService<SearchPage>();
		MapTab.Content = services.GetRequiredService<MapPage>();
		BookingsTab.Content = services.GetRequiredService<BookingsPage>();
		ProfileTab.Content = services.GetRequiredService<ProfilePage>();
	}
}

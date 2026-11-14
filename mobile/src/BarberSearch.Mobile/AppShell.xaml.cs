using BarberSearch.Mobile.Navigation;
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

		// Карточка барбера — не вкладка, а маршрут поверх стека «Поиска»: так возврат
		// к списку сохраняет и город, и найденных барберов.
		Routing.RegisterRoute(BarberPage.Route, new PageRouteFactory<BarberPage>(services));

		// Перенос и отзыв открываются поверх стека «Записей» и закрываются возвратом:
		// список под ними сохраняет вкладку, прокрутку и уже загруженные строки.
		Routing.RegisterRoute(
			RescheduleBookingPage.Route,
			new PageRouteFactory<RescheduleBookingPage>(services));
		Routing.RegisterRoute(
			ReviewBookingPage.Route,
			new PageRouteFactory<ReviewBookingPage>(services));

		// Разделы кабинета открываются поверх стека «Профиля»: вкладка остаётся хабом,
		// а возврат не теряет ни вход, ни загруженный профиль.
		Routing.RegisterRoute(
			AccountProfilePage.Route,
			new PageRouteFactory<AccountProfilePage>(services));
		Routing.RegisterRoute(
			SecurityPage.Route,
			new PageRouteFactory<SecurityPage>(services));
		Routing.RegisterRoute(
			MyReviewsPage.Route,
			new PageRouteFactory<MyReviewsPage>(services));
		Routing.RegisterRoute(
			EditReviewPage.Route,
			new PageRouteFactory<EditReviewPage>(services));
	}
}

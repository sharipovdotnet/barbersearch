using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Navigation;
using BarberSearch.Mobile.Pages;

namespace BarberSearch.Mobile;

public partial class AppShell : Shell
{
	/// <summary>
	/// Маршруты Shell регистрируются один раз: смена языка создаёт новый Shell, но таблица
	/// маршрутов у Shell глобальная, и повторная регистрация того же маршрута — ошибка.
	/// </summary>
	private static bool _routesRegistered;

	public AppShell(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);

		InitializeComponent();

		SearchTab.Content = services.GetRequiredService<SearchPage>();
		MapTab.Content = services.GetRequiredService<MapPage>();
		BookingsTab.Content = services.GetRequiredService<BookingsPage>();
		ProfileTab.Content = services.GetRequiredService<ProfilePage>();

		if (_routesRegistered)
		{
			return;
		}

		_routesRegistered = true;

		// Карточка мастера — не вкладка, а маршрут поверх стека «Поиска»: так возврат
		// к списку сохраняет и город, и найденных мастеров.
		Routing.RegisterRoute(BarberPage.Route, new PageRouteFactory<BarberPage>(services));

		// Выбор услуги тоже открывается поверх вкладки «Поиск»: возврат сохраняет
		// введённые фильтры и найденную выдачу, а сам выбор сразу применяется к фильтру.
		Routing.RegisterRoute(
			HairServiceSelectPage.Route,
			new PageRouteFactory<HairServiceSelectPage>(services));

		// Просмотр работ открывается поверх карточки: возврат оставляет её на прежнем месте,
		// а системная кнопка «назад» закрывает просмотр без своего кода.
		Routing.RegisterRoute(
			PortfolioPhotoPage.Route,
			new PageRouteFactory<PortfolioPhotoPage>(services));

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

		// Заявка на роль мастера открывается строкой из хаба «Профиля» и живёт поверх его
		// стека: возврат оставляет вкладку и загруженный профиль на месте.
		Routing.RegisterRoute(
			BarberRequestPage.Route,
			new PageRouteFactory<BarberRequestPage>(services));

		// Кабинет мастера — один экран с четырьмя разделами, тоже поверх стека «Профиля»:
		// маршрут у кабинета один, а разделы переключаются внутри страницы. Дочерние экраны
		// кабинета открываются поверх него и закрываются возвратом, поэтому кабинет
		// перечитывает /me и активирует свой раздел на Pop.
		Routing.RegisterRoute(
			BarberCabinetRoutes.Cabinet,
			new PageRouteFactory<BarberCabinetPage>(services));
		Routing.RegisterRoute(
			BarberCabinetRoutes.CreateBooking,
			new PageRouteFactory<BarberBookingCreatePage>(services));
		Routing.RegisterRoute(
			BarberCabinetRoutes.RescheduleBooking,
			new PageRouteFactory<BarberBookingReschedulePage>(services));
		Routing.RegisterRoute(
			BarberCabinetRoutes.BookingNote,
			new PageRouteFactory<BarberBookingNotePage>(services));
		Routing.RegisterRoute(
			BarberCabinetRoutes.Onboarding,
			new PageRouteFactory<BarberOnboardingPage>(services));
		Routing.RegisterRoute(
			BarberCabinetRoutes.Notifications,
			new PageRouteFactory<BarberNotificationsPage>(services));
		Routing.RegisterRoute(
			BarberCabinetRoutes.EditProfile,
			new PageRouteFactory<BarberProfileEditPage>(services));
		Routing.RegisterRoute(
			BarberCabinetRoutes.ServiceEditor,
			new PageRouteFactory<BarberServiceEditorPage>(services));

		// Создание и добавление салона открываются кнопками из раздела «Салоны» и живут
		// поверх кабинета: возврат оставляет раздел на месте, а сам раздел перечитывает
		// список на Pop.
		Routing.RegisterRoute(
			BarberCabinetRoutes.CreateSalon,
			new PageRouteFactory<BarberSalonCreatePage>(services));
		Routing.RegisterRoute(
			BarberCabinetRoutes.AddSalon,
			new PageRouteFactory<BarberSalonAddPage>(services));

		// Фото портфолио открывается нажатием на плитку в «Профиле» кабинета и живёт
		// поверх него: возврат оставляет и кабинет, и выбранный раздел на месте, а
		// раздел перечитывает карточку на Pop.
		Routing.RegisterRoute(
			BarberPhotoPage.Route,
			new PageRouteFactory<BarberPhotoPage>(services));

		// Регистрация живёт там же, рядом с формой входа: возврат с неё оставляет вкладку
		// «Профиль» на месте, а после успеха страница закрывается сама.
		Routing.RegisterRoute(
			RegisterPage.Route,
			new PageRouteFactory<RegisterPage>(services));

		// Запрос ссылки сброса пароля стоит рядом с формой входа: возврат оставляет вкладку
		// «Профиль» на месте, а сама ссылка из письма открывается уже в браузере.
		Routing.RegisterRoute(
			ForgotPasswordPage.Route,
			new PageRouteFactory<ForgotPasswordPage>(services));
		Routing.RegisterRoute(
			SecurityPage.Route,
			new PageRouteFactory<SecurityPage>(services));
		Routing.RegisterRoute(
			MyReviewsPage.Route,
			new PageRouteFactory<MyReviewsPage>(services));
		Routing.RegisterRoute(
			EditReviewPage.Route,
			new PageRouteFactory<EditReviewPage>(services));

		// Админка открывается строкой из «Профиля» и живёт поверх его стека: возврат оставляет
		// вкладку на месте. Хаб — корень раздела, разделы открываются с него и возвращаются
		// на него же, поэтому перечитанная сводка и есть обновление после действия.
		Routing.RegisterRoute(
			AdminHubPage.Route,
			new PageRouteFactory<AdminHubPage>(services));

		// Разделы открываются строками хаба и возвращаются на него же: хаб перечитывает
		// сводку при появлении, поэтому счётчик очереди обновляется сам после решения.
		Routing.RegisterRoute(
			AdminReviewsPage.Route,
			new PageRouteFactory<AdminReviewsPage>(services));
		Routing.RegisterRoute(
			AdminReviewPage.Route,
			new PageRouteFactory<AdminReviewPage>(services));
		Routing.RegisterRoute(
			AdminPhotosPage.Route,
			new PageRouteFactory<AdminPhotosPage>(services));
		Routing.RegisterRoute(
			AdminMastersPage.Route,
			new PageRouteFactory<AdminMastersPage>(services));
		Routing.RegisterRoute(
			AdminMasterPage.Route,
			new PageRouteFactory<AdminMasterPage>(services));
		Routing.RegisterRoute(
			AdminRequestsPage.Route,
			new PageRouteFactory<AdminRequestsPage>(services));
		Routing.RegisterRoute(
			AdminRequestPage.Route,
			new PageRouteFactory<AdminRequestPage>(services));
	}
}

using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services, ILocalization localization)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(localization);

		// Язык читается здесь, а не в MauiProgram: к этому моменту платформенные настройки
		// уже доступны. До создания Shell остаётся выставить модульный Loc, которым
		// пользуются XAML-разметка и статические форматтеры.
		localization.Initialize();
		Loc.Current = localization;

		InitializeComponent();

		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Shell собирается из контейнера: страницы получают свои ViewModel через конструктор,
		// а не через Activator.CreateInstance из DataTemplate.
		return new Window(_services.GetRequiredService<AppShell>());
	}
}

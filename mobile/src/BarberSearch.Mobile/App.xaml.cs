namespace BarberSearch.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);

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

namespace Sapling.Mobile;

public partial class App : Application
{
	public App()
	{
		UserAppTheme = AppTheme.Dark;
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage());
}

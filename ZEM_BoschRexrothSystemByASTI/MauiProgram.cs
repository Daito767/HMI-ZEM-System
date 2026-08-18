using Microsoft.Extensions.Logging;
using ZEM_BoschRexrothSystemByASTI.Plc;

namespace ZEM_BoschRexrothSystemByASTI;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

		builder.Services.AddMauiBlazorWebView();

		builder.Services.AddSingleton(Preferences.Default);
		builder.Services.AddSingleton(SecureStorage.Default);
		builder.Services.AddSingleton<HmiSettingsStore>();
		builder.Services.AddSingleton<PlcService>();
		builder.Services.AddSingleton<UiState>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
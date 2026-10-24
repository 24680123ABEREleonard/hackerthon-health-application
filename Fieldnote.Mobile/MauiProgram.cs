using Fieldnote.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace Fieldnote.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		SQLitePCL.Batteries_V2.Init();
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});
		builder.Services.AddSingleton<CaseDatabase>();
		builder.Services.AddSingleton<WorkflowAssistant>();
		builder.Services.AddSingleton<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}

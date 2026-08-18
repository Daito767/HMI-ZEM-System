using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using View = Android.Views.View;

namespace ZEM_BoschRexrothSystemByASTI;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
	ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
	                       ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity, View.IOnApplyWindowInsetsListener
{
	/// <summary>
	/// From Android 15 the window is edge to edge and the WebView draws under the status bar and the
	/// navigation bar. The insets are applied here rather than in CSS: an Android WebView does not
	/// reliably report <c>env(safe-area-inset-*)</c>, and a hidden top row on a control panel is not
	/// a cosmetic problem.
	/// </summary>
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		FindViewById(Android.Resource.Id.Content)?.SetOnApplyWindowInsetsListener(this);
	}

	public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
	{
		if (OperatingSystem.IsAndroidVersionAtLeast(30))
		{
			var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
			view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
		}
		else
		{
#pragma warning disable CA1422 // the pre-30 API is the only one there is on those versions
			view.SetPadding(
				insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop,
				insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
#pragma warning restore CA1422
		}

		return insets;
	}
}

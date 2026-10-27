using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keys in an app's window of their own, on a headset (Android XR): the
/// headset hangs an overlay on a layer that moves with the user's head, and
/// an app's window in space, as a panel the user moves and sizes by its
/// border and places beside the keyboard, where it stays. The window never
/// takes the focus, so the field being typed in keeps it and the keyboard
/// stays open; the keys go out through <see cref="KeyPlusService"/>, which
/// opens this window when it is turned on. A task of its own, apart from the
/// app's screen.
/// </summary>
[Activity(Label = "@string/service_label", Exported = false,
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    TaskAffinity = "ai.berpiztu.keyplus.keys", LaunchMode = LaunchMode.SingleInstance,
    ExcludeFromRecents = true, ResizeableActivity = true)]
// As large as the keys' three rows when it opens, and as small as the user
// takes it, down to a pixel: without a least size of its own the system's
// keeps it tall.
[Layout(DefaultWidth = "640dp", DefaultHeight = "240dp", MinWidth = "1px", MinHeight = "1px")]
public sealed class KeyWindowActivity : Activity
{
    private KeyPanel? panel;
    private PanelPlacement? placed;

    /// <summary>Whether the window is in sight: not minimized nor closed.</summary>
    public static bool InSight { get; private set; }

    /// <summary>Whether ⤓ is filled, docked: the window is brought back, or opened again, when the keyboard opens.</summary>
    private const string DockedKey = "keysWindow.docked";

    /// <summary>Whether the window is docked (⤓ filled), which it is until ⤓ says not.</summary>
    public static bool Docked(Android.Content.Context context) => KeyPlusPreferences.Global(context).GetBoolean(DockedKey, true);

    protected override void OnStart()
    {
        base.OnStart();
        InSight = true;
    }

    protected override void OnStop()
    {
        InSight = false;
        base.OnStop();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Touched, it stays out of the focus: the field keeps it, and the keyboard with it.
        Window!.AddFlags(WindowManagerFlags.NotFocusable);

        if (KeyPlusService.Current is not { } service)
        {
            Toast.MakeText(this, "Turn KeyPlus keys on first", ToastLength.Long)!.Show();
            Finish();
            return;
        }

        var screen = KeyPlusPreferences.ScreenOf(this, Display?.DisplayId ?? Android.Views.Display.DefaultDisplay);
        var preferences = KeyPlusPreferences.ForScreen(this, screen);
        placed = new PanelPlacement(preferences);
        panel = new KeyPanel(this, preferences, service.Clipboard, service.SendKey, service.TypeText)
        {
            OffersWholeKeyboard = false,

            // ⤓ is filled while docked, which the panel calls not floating.
            Floating = !Docked(this),
        };
        placed.Mode = panel.Mode;
        panel.Scale = placed.Scale;
        panel.ModeChanged += () =>
        {
            placed.Mode = panel.Mode;
            panel.Scale = placed.Scale;
        };
        panel.UsualSizeRequested += () =>
        {
            placed.Scale = 1f;
            placed.Save();
            panel.Scale = 1f;
        };
        panel.CloseRequested += Finish;
        panel.DockRequested += () =>
        {
            var docked = !Docked(this);
            KeyPlusPreferences.Global(this).Edit()!.PutBoolean(DockedKey, docked)!.Apply();
            panel.Floating = !docked;
        };
        panel.KeyboardToggle = service.ToggleKeyboard;

        // Test: the keys attached into another app's window.
        panel.Settings =
        [
            new(GetString(Resource.String.attach_keyboard)!, () => service.AttachKeys(toKeyboard: true)),
            new(GetString(Resource.String.attach_app)!, () => service.AttachKeys(toKeyboard: false)),
            new(GetString(Resource.String.detach_keys)!, service.DetachKeys),
        ];
        SetContentView(panel.Root);
        service.WindowPanel = panel;
    }

    protected override void OnDestroy()
    {
        panel?.StopRepeat();
        if (KeyPlusService.Current is { } service && service.WindowPanel == panel)
        {
            service.WindowPanel = null;
        }

        base.OnDestroy();
    }
}

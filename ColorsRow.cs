using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Tema's Colores in one row of one size whatever it holds, as the Windows
/// app's: the choice (Sistema, Claro, Oscuro, Personalizado), and under
/// Personalizado alone Por app, then, with it on, the applications typed in
/// and, for the one chosen, De la app (its theme's background), then the two
/// colours (that application's, with one chosen) and Deshacer. A part that
/// does not apply is hidden in its place; the row made smaller as a whole
/// where the page is narrower, never cut. Each change kept at once, told
/// (<c>changed</c>) for the page to be made again in it.
/// </summary>
public sealed class ColorsRow : FrameLayout
{
    private const float GapDp = 4;
    private const float TextSp = 13;

    /// <summary>The application looked at, kept while the page is made again.</summary>
    private static string? lookedAtApp;

    private readonly LinearLayout row;
    private readonly Action changed;
    private float scale = 1;

    public ColorsRow(Context context, Action changed)
        : base(context)
    {
        this.changed = changed;
        var global = KeyPlusPreferences.Global(context);
        var look = KeyPlusPreferences.Look(context);
        var custom = look == KeyboardLook.Custom;
        var perApp = global.GetBoolean(KeyPlusPreferences.PerAppKey, false);
        var app = custom ? AppLookedAt(context) : null;
        var appColor = app is not null ? AppThemes.For(context, app) : null;
        var showing = StandardColors.Read(context, app);
        row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        AddView(row);

        row.AddView(Words(Resource.String.look));
        var looks = Choices(KeyboardLooks.All.Select(choice => context.GetString(KeyboardLooks.Name(choice))).ToList(), Array.IndexOf(KeyboardLooks.All, look), picked =>
        {
            if (KeyboardLooks.All[picked] != look)
            {
                global.Edit()!.PutString(KeyPlusPreferences.LookKey, KeyboardLooks.All[picked].ToString())!.Apply();
                changed();
            }
        });
        row.AddView(looks);

        var on = Check(Resource.String.per_app, perApp, custom, isOn =>
        {
            global.Edit()!.PutBoolean(KeyPlusPreferences.PerAppKey, isOn)!.Apply();
            changed();
        });
        row.AddView(on);

        var known = AppThemes.All(context).Keys.ToList();
        var apps = Choices(known.Select(package => AppThemes.Name(context, package)).ToList(), app is null ? -1 : known.IndexOf(app), picked =>
        {
            // Only a choice made: a list hidden, or just filled, tells its first one too.
            if (custom && perApp && known[picked] != app)
            {
                lookedAtApp = known[picked];
                changed();
            }
        });
        apps.Visibility = custom && perApp ? ViewStates.Visible : ViewStates.Invisible;
        row.AddView(apps);

        // De la app: the application chosen takes its theme's background; left, its own colours start exactly as it showed.
        var own = Check(Resource.String.app_own, appColor?.Look == AppLook.Own, appColor is not null, isOwn =>
        {
            if (app is not null && appColor is not null)
            {
                AppThemes.Set(context, app, isOwn
                    ? appColor with { Look = AppLook.Own }
                    : appColor with { Look = AppLook.Custom, KeyColor = appColor.WindowColor ?? appColor.KeyColor, AccentColor = null });
                changed();
            }
        });
        row.AddView(own);

        // The two colours: the application's chosen, else Tema's Personalizado's; hidden for an application taking its theme's.
        void Choose(bool keyboard, Rgb chosen)
        {
            if (app is not null && appColor is not null)
            {
                AppThemes.Set(context, app, (keyboard ? appColor with { KeyColor = chosen.Text } : appColor with { AccentColor = chosen.Text }) with { Look = AppLook.Custom });
            }
            else
            {
                // Tema's: the two as they were kept for Deshacer.
                var before = global.GetString(KeyPlusPreferences.KeyColorKey, "") + "|" + global.GetString(KeyPlusPreferences.AccentColorKey, "");
                global.Edit()!
                    .PutString(keyboard ? KeyPlusPreferences.KeyColorKey : KeyPlusPreferences.AccentColorKey, chosen.Text)!
                    .PutString(KeyPlusPreferences.ColorsBeforeKey, before)!
                    .Apply();
            }

            changed();
        }

        var colorsShown = custom && appColor?.Look != AppLook.Own;
        row.AddView(ColorButton(Resource.String.look_keyboard, showing.Ground, colorsShown, chosen => Choose(keyboard: true, chosen)));
        row.AddView(ColorButton(Resource.String.look_accent, showing.Primary, colorsShown, chosen => Choose(keyboard: false, chosen)));

        // Deshacer, Tema's Personalizado's: its two colours as they were before the last one was chosen, one step back.
        var before = global.GetString(KeyPlusPreferences.ColorsBeforeKey, null);
        var undo = Pill("↶", custom && app is null && before is not null);
        undo.Click += (_, _) =>
        {
            var (key, accent) = before!.Split('|') is [var k, var a] ? (k, a) : ("", "");
            global.Edit()!
                .PutString(KeyPlusPreferences.KeyColorKey, key.Length > 0 ? key : null)!
                .PutString(KeyPlusPreferences.AccentColorKey, accent.Length > 0 ? accent : null)!
                .Remove(KeyPlusPreferences.ColorsBeforeKey)!
                .Apply();
            changed();
        };
        row.AddView(undo);
    }

    /// <summary>The application looked at, with Personalizado's Por app on: the one chosen, or the first typed in; none, Tema's colours.</summary>
    public static string? AppLookedAt(Context context)
    {
        var apps = AppThemes.All(context);
        lookedAtApp = lookedAtApp is not null && apps.ContainsKey(lookedAtApp) ? lookedAtApp : apps.Keys.FirstOrDefault();
        return KeyPlusPreferences.Look(context) == KeyboardLook.Custom && KeyPlusPreferences.Global(context).GetBoolean(KeyPlusPreferences.PerAppKey, false)
            ? lookedAtApp
            : null;
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var unspecified = MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified);
        row.Measure(unspecified, unspecified);
        var available = MeasureSpec.GetSize(widthMeasureSpec);
        scale = Math.Min(1f, available / (float)Math.Max(1, row.MeasuredWidth));
        SetMeasuredDimension(available, (int)Math.Ceiling(row.MeasuredHeight * scale));
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        row.Layout(0, 0, row.MeasuredWidth, row.MeasuredHeight);
        row.PivotX = row.PivotY = 0;
        row.ScaleX = row.ScaleY = scale;
    }

    private TextView Words(int text)
    {
        var words = new TextView(Context) { Text = Context!.GetString(text) };
        words.SetTextSize(ComplexUnitType.Sp, TextSp);
        words.SetPadding(0, 0, Dp(GapDp), 0);
        return words;
    }

    private CheckBox Check(int text, bool on, bool shown, Action<bool> turned)
    {
        var check = new CheckBox(Context) { Text = Context!.GetString(text), Checked = on, Visibility = shown ? ViewStates.Visible : ViewStates.Invisible };
        check.SetTextSize(ComplexUnitType.Sp, TextSp);
        check.Click += (_, _) => turned(check.Checked);
        check.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(GapDp) };
        return check;
    }

    /// <summary>A list in small type, as wide as its longest choice whatever it shows, so a choice made moves nothing.</summary>
    private Spinner Choices(IReadOnlyList<string> words, int chosen, Action<int> picked)
    {
        var list = new Spinner(Context);
        list.Adapter = new SmallWords(Context!, words, TextSp);
        if (chosen >= 0)
        {
            list.SetSelection(chosen, false);
        }

        list.ItemSelected += (_, e) => picked(e.Position);
        var paint = new TextPaint { TextSize = TypedValue.ApplyDimension(ComplexUnitType.Sp, TextSp, Resources!.DisplayMetrics) };
        var widest = words.Count == 0 ? 0 : words.Max(word => paint.MeasureText(word));
        list.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(GapDp) };
        list.Post(() =>
        {
            // Its frame and arrow as drawn, less the word it shows, and the longest word.
            var shown = list.SelectedItem?.ToString() ?? "";
            list.LayoutParameters!.Width = (int)Math.Ceiling(list.Width - paint.MeasureText(shown) + widest);
            list.RequestLayout();
        });
        return list;
    }

    /// <summary>A pill with a sign or a few words, a finger's room around them, hidden in its place where it does not apply.</summary>
    private TextView Pill(string text, bool shown)
    {
        var pill = new TextView(Context) { Text = text, Gravity = GravityFlags.Center, Clickable = true, Visibility = shown ? ViewStates.Visible : ViewStates.Invisible };
        pill.SetTextSize(ComplexUnitType.Sp, TextSp);
        var ground = new GradientDrawable();
        ground.SetStroke(Dp(1), new Color(Context!.GetColor(Android.Resource.Color.DarkerGray)));
        ground.SetCornerRadius(Dp(6));
        pill.Background = ground;
        pill.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));
        pill.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(GapDp) };
        return pill;
    }

    /// <summary>One of Personalizado's colours: its colour shown before its name; a tap, the colour picker on it.</summary>
    private TextView ColorButton(int name, Color shown, bool visible, Action<Rgb> chosen)
    {
        var button = Pill("■ " + Context!.GetString(name), visible);
        var words = new Android.Text.SpannableString(button.Text);
        words.SetSpan(new Android.Text.Style.ForegroundColorSpan(shown), 0, 1, Android.Text.SpanTypes.ExclusiveExclusive);
        button.TextFormatted = words;
        button.Click += (_, _) => ColorPicker.Show(Context!, new Rgb(shown.R, shown.G, shown.B), chosen);
        return button;
    }

    private int Dp(float value) => (int)((value * Resources!.DisplayMetrics!.Density) + 0.5f);

    /// <summary>A list's words in the size asked, the shown one and those dropped down alike.</summary>
    private sealed class SmallWords(Context context, IReadOnlyList<string> words, float sp)
        : ArrayAdapter<string>(context, Android.Resource.Layout.SimpleSpinnerItem, words.ToList())
    {
        public override View GetView(int position, View? convertView, ViewGroup parent) => Small(base.GetView(position, convertView, parent));

        public override View? GetDropDownView(int position, View? convertView, ViewGroup? parent) => Small(base.GetDropDownView(position, convertView, parent));

        private View Small(View? view)
        {
            (view as TextView)?.SetTextSize(ComplexUnitType.Sp, sp);
            return view!;
        }
    }
}

/// <summary>
/// A colour chosen on Android, which has no picker of its own as Windows
/// does: its hue, saturation and brightness on three bars, the colour shown
/// above them as it is made, Aceptar keeping it.
/// </summary>
public static class ColorPicker
{
    public static void Show(Context context, Rgb start, Action<Rgb> chosen)
    {
        var hsv = new float[3];
        Color.ColorToHSV(new Color(start.R, start.G, start.B), hsv);
        var density = context.Resources!.DisplayMetrics!.Density;
        int Dp(float value) => (int)((value * density) + 0.5f);
        var sample = new View(context);
        void Paint() => sample.SetBackgroundColor(Color.HSVToColor(hsv));
        Paint();

        var form = new LinearLayout(context) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(20), Dp(12), Dp(20), 0);
        form.AddView(sample, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48)) { BottomMargin = Dp(8) });
        void Bar(int name, int part, int most)
        {
            form.AddView(new TextView(context) { Text = context.GetString(name) });
            var bar = new SeekBar(context) { Max = most, Progress = (int)Math.Round(hsv[part] * (part == 0 ? 1 : most)) };
            bar.ProgressChanged += (_, e) =>
            {
                hsv[part] = part == 0 ? e.Progress : e.Progress / (float)most;
                Paint();
            };
            form.AddView(bar);
        }

        Bar(Resource.String.hue, 0, 360);
        Bar(Resource.String.saturation, 1, 100);
        Bar(Resource.String.brightness, 2, 100);
        new AlertDialog.Builder(context)
            .SetView(form)!
            .SetPositiveButton(Resource.String.ok, (_, _) =>
            {
                var made = new Color(Color.HSVToColor(hsv));
                chosen(new Rgb(made.R, made.G, made.B));
            })!
            .SetNegativeButton(Resource.String.cancel, (_, _) => { })!
            .Show();
    }
}

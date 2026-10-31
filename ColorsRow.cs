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
/// Tema's Colores in two lines of one size whatever they hold, as the Windows settings' (the owner's, 2026-10-25): the
/// choice (Sistema, Claro, Oscuro, Personalizado, Por aplicación), the two colours and Deshacer: Personalizado's for
/// every application, or, Por aplicación, the one chosen's; under them Aplicación, the applications typed in and, for
/// the one chosen, De la app (its theme's background). Por aplicación is Personalizado with Por app on, as kept before.
/// Nothing is hidden: a part that does not apply is there, turned off and faded. The two lists as wide as the choices'
/// longest, whatever they hold (an application's list dropped down shows the names whole); the lines made smaller as a
/// whole where the page is narrower, never cut. Each change kept at once, told (<c>changed</c>) for the page to be made
/// again in it.
/// </summary>
public sealed class ColorsRow : FrameLayout
{
    private const float GapDp = 4;

    /// <summary>A part turned off, faded so it reads as off; a settings row that does not apply now alike.</summary>
    public const float OffAlpha = 0.35f;

    /// <summary>Tema's small type, its labels' and its lists' alike (<see cref="ThemeView"/>).</summary>
    public const float TextSp = 13;

    /// <summary>The application looked at, kept while the page is made again.</summary>
    private static string? lookedAtApp;

    private readonly LinearLayout lines;
    private readonly Action changed;
    private readonly Spinner looks;
    private readonly Spinner apps;

    /// <summary>Each list's width for its longest choice, once laid out (<see cref="Choices"/>).</summary>
    private readonly Dictionary<Spinner, int> widths = [];
    private float scale = 1;

    public ColorsRow(Context context, Action changed)
        : base(context)
    {
        this.changed = changed;
        var global = KeyPlusPreferences.Global(context);
        var look = KeyPlusPreferences.Look(context);
        var custom = look == KeyboardLook.Custom;
        var perApp = custom && global.GetBoolean(KeyPlusPreferences.PerAppKey, false);
        var app = perApp ? AppLookedAt(context) : null;
        var appColor = app is not null ? AppThemes.For(context, app) : null;
        var showing = StandardColors.Read(context, app);
        lines = new LinearLayout(context) { Orientation = Orientation.Vertical };
        AddView(lines);
        var colorsLine = Line();
        var appLine = Line();

        // The two lines' words as wide as the wider, so the lists stand one under the other.
        var colorsWords = Words(Resource.String.look);
        var appWords = Words(Resource.String.app_list);
        var wordsWidth = (int)Math.Ceiling(Math.Max(colorsWords.Paint!.MeasureText(colorsWords.Text), appWords.Paint!.MeasureText(appWords.Text))) + Dp(GapDp);
        colorsWords.LayoutParameters = new LinearLayout.LayoutParams(wordsWidth, ViewGroup.LayoutParams.WrapContent);
        appWords.LayoutParameters = new LinearLayout.LayoutParams(wordsWidth, ViewGroup.LayoutParams.WrapContent);

        // Tema's choices, then Por aplicación, Personalizado with each application's colours.
        var choices = KeyboardLooks.All.Select(choice => context.GetString(KeyboardLooks.Name(choice))).Append(context.GetString(Resource.String.per_app)!).ToList();
        colorsLine.AddView(colorsWords);
        looks = Choices(choices, perApp ? KeyboardLooks.All.Length : Array.IndexOf(KeyboardLooks.All, look), picked =>
        {
            var toPerApp = picked == KeyboardLooks.All.Length;
            var chosen = toPerApp ? KeyboardLook.Custom : KeyboardLooks.All[picked];
            if (chosen != look || toPerApp != perApp)
            {
                global.Edit()!
                    .PutString(KeyPlusPreferences.LookKey, chosen.ToString())!
                    .PutBoolean(KeyPlusPreferences.PerAppKey, toPerApp)!
                    .Apply();
                changed();
            }
        });
        ((LinearLayout.LayoutParams)looks.LayoutParameters!).LeftMargin = 0;
        colorsLine.AddView(looks);

        // The two colours: the application's chosen, else Personalizado's for every application, kept as they were for Deshacer.
        void Choose(bool keyboard, Rgb chosen)
        {
            if (app is not null && appColor is not null)
            {
                AppThemes.Set(context, app, (keyboard ? appColor with { KeyColor = chosen.Text } : appColor with { AccentColor = chosen.Text })
                    with { Look = AppLook.Custom, Before = new(appColor.KeyColor, appColor.AccentColor) });
            }
            else
            {
                var before = global.GetString(KeyPlusPreferences.KeyColorKey, "") + "|" + global.GetString(KeyPlusPreferences.AccentColorKey, "");
                global.Edit()!
                    .PutString(keyboard ? KeyPlusPreferences.KeyColorKey : KeyPlusPreferences.AccentColorKey, chosen.Text)!
                    .PutString(KeyPlusPreferences.ColorsBeforeKey, before)!
                    .Apply();
            }

            changed();
        }

        // On with Personalizado, or, Por aplicación, for an application chosen that does not take its theme's.
        var colorsOn = custom && (!perApp || appColor is { Look: not AppLook.Own });
        colorsLine.AddView(ColorButton(Resource.String.look_keyboard, showing.Ground, colorsOn, chosen => Choose(keyboard: true, chosen)));
        colorsLine.AddView(ColorButton(Resource.String.look_accent, showing.Primary, colorsOn, chosen => Choose(keyboard: false, chosen)));

        // Deshacer, beside the two colours it takes back: those shown as they were before the last one was chosen, one step back.
        var temaBefore = global.GetString(KeyPlusPreferences.ColorsBeforeKey, null);
        var undo = Pill(context.GetString(Resource.String.undo)!, colorsOn && (appColor is not null ? appColor.Before is not null : temaBefore is not null));
        undo.Click += (_, _) =>
        {
            if (app is not null && appColor is { Before: { } appBefore })
            {
                AppThemes.Set(context, app, appColor with { KeyColor = appBefore.KeyColor, AccentColor = appBefore.AccentColor, Before = null });
            }
            else if (app is null && temaBefore is not null)
            {
                var (key, accent) = temaBefore.Split('|') is [var k, var a] ? (k, a) : ("", "");
                global.Edit()!
                    .PutString(KeyPlusPreferences.KeyColorKey, key.Length > 0 ? key : null)!
                    .PutString(KeyPlusPreferences.AccentColorKey, accent.Length > 0 ? accent : null)!
                    .Remove(KeyPlusPreferences.ColorsBeforeKey)!
                    .Apply();
            }

            changed();
        };
        colorsLine.AddView(undo);

        // Aplicación and the applications typed in.
        appLine.AddView(appWords);
        var known = AppThemes.All(context).Keys.ToList();
        apps = Choices(known.Select(package => AppThemes.Name(context, package)).ToList(), app is null ? -1 : known.IndexOf(app), picked =>
        {
            // Only a choice made: a list turned off, or just filled, tells its first one too.
            if (perApp && known[picked] != app)
            {
                lookedAtApp = known[picked];
                changed();
            }
        });
        ((LinearLayout.LayoutParams)apps.LayoutParameters!).LeftMargin = 0;
        apps.LayoutParameters.Width = widths[looks];
        Turned(apps, perApp);
        appLine.AddView(apps);

        // De la app: the application chosen takes its theme's background; left, its own colours start exactly as it showed.
        appLine.AddView(Check(Resource.String.app_own, appColor?.Look == AppLook.Own, appColor is not null, isOwn =>
        {
            if (app is not null && appColor is not null)
            {
                AppThemes.Set(context, app, isOwn
                    ? appColor with { Look = AppLook.Own }
                    : appColor with { Look = AppLook.Custom, KeyColor = appColor.WindowColor ?? appColor.KeyColor, AccentColor = null });
                changed();
            }
        }));
    }

    /// <summary>A part on or off where it does not apply, faded then so it reads as off; never hidden.</summary>
    public static void Turned(View part, bool on)
    {
        part.Enabled = on;
        part.Alpha = on ? 1 : OffAlpha;
    }

    /// <summary>One of the lines, its parts in a row and centred on it, a little below the one above.</summary>
    private LinearLayout Line()
    {
        var line = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
        line.SetGravity(GravityFlags.CenterVertical);
        lines.AddView(line, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { TopMargin = lines.ChildCount > 0 ? Dp(GapDp) : 0 });
        return line;
    }

    /// <summary>The application looked at, Por aplicación (Personalizado with Por app on): the one chosen, or the first typed in; none, Tema's colours.</summary>
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
        var available = MeasureSpec.GetSize(widthMeasureSpec);

        // The applications' list as wide as the choices' above it, whatever the application's name.
        if (widths.TryGetValue(looks, out var choicesWidth))
        {
            apps.LayoutParameters!.Width = choicesWidth;
        }

        lines.Measure(unspecified, unspecified);
        scale = Math.Min(1f, available / (float)Math.Max(1, lines.MeasuredWidth));
        SetMeasuredDimension(available, (int)Math.Ceiling(lines.MeasuredHeight * scale));
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        lines.Layout(0, 0, lines.MeasuredWidth, lines.MeasuredHeight);
        lines.PivotX = lines.PivotY = 0;
        lines.ScaleX = lines.ScaleY = scale;
    }

    private TextView Words(int text) => SmallWords.Label(Context!, Context!.GetString(text)!);

    private CheckBox Check(int text, bool on, bool enabled, Action<bool> turned)
    {
        var check = new CheckBox(Context) { Text = Context!.GetString(text), Checked = on };
        Turned(check, enabled);
        check.SetTextSize(ComplexUnitType.Sp, TextSp);
        check.Click += (_, _) => turned(check.Checked);
        check.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(GapDp) };
        return check;
    }

    /// <summary>A list in small type, as wide as its longest choice whatever it shows, so a choice made moves nothing; that width kept (<see cref="widths"/>), for the applications' list to take.</summary>
    private Spinner Choices(IReadOnlyList<string> words, int chosen, Action<int> picked)
    {
        var list = SmallWords.List(Context!, words, chosen, picked);
        widths[list] = list.LayoutParameters!.Width;
        return list;
    }

    /// <summary>A pill with a sign or a few words, a finger's room around them, off and faded where it does not apply.</summary>
    private TextView Pill(string text, bool enabled)
    {
        var pill = new TextView(Context) { Text = text, Gravity = GravityFlags.Center, Clickable = true };
        Turned(pill, enabled);
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
    private TextView ColorButton(int name, Color shown, bool enabled, Action<Rgb> chosen)
    {
        var button = Pill("■ " + Context!.GetString(name), enabled);
        var words = new Android.Text.SpannableString(button.Text);
        words.SetSpan(new Android.Text.Style.ForegroundColorSpan(shown), 0, 1, Android.Text.SpanTypes.ExclusiveExclusive);
        button.TextFormatted = words;
        button.Click += (_, _) => ColorPicker.Show(Context!, new Rgb(shown.R, shown.G, shown.B), chosen);
        return button;
    }

    private int Dp(float value) => (int)((value * Resources!.DisplayMetrics!.Density) + 0.5f);
}

/// <summary>
/// A list's words in Tema's small type (<see cref="ColorsRow.TextSp"/>), the shown one and those dropped down alike; the
/// shown one on one line, cut short with … where its list is narrower, those dropped down whole. Its list and the label
/// before it (<see cref="List"/>, <see cref="Label"/>) are the settings' one list, Tema's and a language's context
/// model's alike.
/// </summary>
public sealed class SmallWords(Context context, IReadOnlyList<string> words, float textSp = ColorsRow.TextSp)
    : ArrayAdapter<string>(context, Android.Resource.Layout.SimpleSpinnerItem, words.ToList())
{
    private const float GapDp = 4;

    /// <summary>A language's context models' list, twice the small type: its names are read and told apart, not glanced at.</summary>
    public const float ModelsSp = ColorsRow.TextSp * 2;

    /// <summary>
    /// A list in small type, as wide as its longest choice whatever it shows, so a choice made moves nothing. A choice
    /// tells <paramref name="picked"/>; so does a list as it is first laid out, of the one it shows.
    /// </summary>
    public static Spinner List(Context context, IReadOnlyList<string> words, int chosen, Action<int> picked, float textSp = ColorsRow.TextSp)
    {
        var list = new Spinner(context) { Adapter = new SmallWords(context, words, textSp) };
        if (chosen >= 0)
        {
            list.SetSelection(chosen, false);
        }

        list.ItemSelected += (_, e) => picked(e.Position);

        // Its width at once, before it is drawn: measured with no limit, a list is as wide as its longest choice, its frame
        // and arrow too. Set after it was first drawn, the applications' list showed at its own width for half a second
        // each time the page was made again (De la app turned).
        var unspecified = View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified);
        list.Measure(unspecified, unspecified);
        list.LayoutParameters = new LinearLayout.LayoutParams(list.MeasuredWidth, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(context, GapDp) };
        return list;
    }

    /// <summary>The words before a list, in its small type.</summary>
    public static TextView Label(Context context, string text, float textSp = ColorsRow.TextSp)
    {
        var label = new TextView(context) { Text = text };
        label.SetTextSize(ComplexUnitType.Sp, textSp);
        label.SetPadding(0, 0, Dp(context, GapDp), 0);
        return label;
    }

    private static int Dp(Context context, float value) => (int)((value * context.Resources!.DisplayMetrics!.Density) + 0.5f);

    public override View GetView(int position, View? convertView, ViewGroup parent)
    {
        var shown = Small(base.GetView(position, convertView, parent));
        if (shown is TextView line)
        {
            line.SetSingleLine(true);
            line.Ellipsize = TextUtils.TruncateAt.End;
        }

        return shown;
    }

    public override View? GetDropDownView(int position, View? convertView, ViewGroup? parent) => Small(base.GetDropDownView(position, convertView, parent));

    private View Small(View? view)
    {
        (view as TextView)?.SetTextSize(ComplexUnitType.Sp, textSp);
        return view!;
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

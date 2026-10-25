using Android.Content;
using Android.Content.Res;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Tema, as the Windows app's, by language (a PC's keys differ between
/// them), for the screen the settings are on, its parts each of one size
/// whatever they hold, so nothing on the page moves: the language looked at
/// (the one typed in at first, left as it is); the keyboard the screen shows
/// in it, chosen from that language's list (as ⊞'s switch changes it); a
/// name with Guardar como, which copies the keyboard chosen under it (to
/// duplicate it, or design a copy), and Borrar, for one saved; and the
/// keyboard drawn as Android draws it, docked or floating as the screen
/// shows it, at its own size (Gboard's keys at the screen's keys' size,
/// whatever room the page has) in a card of one size (Estándar PC's), zoomed
/// whole to the page, its proportions kept, only to be seen, with Diseñar in
/// its bar's place, which designs it there with the screen to itself, one to
/// one where it fits, Hecho changing the keyboard on screen at once.
/// </summary>
public sealed class ThemeView : LinearLayout
{
    private const float FieldGapDp = 8;
    private const float CompactSp = ColorsRow.TextSp;
    private const float HintSp = 12;

    private readonly SettingsScreen settings;
    private readonly ISharedPreferences screen;
    private readonly KeyPlusPreferences.Screen kind;

    /// <summary>The keyboard as the screen shows it: floating or docked, and its keys' size.</summary>
    private readonly bool floating;
    private readonly float keysScale;

    private readonly StandardColors colors;
    private readonly Spinner languages;
    private readonly Spinner keyboards;
    private readonly EditText name;
    private readonly Button delete;
    private readonly LinearLayout controls;
    private readonly LinearLayout card;
    private readonly TextView design;
    private readonly LinearLayout designBar;
    private readonly StandardKeyboard preview;
    private readonly ScrollView extras;
    private readonly StandardDesigner designer;

    /// <summary>Kept as a field: Android holds its listeners weakly.</summary>
    private readonly PreferenceWatch appsWatch;
    private (int Width, int Height) room = keptRoom;

    /// <summary>
    /// The preview's room as last told, kept for the page made again (a colour chosen remakes it): its card zoomed to it
    /// before it is first drawn, where it was drawn whole first and made smaller after, the keyboard shrinking on screen.
    /// </summary>
    private static (int Width, int Height) keptRoom;

    private StandardLanguage language;
    private IReadOnlyList<KeyboardEntry> listed = [];
    private KeyboardEntry shown;
    private bool filling;

    /// <param name="remade">Tema made again, its colours changed (Colores).</param>
    public ThemeView(Context context, SettingsScreen settings, KeyPlusPreferences.Screen kind, Action remade)
        : base(context)
    {
        this.settings = settings;
        this.kind = kind;
        screen = KeyPlusPreferences.ForScreen(context, kind);
        language = KeyPlusPreferences.Language(context);
        shown = Keyboards.Selected(context, screen, kind, language.Code);
        var placed = new PanelPlacement(screen)
        {
            Mode = PanelPlacement.StandardMode,
            Pc = shown.Base == Keyboards.StandardPcBase,
            Landscape = context.Resources!.Configuration!.Orientation == Android.Content.Res.Orientation.Landscape,
        };
        (floating, keysScale) = (placed.Floating, placed.Scale);
        Orientation = Orientation.Vertical;
        // The preview in the colours of the application looked at, with Personalizado's Por app on; else Tema's.
        colors = StandardColors.Read(context, ColorsRow.AppLookedAt(context));

        // The language, the keyboard and its name in two rows of small type, each label beside its control, the room left to the keyboard drawn under them.
        languages = new Spinner(context);
        languages.Adapter = Choices(StandardKeys.Languages.Select(known => known.Name));
        languages.SetSelection(StandardKeys.Languages.ToList().IndexOf(language));
        keyboards = new Spinner(context);
        var choosing = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        choosing.SetGravity(GravityFlags.CenterVertical);
        choosing.AddView(Caption(Resource.String.languages));
        choosing.AddView(languages, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = Dp(FieldGapDp) });
        choosing.AddView(Caption(Resource.String.keyboard));
        choosing.AddView(keyboards, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        name = new EditText(context) { InputType = Android.Text.InputTypes.ClassText };
        name.SetSingleLine(true);
        name.SetTextSize(ComplexUnitType.Sp, CompactSp);
        var saveAs = Button(Resource.String.save_as);
        delete = Button(Resource.String.delete);
        var naming = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        naming.SetGravity(GravityFlags.CenterVertical);
        naming.AddView(name, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        naming.AddView(saveAs, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(FieldGapDp / 2) });
        naming.AddView(delete, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(FieldGapDp / 2) });

        var hint = new TextView(context) { Text = context.GetString(Resource.String.keyboards_saved_summary), Alpha = 0.7f };
        hint.SetTextSize(ComplexUnitType.Sp, HintSp);
        // Two cards, each its part, as the Windows settings' (the owner's, 2026-10-25): the keyboard, its name and its
        // summary; the colours.
        var choosingKeyboard = new LinearLayout(context) { Orientation = Orientation.Vertical };
        choosingKeyboard.AddView(choosing);
        choosingKeyboard.AddView(naming);
        choosingKeyboard.AddView(hint, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        controls = new LinearLayout(context) { Orientation = Orientation.Vertical };
        controls.AddView(settings.Card(choosingKeyboard), new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(FieldGapDp / 2) });
        controls.AddView(settings.Card(new ColorsRow(context, remade)), new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(FieldGapDp) });
        AddView(controls);

        // The card as the keyboard shows it: its bar, Diseñar in its place, then the keys, + Tecla's list over them while chosen.
        preview = new StandardKeyboard(context, colors) { Types = false };
        designer = new StandardDesigner(context, colors, preview);
        design = new TextView(context) { Text = context.GetString(Resource.String.design), Gravity = GravityFlags.Center, Clickable = true };
        var pill = new GradientDrawable();
        pill.SetColor(colors.Function);
        pill.SetCornerRadius(Dp(KeyPanel.ToolCircleDp) / 2f);
        design.Background = new RippleDrawable(ColorStateList.ValueOf(KeyPanel.RippleColor), pill, null);
        design.SetTextColor(colors.FunctionGlyph);
        design.SetTextSize(ComplexUnitType.Sp, 14);
        design.SetPadding(Dp(16), 0, Dp(16), 0);
        designBar = new LinearLayout(context) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        var bar = new FrameLayout(context);
        bar.AddView(design, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(KeyPanel.ToolCircleDp), GravityFlags.Left | GravityFlags.CenterVertical) { LeftMargin = Dp(4) });
        bar.AddView(designBar, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        extras = new ScrollView(context) { Visibility = ViewStates.Gone, VerticalScrollBarEnabled = false };
        extras.SetBackgroundColor(colors.Ground);
        var keysPlace = new FrameLayout(context);
        keysPlace.AddView(preview, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.Left));
        keysPlace.AddView(extras, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        card = new LinearLayout(context) { Orientation = Orientation.Vertical };
        card.SetBackgroundColor(colors.Ground);
        card.AddView(bar, new LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(KeyPanel.ToolbarDp)));
        card.AddView(keysPlace, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        var box = new Box(context, (width, height) =>
        {
            room = keptRoom = (width, height);
            Post(Fit);
        });
        card.PivotX = card.PivotY = 0;
        box.AddView(card, new FrameLayout.LayoutParams((int)CardWidth, (int)CardHeight, GravityFlags.Top | GravityFlags.Left));
        AddView(box, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        languages.ItemSelected += (_, picked) =>
        {
            if (StandardKeys.Languages[picked.Position] != language)
            {
                language = StandardKeys.Languages[picked.Position];
                Fill();
            }
        };
        keyboards.ItemSelected += (_, picked) =>
        {
            if (!filling && listed[picked.Position].Id != shown.Id)
            {
                Keyboards.Select(screen, language.Code, listed[picked.Position].Id);
                Show(listed[picked.Position]);
            }
        };
        saveAs.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                return;
            }

            // A copy under the name written, chosen to be designed; the keyboard it was made from stays as it is.
            var copy = Keyboards.SaveAs(context, name.Text.Trim(), shown.Base, shown.Design, language.Code, shown.FloatingDesign);
            Keyboards.Select(screen, language.Code, copy.Id);
            Fill();
        };
        delete.Click += (_, _) => new AlertDialog.Builder(context)
            .SetMessage(context.GetString(Resource.String.delete_ask, new Java.Lang.String(shown.Name)))!
            .SetPositiveButton(Resource.String.delete, (_, _) =>
            {
                Keyboards.Delete(context, shown.Id);
                Fill();
            })!
            .SetNegativeButton(Resource.String.cancel, (_, _) => { })!
            .Show();
        design.Click += (_, _) => StartDesign();

        // The applications' colours noted by the keyboard (one typed in, its colour seen): the page made again in them, not
        // while designing.
        appsWatch = new PreferenceWatch((_, key) =>
        {
            if (AppThemes.IsList(key) && controls.Visibility == ViewStates.Visible)
            {
                Post(remade);
            }
        });
        KeyPlusPreferences.Global(context).RegisterOnSharedPreferenceChangeListener(appsWatch);
        preview.DesignChanged = Fit;
        Fill();
    }

    /// <summary>The language's keyboards in the list, the one the screen shows in it chosen.</summary>
    private void Fill()
    {
        filling = true;
        listed = Keyboards.All(Context!, language.Code);
        var chosen = Keyboards.Selected(Context!, screen, kind, language.Code);
        keyboards.Adapter = Choices(listed.Select(keyboard => keyboard.Name));
        keyboards.SetSelection(listed.ToList().FindIndex(keyboard => keyboard.Id == chosen.Id));
        Post(() => filling = false);
        Show(chosen);
    }

    /// <summary>The keyboard chosen as the screen shows it: its keys, its design for the mode and the language looked at; its name to copy it under, Borrar for one saved, hidden in its place for one built in.</summary>
    private void Show(KeyboardEntry keyboard)
    {
        shown = keyboard;
        name.Text = keyboard.Name;
        delete.Visibility = keyboard.BuiltIn ? ViewStates.Invisible : ViewStates.Visible;
        var drawn = keyboard.Standard ? ViewStates.Visible : ViewStates.Invisible;
        preview.Visibility = design.Visibility = drawn;
        preview.Language = language;
        preview.Pc = keyboard.Base == Keyboards.StandardPcBase;
        preview.Xr = keyboard.Base == Keyboards.StandardXrBase;
        preview.Design = StandardDesign.FromText(keyboard.DesignFor(docked: !floating));
        Fit();
    }

    /// <summary>The card whatever the keyboard chosen: Estándar PC at its own size, the bar over its rows, at the keys' size, in pixels.</summary>
    private float CardWidth => StandardKeyboard.WidthDp(StandardKeyboard.Across(StandardPage.Pc, StandardKeys.Spanish)) * Density * keysScale;

    private float CardHeight =>
        Dp(KeyPanel.ToolbarDp) + (StandardKeyboard.HeightDp(StandardKeys.Page(StandardPage.Pc, StandardKeys.Spanish).Count) * Density * keysScale);

    private float Density => Resources!.DisplayMetrics!.Density;

    /// <summary>
    /// The card zoomed whole to the room, never larger than it is; in it the
    /// keyboard chosen at its own size at its top left, a narrower one (Gboard's
    /// ten keys) leaving room beside it, one with more rows (a design's) made
    /// smaller to fit.
    /// </summary>
    private void Fit()
    {
        var rows = preview.Design?.Rows.Count ?? StandardKeys.Page(preview.BasePage, language).Count;
        var keysRoom = CardHeight - Dp(KeyPanel.ToolbarDp);
        preview.Scale = Math.Min(keysScale, keysRoom / (StandardKeyboard.HeightDp(Math.Max(1, rows), preview.Xr) * Density));
        var width = (int)(StandardKeyboard.WidthDp(StandardKeyboard.Across(preview.BasePage, language)) * Density * preview.Scale);
        if (preview.LayoutParameters is { } place && place.Width != width)
        {
            place.Width = width;
            preview.RequestLayout();
        }

        if (room.Width > 0 && room.Height > 0)
        {
            card.ScaleX = card.ScaleY = Math.Min(1f, Math.Min(room.Width / CardWidth, room.Height / CardHeight));
        }
    }

    /// <summary>Diseñar: the keyboard alone on the screen, the designer's pills in its bar's place.</summary>
    private void StartDesign()
    {
        controls.Visibility = ViewStates.Gone;
        design.Visibility = ViewStates.Invisible;
        designBar.RemoveAllViews();
        designer.FillBar(designBar, ToggleExtras, EndDesign);
        designBar.Visibility = ViewStates.Visible;
        designer.Start(shown, floating);
        settings.Alone(true);
    }

    /// <summary>Hecho or Cancelar: the page as it was, the keyboard as it now is chosen and drawn, the keyboard on screen changed with it.</summary>
    private void EndDesign(bool keep)
    {
        extras.Visibility = ViewStates.Gone;
        designBar.Visibility = ViewStates.Gone;
        designBar.RemoveAllViews();
        designer.End(keep, screen);
        controls.Visibility = ViewStates.Visible;
        settings.Alone(false);
        Fill();
    }

    /// <summary>+ Tecla: its list over the keys, or the keys again.</summary>
    private void ToggleExtras()
    {
        if (extras.Visibility == ViewStates.Visible)
        {
            extras.Visibility = ViewStates.Gone;
            return;
        }

        extras.RemoveAllViews();
        extras.AddView(designer.ExtraKeys(() => extras.Visibility = ViewStates.Gone));
        extras.ScrollTo(0, 0);
        extras.Visibility = ViewStates.Visible;
    }

    private TextView Caption(int text)
    {
        var caption = new TextView(Context) { Text = Context!.GetString(text) };
        caption.SetTextSize(ComplexUnitType.Sp, CompactSp);
        caption.SetPadding(0, 0, Dp(FieldGapDp / 2), 0);
        return caption;
    }

    private Button Button(int text)
    {
        var button = new Button(Context) { Text = Context!.GetString(text), Gravity = GravityFlags.Center };
        button.SetTextSize(ComplexUnitType.Sp, CompactSp);
        button.SetAllCaps(false);
        return button;
    }

    /// <summary>A list's words as large as its label beside it (<see cref="Caption"/>), the user's: they stood twice as large.</summary>
    private SmallWords Choices(IEnumerable<string> names)
    {
        var adapter = new SmallWords(Context!, names.ToList());
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        return adapter;
    }

    private int Dp(float value) => (int)((value * Resources!.DisplayMetrics!.Density) + 0.5f);

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        KeyPlusPreferences.Global(Context!).UnregisterOnSharedPreferenceChangeListener(appsWatch);
    }

    /// <summary>A preference changed, told.</summary>
    private sealed class PreferenceWatch(Action<ISharedPreferences?, string?> changed) : Java.Lang.Object, ISharedPreferencesOnSharedPreferenceChangeListener
    {
        public void OnSharedPreferenceChanged(ISharedPreferences? preferences, string? key) => changed(preferences, key);
    }

    /// <summary>The preview's room, told each time its size changes (the page's, or the screen to itself while designing).</summary>
    private sealed class Box(Context context, Action<int, int> sized) : FrameLayout(context)
    {
        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            sized(w, h);
        }
    }
}

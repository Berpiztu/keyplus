using Android.Content;
using Android.Content.Res;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Diseñar on Android, the keyboard's and Tema's preview's alike, as the
/// Windows app's KeyboardDesigner: over a <see cref="StandardKeyboard"/>, the
/// designer's pills (−, +, + Tecla, + Fila, + Columna, Quitar, Cancelar,
/// Hecho), put in a bar its host gives, and + Tecla's list, which its host
/// shows in the keys' place. Started with a keyboard of the list, from its
/// design for the mode it shows in or the keyboard as made; Hecho saves a
/// built-in one as a new keyboard of the language designed in, chosen for
/// the screen at once, and a saved one in its place, floating as its
/// floating design; Cancelar leaves it as it was.
/// </summary>
public sealed class StandardDesigner(Context context, StandardColors colors, StandardKeyboard keys)
{
    /// <summary>+ Tecla's list: five keys across, 40 dp tall.</summary>
    private const int ExtrasAcross = 5;
    private const float ExtraKeyDp = 40;

    private KeyboardEntry? keyboard;
    private bool floating;

    /// <summary>A keyboard of the list designed as it shows docked or floating, from that mode's design or the keyboard as made.</summary>
    public void Start(KeyboardEntry shown, bool floating)
    {
        keyboard = shown;
        this.floating = floating;
        var made = StandardKeys.Page(keys.Pc ? StandardPage.Pc : StandardPage.Letters, keys.Language);
        keys.Design = StandardDesign.FromText(shown.DesignFor(docked: !floating)) ?? StandardDesign.From(made);
        keys.Designing = true;
    }

    /// <summary>
    /// Hecho (<paramref name="keep"/>): the design kept, a keyboard the
    /// keyboard comes with saved as a new one, which the screen then shows
    /// (renamed in Tema), a saved one keeping it in its place; Cancelar: as
    /// it was. The keyboard as it now is, that designed again opens as saved.
    /// </summary>
    public KeyboardEntry? End(bool keep, ISharedPreferences screen)
    {
        if (keyboard is not { } shown)
        {
            return null;
        }

        keyboard = null;
        keys.Designing = false;
        if (keep && keys.Design is { } designed)
        {
            var language = keys.Language.Code;
            if (shown.BuiltIn)
            {
                // Floating, the design is the keyboard's floating one: a new keyboard made from a built-in one docks as that one does.
                var name = Keyboards.NewName(context, shown);
                shown = floating
                    ? Keyboards.SaveAs(context, name, shown.Base, shown.Design, language, designed.Text)
                    : Keyboards.SaveAs(context, name, shown.Base, designed.Text, language);
                Keyboards.Select(screen, language, shown.Id);
            }
            else
            {
                Keyboards.SetDesign(context, shown.Id, designed.Text, floating);
                shown = floating ? shown with { FloatingDesign = designed.Text } : shown with { Design = designed.Text };
            }
        }
        else
        {
            keys.Design = StandardDesign.FromText(shown.DesignFor(docked: !floating));
        }

        return shown;
    }

    /// <summary>The pills in a bar, Gboard's: −, +, + Tecla, + Fila, + Columna, Quitar, Cancelar, Hecho in blue; a sign's pill narrower than a word's.</summary>
    public void FillBar(LinearLayout bar, Action addKey, Action<bool> end)
    {
        (string Label, Action Pressed, bool Done)[] buttons =
        [
            ("−", () => keys.ResizeMarked(wider: false), false),
            ("+", () => keys.ResizeMarked(wider: true), false),
            (context.GetString(Resource.String.add_key), addKey, false),
            (context.GetString(Resource.String.add_row), keys.AddRow, false),
            (context.GetString(Resource.String.add_column), keys.AddColumn, false),
            (context.GetString(Resource.String.remove), keys.RemoveMarked, false),
            (context.GetString(Resource.String.cancel), () => end(false), false),
            (context.GetString(Resource.String.done), () => end(true), true),
        ];
        foreach (var (label, pressed, done) in buttons)
        {
            var ground = new GradientDrawable();
            ground.SetColor(done ? colors.Primary : colors.Function);
            ground.SetCornerRadius(Dp(KeyPanel.ToolCircleDp) / 2f);
            var button = new TextView(context)
            {
                Text = label,
                Gravity = GravityFlags.Center,
                Background = new RippleDrawable(ColorStateList.ValueOf(KeyPanel.RippleColor), ground, null),
                Clickable = true,
            };
            button.SetTextColor(done ? colors.OnPrimary : colors.FunctionGlyph);
            button.SetSingleLine(true);
            button.SetAutoSizeTextTypeUniformWithConfiguration(8, 16, 1, (int)ComplexUnitType.Sp);
            button.Click += (_, _) => pressed();
            var place = new FrameLayout(context);
            place.AddView(button, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(KeyPanel.ToolCircleDp), GravityFlags.Center) { LeftMargin = Dp(2), RightMargin = Dp(2) });
            bar.AddView(place, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, label.Length <= 1 ? 0.7f : 1f));
        }
    }

    /// <summary>+ Tecla: the keys a design can add and the keyboard lacks, by group; one tapped, the list put away (<paramref name="chosen"/>), it comes loose over the keys, to be dragged to its place.</summary>
    public View ExtraKeys(Action chosen)
    {
        var list = new LinearLayout(context) { Orientation = Orientation.Vertical };
        list.SetPadding(Dp(12), Dp(4), Dp(12), Dp(8));
        foreach (var (name, offered) in StandardKeys.Extras)
        {
            var ids = offered.Where(id => !keys.HasKey(id)).ToArray();
            if (ids.Length == 0)
            {
                continue;
            }

            var header = new TextView(context) { Text = context.GetString(name) };
            header.SetTextColor(colors.Outline);
            header.SetTextSize(ComplexUnitType.Sp, 13);
            header.SetPadding(Dp(4), Dp(8), 0, Dp(4));
            list.AddView(header);
            for (var first = 0; first < ids.Length; first += ExtrasAcross)
            {
                var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
                for (var i = first; i < first + ExtrasAcross; i++)
                {
                    if (i >= ids.Length || StandardKeys.Extra(ids[i], keys.Language) is not { } key)
                    {
                        row.AddView(new View(context), new LinearLayout.LayoutParams(0, Dp(ExtraKeyDp), 1f));
                        continue;
                    }

                    var id = ids[i];
                    var ground = new GradientDrawable();
                    ground.SetColor(colors.Function);
                    ground.SetCornerRadius(Dp(8));
                    var button = new TextView(context) { Text = key.Label, Gravity = GravityFlags.Center, Background = new RippleDrawable(ColorStateList.ValueOf(KeyPanel.RippleColor), ground, null), Clickable = true };
                    button.SetTextColor(colors.FunctionGlyph);
                    button.SetSingleLine(true);
                    button.SetAutoSizeTextTypeUniformWithConfiguration(8, 15, 1, (int)ComplexUnitType.Sp);
                    button.Click += (_, _) =>
                    {
                        chosen();
                        keys.AddKey(id);
                    };
                    row.AddView(button, new LinearLayout.LayoutParams(0, Dp(ExtraKeyDp), 1f) { LeftMargin = Dp(2), RightMargin = Dp(2), BottomMargin = Dp(4) });
                }

                list.AddView(row);
            }
        }

        return list;
    }

    private int Dp(float value) => (int)((value * context.Resources!.DisplayMetrics!.Density) + 0.5f);
}

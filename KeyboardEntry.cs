namespace Berpiztu.KeyPlus;

/// <summary>
/// A keyboard of the list (Ajustes › Tema): its id, its name, what it is made
/// from (Estándar, Gboard's keys; Estándar PC, a PC's keys in Gboard's look;
/// or PC, the whole keyboard of the designs made before the list), and its
/// design, which goes with it; none, as its base is; and, on Windows, the
/// language it was designed in (a PC's keys differ between them), each
/// language listing its own: none, made before there were languages, is
/// Spanish's. Shared with the Windows app, whose <see cref="Keyboards"/>
/// keeps its own list.
/// </summary>
public sealed record KeyboardEntry(string Id, string Name, string Base, string? Design = null, string? Language = null)
{
    /// <summary>The language a saved keyboard belongs to: its own, or Spanish for one made before there were languages.</summary>
    public string LanguageCode => Language ?? StandardKeys.Spanish.Code;

    /// <summary>Whether it is one the keyboard comes with (Estándar, Estándar PC): never changed, never deleted.</summary>
    public bool BuiltIn => Id is Keyboards.StandardId or Keyboards.StandardPcId;

    /// <summary>Whether it is drawn as Gboard draws its keys, in the device's colours.</summary>
    public bool Standard => Base is Keyboards.StandardBase or Keyboards.StandardPcBase;
}

// The Android names the files shared with the Android keyboard use
// (StandardKeys.cs, EmojiList.cs), so the same files compile here: a key's
// code, with Android's own values, and the ids of signs and texts, each
// named as its Android resource (Resources/drawable/<name>.xml,
// Resources/values*/strings.xml), which Icons and Texts read by that name.

namespace Android.Views
{
    /// <summary>Android's key codes that the layouts name, with Android's values.</summary>
    public enum Keycode
    {
        Unknown = 0,
        Num0 = 7,
        DpadUp = 19,
        DpadDown = 20,
        DpadLeft = 21,
        DpadRight = 22,
        A = 29,
        Comma = 55,
        Period = 56,
        Tab = 61,
        Space = 62,
        Enter = 66,
        Del = 67,
        Minus = 69,
        Equals = 70,
        Slash = 76,
        Plus = 81,
        PageUp = 92,
        PageDown = 93,
        Escape = 111,
        ForwardDel = 112,
        Sysrq = 120,
        MoveHome = 122,
        MoveEnd = 123,
        Insert = 124,
        F1 = 131,
    }
}

namespace Berpiztu.KeyPlus
{
    /// <summary>The ids of the Android resources the shared files and the Windows app name, each field named as its resource.</summary>
    public static class Resource
    {
        public static class Drawable
        {
            public const int ic_key_shift = 1;
            public const int ic_key_shift_on = 2;
            public const int ic_key_shift_lock = 3;
            public const int ic_key_backspace = 4;
            public const int ic_key_language = 5;
            public const int ic_key_return = 6;
            public const int ic_key_emoji = 7;
            public const int ic_grid_pc = 8;
            public const int ic_grid_phone = 9;
            public const int ic_tool_close = 10;
            public const int ic_tool_back = 11;
            public const int ic_emoji_recent = 12;
            public const int ic_emoji_people = 13;
            public const int ic_emoji_nature = 14;
            public const int ic_emoji_food = 15;
            public const int ic_emoji_travel = 16;
            public const int ic_emoji_activities = 17;
            public const int ic_emoji_objects = 18;
            public const int ic_emoji_symbols = 19;
            public const int ic_emoji_flags = 20;
            public const int ic_tool_mic = 21;
        }

        public static class String
        {
            public const int design_editing = 1;
            public const int design_modifiers = 2;
            public const int design_functions = 3;
            public const int design_arrows = 4;
            public const int emoji_recent = 5;
            public const int emoji_smileys = 6;
            public const int emoji_people = 7;
            public const int emoji_nature = 8;
            public const int emoji_food = 9;
            public const int emoji_travel = 10;
            public const int emoji_activities = 11;
            public const int emoji_objects = 12;
            public const int emoji_symbols = 13;
            public const int emoji_flags = 14;
            public const int not_made_yet = 15;
            public const int voice_listening = 16;
            public const int voice_transcribing = 17;
            public const int voice_unavailable = 18;
            public const int voice_no_microphone = 19;
        }

        /// <summary>A resource's name by its id, as its field is named.</summary>
        public static string NameOf(Type kind, int id) =>
            kind.GetFields().First(field => field.IsLiteral && (int)field.GetRawConstantValue()! == id).Name;
    }
}

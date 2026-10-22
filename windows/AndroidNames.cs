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
            public const int ic_tool_settings = 22;
            public const int ic_keyplus = 23;
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
            public const int voice_nothing_heard = 20;
            public const int voice_microphone = 22;
            public const int voice_microphone_default = 23;
            public const int voice_silence = 24;
            public const int voice_silence_hint = 25;
            public const int voice_gain = 26;
            public const int voice_gain_hint = 27;
            public const int voice_service = 28;
            public const int voice_service_hint = 29;
            public const int voice_local = 32;
            public const int voice_address = 33;
            public const int voice_user = 34;
            public const int voice_password = 35;
            public const int voice_sign_in_refused = 36;
            public const int settings_title = 37;
            public const int preferences = 38;
            public const int voice_typing = 39;
            public const int avoid_caret = 40;
            public const int avoid_caret_summary = 41;
            public const int keyboard_settings = 42;
            public const int show_keyboard = 43;
            public const int show_keyboard_summary = 44;
            public const int keyboard_icon = 45;
            public const int keyboard_icon_summary = 46;
            public const int focus_lost = 47;
            public const int focus_lost_hide = 48;
            public const int focus_lost_icon = 49;
            public const int focus_lost_stay = 50;
            public const int open_keyboard = 51;
            public const int quit = 52;
            public const int avoid_menus = 53;
            public const int avoid_menus_summary = 54;
            public const int voice_auto_stop = 55;
            public const int voice_auto_stop_summary = 56;
            public const int voice_lan = 57;
            public const int voice_remote = 58;
            public const int voice_lan_hint = 59;
            public const int languages = 60;
            public const int theme = 61;
            public const int glide = 62;
            public const int glide_summary = 63;
            public const int glide_trail = 64;
            public const int glide_trail_summary = 65;
            public const int futo = 66;
            public const int about = 67;
            public const int version = 68;
            public const int licences = 69;
            public const int windows_licences_summary = 70;
            public const int keyboard_standard = 71;
            public const int keyboard_standard_pc = 72;
        }

        /// <summary>A resource's name by its id, as its field is named.</summary>
        public static string NameOf(Type kind, int id) =>
            kind.GetFields().First(field => field.IsLiteral && (int)field.GetRawConstantValue()! == id).Name;
    }
}

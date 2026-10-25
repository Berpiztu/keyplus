// The Android names the files shared with the Android keyboard use
// (StandardKeys.cs, EmojiList.cs, StandardDesign.cs), so the same files
// compile here: a key's code, with Android's own values, a warning to the
// log, and the ids of signs and texts, each
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

namespace Android.Util
{
    /// <summary>Android's log, as the shared files warn: to the keyboard's own.</summary>
    public static class Log
    {
        public static void Warn(string tag, string message) => Berpiztu.KeyPlus.Log.Write(message);
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
            public const int ic_grid_design = 24;
            public const int ic_grid_floating = 25;
            public const int ic_grid_floating_off = 26;
            public const int ic_tool_resize = 27;
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
            public const int avoid_menus_electron = 73;
            public const int troubleshooting = 74;
            public const int troubleshooting_vscode = 75;
            public const int troubleshooting_log = 76;
            public const int troubleshooting_log_summary = 77;
            public const int troubleshooting_vscode_button = 78;
            public const int troubleshooting_vscode_done = 79;
            public const int troubleshooting_vscode_already = 80;
            public const int troubleshooting_vscode_none = 81;
            public const int design = 82;
            public const int add_key = 83;
            public const int add_row = 84;
            public const int remove = 85;
            public const int cancel = 86;
            public const int done = 87;
            public const int saved_as = 88;
            public const int delete = 89;
            public const int windows_delete_ask = 91;
            public const int keyboards_saved_summary = 92;
            public const int add_column = 93;
            public const int save_as = 94;
            public const int keyboard = 95;
            public const int reset_size = 96;
            public const int look = 97;
            public const int look_system = 98;
            public const int look_light = 99;
            public const int look_dark = 100;
            public const int look_custom = 101;
            public const int look_keyboard = 102;
            public const int look_accent = 103;
            public const int undo = 104;
            public const int per_app = 105;
            public const int app_own = 107;
            public const int updates_automatic = 108;
            public const int updates_automatic_summary = 109;
            public const int updates_check = 110;
            public const int update_title = 111;
            public const int update_ask = 112;
            public const int update_yes = 113;
            public const int update_no = 114;
            public const int update_current = 115;
            public const int update_failed = 116;
            public const int dictionary = 128;
            public const int glide_delete_word = 117;
            public const int glide_delete_word_summary = 118;
            public const int dictionary_language = 119;
            public const int dictionary_words = 120;
            public const int dictionary_words_summary = 121;
            public const int dictionary_add = 122;
            public const int dictionary_learn_typed = 123;
            public const int dictionary_learn_typed_summary = 124;
            public const int dictionary_learn_chosen = 125;
            public const int dictionary_learn_chosen_summary = 126;
            public const int dictionary_forget_chosen = 127;
        }

        /// <summary>A resource's name by its id, as its field is named.</summary>
        public static string NameOf(Type kind, int id) =>
            kind.GetFields().First(field => field.IsLiteral && (int)field.GetRawConstantValue()! == id).Name;
    }
}

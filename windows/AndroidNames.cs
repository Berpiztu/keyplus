// The Android names the layouts shared with the Android keyboard use
// (StandardKeys.cs), so the same file compiles here: a key's code, with
// Android's own values, and the ids of its signs and of its groups' names.

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
    /// <summary>The ids the layouts give a key's sign and a group's name; <see cref="Icons"/> reads the signs by them.</summary>
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
        }

        public static class String
        {
            public const int design_editing = 1;
            public const int design_modifiers = 2;
            public const int design_functions = 3;
            public const int design_arrows = 4;
        }
    }
}

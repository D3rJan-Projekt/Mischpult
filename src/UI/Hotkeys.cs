using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Mischpult.Native;

namespace Mischpult.UI
{
    /// <summary>Systemweite Tastenkuerzel (funktionieren auch, wenn ein Spiel im Vordergrund ist).</summary>
    internal sealed class HotkeyManager
    {
        private readonly Dictionary<int, string> _ids = new Dictionary<int, string>();
        private readonly List<string> _failed = new List<string>();
        private IntPtr _hwnd;

        public IReadOnlyList<string> Failed => _failed;

        public static bool IsRepeatAction(string action) => action.EndsWith(".up") || action.EndsWith(":up") || action.EndsWith(".down") || action.EndsWith(":down");

        public void Apply(IntPtr hwnd, IEnumerable<HotkeyBinding> bindings)
        {
            UnregisterAll();
            _hwnd = hwnd;
            if (!Win32.IsWindows || hwnd == IntPtr.Zero) return;
            int id = 0x1100;
            foreach (var b in bindings)
            {
                var keys = (Keys)b.Keys;
                uint vk = (uint)(keys & Keys.KeyCode);
                if (vk == 0) continue;
                uint mods = 0;
                if ((keys & Keys.Control) != 0) mods |= Win32.MOD_CONTROL;
                if ((keys & Keys.Alt) != 0) mods |= Win32.MOD_ALT;
                if ((keys & Keys.Shift) != 0) mods |= Win32.MOD_SHIFT;
                if (!IsRepeatAction(b.Action)) mods |= Win32.MOD_NOREPEAT;
                id++;
                bool ok;
                try { ok = Win32.RegisterHotKey(hwnd, id, mods, vk); } catch { ok = false; }
                if (ok) _ids[id] = b.Action;
                else _failed.Add(b.Action);
            }
        }

        public void UnregisterAll()
        {
            if (_hwnd != IntPtr.Zero && Win32.IsWindows)
                foreach (var id in _ids.Keys) { try { Win32.UnregisterHotKey(_hwnd, id); } catch { } }
            _ids.Clear();
            _failed.Clear();
        }

        public string ActionFor(int id) => _ids.TryGetValue(id, out var a) ? a : null;

        public static string Format(int keyData)
        {
            if (keyData == 0) return "";
            var k = (Keys)keyData;
            var parts = new List<string>();
            if ((k & Keys.Control) != 0) parts.Add("Strg");
            if ((k & Keys.Alt) != 0) parts.Add("Alt");
            if ((k & Keys.Shift) != 0) parts.Add("Shift");
            var code = k & Keys.KeyCode;
            if (code != Keys.None && code != Keys.ControlKey && code != Keys.Menu && code != Keys.ShiftKey)
                parts.Add(KeyName(code));
            return string.Join("+", parts);
        }

        private static string KeyName(Keys code)
        {
            if (code >= Keys.A && code <= Keys.Z) return ((char)('A' + (code - Keys.A))).ToString();
            if (code >= Keys.D0 && code <= Keys.D9) return ((char)('0' + (code - Keys.D0))).ToString();
            if (code >= Keys.NumPad0 && code <= Keys.NumPad9) return "Num" + (code - Keys.NumPad0);
            switch (code)
            {
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.PageUp: return "Bild↑";
                case Keys.Next: return "Bild↓";
                case Keys.Home: return "Pos1";
                case Keys.End: return "Ende";
                case Keys.Insert: return "Einfg";
                case Keys.Delete: return "Entf";
                case Keys.Space: return "Leertaste";
                case Keys.Add: return "Num+";
                case Keys.Subtract: return "Num-";
                case Keys.Multiply: return "Num*";
                case Keys.Divide: return "Num/";
                case Keys.Oemplus: return "+";
                case Keys.OemMinus: return "-";
                case Keys.Pause: return "Pause";
                case Keys.Scroll: return "Rollen";
            }
            return code.ToString();
        }
    }

    /// <summary>Eingabefeld, das eine Tastenkombination aufnimmt.</summary>
    internal sealed class HotkeyBox : TextBox
    {
        private int _keys;
        public event Action Changed;

        public HotkeyBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            BackColor = Theme.Input;
            ForeColor = Theme.ValueText;
            BorderStyle = BorderStyle.FixedSingle;
            TextAlign = HorizontalAlignment.Center;
            Cursor = Cursors.Hand;
        }

        public int Keys
        {
            get => _keys;
            set { _keys = value; Text = value == 0 ? "—" : HotkeyManager.Format(value); }
        }

        protected override bool IsInputKey(System.Windows.Forms.Keys keyData) => true;

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            BackColor = Theme.InputFocus;
            BeginInvoke(new Action(() => Select(0, 0)));
        }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); BackColor = Theme.Input; Keys = _keys; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            var code = e.KeyCode;
            if (code == System.Windows.Forms.Keys.ControlKey || code == System.Windows.Forms.Keys.Menu || code == System.Windows.Forms.Keys.ShiftKey)
            {
                Text = HotkeyManager.Format((int)e.Modifiers) + "+…";
                return;
            }
            if (e.Modifiers == System.Windows.Forms.Keys.None &&
                (code == System.Windows.Forms.Keys.Back || code == System.Windows.Forms.Keys.Delete || code == System.Windows.Forms.Keys.Escape))
            {
                Keys = 0;
                Changed?.Invoke();
                return;
            }
            bool fKey = code >= System.Windows.Forms.Keys.F1 && code <= System.Windows.Forms.Keys.F24;
            bool special = code == System.Windows.Forms.Keys.Pause || code == System.Windows.Forms.Keys.Scroll;
            if (e.Modifiers == System.Windows.Forms.Keys.None && !fKey && !special)
            {
                Text = "mit Strg/Alt/Shift";
                return;
            }
            Keys = (int)(code | e.Modifiers);
            Changed?.Invoke();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            e.Handled = true;
            if (Text.EndsWith("+…")) Keys = _keys;
        }
    }
}

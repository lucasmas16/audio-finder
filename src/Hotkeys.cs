using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioFinder
{
    internal sealed class Hotkeys : NativeWindow, IDisposable
    {
        private readonly List<int> registered = new List<int>();
        internal readonly Dictionary<int, string> Errors = new Dictionary<int, string>();
        internal event Action<int> Pressed;
        internal Hotkeys() { CreateHandle(new CreateParams { Caption = "AudioFinder.Hotkeys", Parent = new IntPtr(-3) }); }
        internal void Clear()
        {
            foreach (int id in registered) UnregisterHotKey(Handle, id);
            registered.Clear(); Errors.Clear();
        }
        internal bool Apply(Settings settings)
        {
            Clear(); if (!settings.Enabled) return true;
            for (int i = 0; i < 8; i++)
            {
                var slot = settings.Slots[i]; if (slot.Key == 0 || String.IsNullOrEmpty(slot.DeviceId)) continue;
                if (!HotkeyText.Valid(slot.Key, slot.Modifiers)) { Errors[i] = "Combinação não permitida"; continue; }
                if (RegisterHotKey(Handle, i + 1, slot.Modifiers | 0x4000, slot.Key)) registered.Add(i + 1);
                else Errors[i] = "Atalho ocupado ou reservado pelo Windows (" + Marshal.GetLastWin32Error() + ")";
            }
            return Errors.Count == 0;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x312 && Pressed != null) Pressed(message.WParam.ToInt32() - 1);
            base.WndProc(ref message);
        }
        public void Dispose() { Clear(); DestroyHandle(); }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}

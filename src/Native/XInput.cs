using System;
using System.Runtime.InteropServices;

namespace Mischpult.Native
{
    /// <summary>3.5: Xbox-Controller ueber XInput (Windows 8+: xinput1_4, sonst xinput9_1_0). Fehler = kein Controller.</summary>
    internal static class XInput
    {
        public const ushort Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008, Start = 0x0010, Back = 0x0020,
                            LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public ushort wButtons;
            public byte bLeftTrigger, bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
        }

        public struct State
        {
            public ushort Buttons;
            public byte LeftTrigger, RightTrigger;
            public short LeftX, LeftY;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int GetState14(int index, out XINPUT_STATE state);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int GetState910(int index, out XINPUT_STATE state);

        private static int _dll;   // 0 unbekannt, 1 = 1_4, 2 = 9_1_0, -1 = keins

        public static bool TryGet(int index, out State s)
        {
            s = default(State);
            if (!Win32.IsWindows || _dll < 0) return false;
            XINPUT_STATE st;
            int hr;
            try
            {
                if (_dll == 2) hr = GetState910(index, out st);
                else
                {
                    try { hr = GetState14(index, out st); _dll = 1; }
                    catch (DllNotFoundException) { _dll = 2; hr = GetState910(index, out st); }
                }
            }
            catch { _dll = -1; return false; }
            if (hr != 0) return false;
            s.Buttons = st.wButtons; s.LeftTrigger = st.bLeftTrigger; s.RightTrigger = st.bRightTrigger;
            s.LeftX = st.sThumbLX; s.LeftY = st.sThumbLY;
            return true;
        }
    }
}

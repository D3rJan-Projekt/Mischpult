using System;
using System.Runtime.InteropServices;

namespace Mischpult.Native
{
    /// <summary>
    /// 3.4.6: Windows-Taskleiste – Knoepfe in der Vorschau (ITaskbarList3), Lautstaerke als Balken im Symbol und
    /// Aufgaben im Rechtsklick-Menue (Sprungliste, ICustomDestinationList). Alles optional; Fehler werden geschluckt.
    /// </summary>
    internal static class Taskbar
    {
        public const int WM_COMMAND = 0x0111;
        public const int THBN_CLICKED = 0x1800;

        public const uint THB_ICON = 0x2, THB_TOOLTIP = 0x4, THB_FLAGS = 0x8;
        public const uint THBF_ENABLED = 0, THBF_DISABLED = 0x1, THBF_HIDDEN = 0x8;
        public const int TBPF_NOPROGRESS = 0, TBPF_NORMAL = 0x2, TBPF_ERROR = 0x4, TBPF_PAUSED = 0x8;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct THUMBBUTTON
        {
            public uint dwMask;
            public uint iId;
            public uint iBitmap;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szTip;
            public uint dwFlags;
        }

        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ITaskbarList3
        {
            // ITaskbarList
            [PreserveSig] int HrInit();
            [PreserveSig] int AddTab(IntPtr hwnd);
            [PreserveSig] int DeleteTab(IntPtr hwnd);
            [PreserveSig] int ActivateTab(IntPtr hwnd);
            [PreserveSig] int SetActiveAlt(IntPtr hwnd);
            // ITaskbarList2
            [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
            // ITaskbarList3
            [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
            [PreserveSig] int SetProgressState(IntPtr hwnd, int tbpFlags);
            [PreserveSig] int RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
            [PreserveSig] int UnregisterTab(IntPtr hwndTab);
            [PreserveSig] int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
            [PreserveSig] int SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
            [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButton);
            [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButton);
            [PreserveSig] int ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
            [PreserveSig] int SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
            [PreserveSig] int SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
            [PreserveSig] int SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        private class CTaskbarList { }

        private static ITaskbarList3 _list;
        private static bool _failed;

        /// <summary>Die Taskleiste (oder null, wenn es nicht geht – z. B. kein Windows).</summary>
        public static ITaskbarList3 List
        {
            get
            {
                if (_list != null || _failed) return _list;
                try
                {
                    if (!Win32.IsWindows) { _failed = true; return null; }
                    var l = (ITaskbarList3)new CTaskbarList();
                    if (l.HrInit() < 0) { _failed = true; return null; }
                    _list = l;
                }
                catch { _failed = true; }
                return _list;
            }
        }

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // ------------------------------------------------------------------ Sprungliste

        [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICustomDestinationList
        {
            void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            void BeginList(out uint pcMinSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
            void AppendKnownCategory(int category);
            void AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
            void CommitList();
            void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            void AbortList();
        }

        [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6"), ClassInterface(ClassInterfaceType.None)]
        private class CDestinationList { }

        [ComImport, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectArray
        {
            void GetCount(out uint pcObjects);
            void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        }

        [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectCollection
        {
            // IObjectArray
            void GetCount(out uint pcObjects);
            void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            // IObjectCollection
            void AddObject([MarshalAs(UnmanagedType.Interface)] object punk);
            void AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray poaSource);
            void RemoveObjectAt(uint uiIndex);
            void Clear();
        }

        [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a"), ClassInterface(ClassInterfaceType.None)]
        private class CEnumerableObjectCollection { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath(IntPtr pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription(IntPtr pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory(IntPtr pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments(IntPtr pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation(IntPtr pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
        private class CShellLink { }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PropertyKey pkey);
            void GetValue(ref PropertyKey key, out PropVariant pv);
            void SetValue(ref PropertyKey key, ref PropVariant pv);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
        }

        /// <summary>PROPVARIANT (nur VT_LPWSTR und VT_BOOL), gross genug fuer 32 und 64 Bit.</summary>
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr p;
            [FieldOffset(8)] public short boolVal;
        }

        private static readonly Guid PkeyFmt = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9");   // PKEY_Title, pid 2
        private static readonly Guid PkeyAppUserModel = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");   // PKEY_AppUserModel_IsDestListSeparator, pid 6

        /// <summary>Eine Aufgabe in der Sprungliste (Titel null = Trennstrich).</summary>
        public struct JumpTask
        {
            public string Title, Arguments, Description;
            public int IconIndex;
        }

        /// <summary>Aufgaben setzen (leere Liste oder null = Sprungliste loeschen). true = hat geklappt.</summary>
        public static bool SetJumpList(string exePath, JumpTask[] tasks)
        {
            if (!Win32.IsWindows) return false;
            try
            {
                var list = (ICustomDestinationList)new CDestinationList();
                if (tasks == null || tasks.Length == 0)
                {
                    try { list.DeleteList(null); } catch { }
                    return true;
                }
                var iid = typeof(IObjectArray).GUID;
                list.BeginList(out uint slots, ref iid, out object removed);
                var coll = (IObjectCollection)new CEnumerableObjectCollection();
                foreach (var t in tasks) coll.AddObject(MakeLink(exePath, t));
                list.AddUserTasks((IObjectArray)coll);
                list.CommitList();
                return true;
            }
            catch { return false; }
        }

        private static object MakeLink(string exePath, JumpTask t)
        {
            var link = (IShellLinkW)new CShellLink();
            var store = (IPropertyStore)link;
            if (t.Title == null)
            {
                var key = new PropertyKey { fmtid = PkeyAppUserModel, pid = 6 };
                var pv = new PropVariant { vt = 11 /* VT_BOOL */, boolVal = -1 };
                store.SetValue(ref key, ref pv);
                store.Commit();
                return link;
            }
            link.SetPath(exePath);
            link.SetArguments(t.Arguments ?? "");
            link.SetIconLocation(exePath, t.IconIndex);
            if (!string.IsNullOrEmpty(t.Description)) link.SetDescription(t.Description);
            var tk = new PropertyKey { fmtid = PkeyFmt, pid = 2 };
            var tv = new PropVariant { vt = 31 /* VT_LPWSTR */, p = Marshal.StringToCoTaskMemUni(t.Title) };
            try { store.SetValue(ref tk, ref tv); store.Commit(); }
            finally { Marshal.FreeCoTaskMem(tv.p); }
            return link;
        }
    }
}

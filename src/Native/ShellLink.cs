using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Mischpult.Native
{
    /// <summary>4.0: Verknuepfungen (.lnk) anlegen – fuers Installieren (Startmenue, Desktop).</summary>
    internal static class ShellLink
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        /// <summary>Verknuepfung schreiben. Liefert false, wenn es nicht ging.</summary>
        public static bool Create(string lnkPath, string target, string args, string description)
        {
            if (!Win32.IsWindows) return false;
            object obj = null;
            try
            {
                obj = new CShellLink();
                var link = (IShellLinkW)obj;
                link.SetPath(target);
                if (!string.IsNullOrEmpty(args)) link.SetArguments(args);
                link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target));
                if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
                link.SetIconLocation(target, 0);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(lnkPath));
                ((IPersistFile)obj).Save(lnkPath, true);
                return true;
            }
            catch { return false; }
            finally { if (obj != null) try { Marshal.FinalReleaseComObject(obj); } catch { } }
        }
    }
}

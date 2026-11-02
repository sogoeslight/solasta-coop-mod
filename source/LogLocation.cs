using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SolastaCoop
{
    internal static class LogLocation
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint model);
        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        internal static string ChooseDirectory(Func<string> knownDownloads, string profile)
        {
            string downloads = null;
            try { downloads = knownDownloads(); } catch { }
            if (String.IsNullOrEmpty(downloads)) downloads = Path.Combine(profile, "Downloads");
            return Path.Combine(downloads, "SolastaCoopMod");
        }

        internal static string DirectoryPath
        {
            get { return ChooseDirectory(GetDownloads, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); }
        }

        private static string GetDownloads()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            IntPtr path = IntPtr.Zero;
            int initialized = CoInitializeEx(IntPtr.Zero, 0);
            try
            {
                Guid id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                int result = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out path);
                return result == 0 && path != IntPtr.Zero ? Marshal.PtrToStringUni(path) : null;
            }
            finally
            {
                if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path);
                if (initialized >= 0) CoUninitialize();
            }
        }
    }
}

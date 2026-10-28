using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace HearthstoneRank.Installation
{
    public static class InstallSafety
    {
        public const string Marker = "HearthstoneRank.install";
        public const string MarkerValue = "HearthstoneRank-managed-directory-v1";
        public const string RegistryKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\HearthstoneRank";
        public static readonly string[] ManagedFiles = { "HearthstoneRank.exe", "HearthstoneRank.exe.config", "LICENSE.txt", "HearthstoneRank-README.md", "Uninstall.exe", Marker };
        [StructLayout(LayoutKind.Sequential)]
        struct FileInformation
        {
            public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh,
                WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
        public static string CanonicalDirectory(string input)
        {
            if (String.IsNullOrWhiteSpace(input) || input.Length < 3 || !Char.IsLetter(input[0]) || input[1] != ':' || (input[2] != '\\' && input[2] != '/'))
                throw new IOException("安装目录必须是本地绝对路径。");
            string full = Path.GetFullPath(input);
            string root = Path.GetPathRoot(full);
            if (full.StartsWith("\\\\", StringComparison.Ordinal) || String.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new IOException("不能安装到网络路径或磁盘根目录。");
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        static FileAttributes? Attributes(string path)
        {
            try { return File.GetAttributes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }
        public static void CheckPath(string input)
        {
            string current = CanonicalDirectory(input);
            while (!String.IsNullOrEmpty(current))
            {
                var attributes = Attributes(current);
                if (attributes.HasValue && (attributes.Value & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("安装路径不能经过符号链接或目录联接。");
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
        }
        public static void CheckFile(string path)
        {
            var attributes = Attributes(path);
            if (!attributes.HasValue) return;
            if ((attributes.Value & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("安装文件不能是目录或链接：" + Path.GetFileName(path));
            using (var handle = CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero))
            {
                FileInformation info;
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, out info))
                    throw new IOException("无法验证安装文件：" + Path.GetFileName(path));
                if (info.Links != 1) throw new IOException("安装文件不能包含硬链接：" + Path.GetFileName(path));
            }
        }
        static bool HasMarker(string directory)
        {
            string marker = Path.Combine(directory, Marker);
            CheckFile(marker);
            return File.Exists(marker) && new FileInfo(marker).Length <= 128 && File.ReadAllText(marker, Encoding.UTF8) == MarkerValue;
        }
        public static bool RegisteredHere(string directory)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey))
            {
                if (key == null || !String.Equals(key.GetValue("DisplayName") as string, "旅店排名", StringComparison.Ordinal)) return false;
                string location = key.GetValue("InstallLocation") as string;
                if (String.IsNullOrWhiteSpace(location)) return false;
                return String.Equals(CanonicalDirectory(location), directory, StringComparison.OrdinalIgnoreCase);
            }
        }
        public static void CheckInstall(string input)
        {
            string directory = CanonicalDirectory(input);
            CheckPath(directory);
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey))
            {
                string existing = key == null ? null : key.GetValue("InstallLocation") as string;
                if (!String.IsNullOrWhiteSpace(existing))
                {
                    string old = CanonicalDirectory(existing);
                    if (!String.Equals(old, directory, StringComparison.OrdinalIgnoreCase) && ManagedFiles.Any(file => File.Exists(Path.Combine(old, file))))
                        throw new IOException("另一个位置已有旅店排名；请使用原安装目录或先卸载旧版。");
                }
            }
            foreach (string file in ManagedFiles) CheckFile(Path.Combine(directory, file));
            if (!Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any()) return;
            // 1.0.0 had no marker; accept only its exact registered location.
            bool legacy = RegisteredHere(directory) && File.Exists(Path.Combine(directory, "HearthstoneRank.exe")) && File.Exists(Path.Combine(directory, "Uninstall.exe"));
            if (!HasMarker(directory) && !legacy) throw new IOException("请选择空目录或已安装旅店排名的目录。");
        }
        public static void CheckUninstall(string input)
        {
            string directory = CanonicalDirectory(input);
            CheckPath(directory);
            foreach (string file in ManagedFiles) CheckFile(Path.Combine(directory, file));
            if (!RegisteredHere(directory)) throw new IOException("此目录与已注册的旅店排名安装位置不一致，已停止卸载。");
            if (!HasMarker(directory) && !File.Exists(Path.Combine(directory, "HearthstoneRank.exe")))
                throw new IOException("安装目录缺少归属信息，已停止卸载。");
        }
        public static void StopApplication()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HearthstoneRank");
            string suffix;
            using (var hash = SHA256.Create()) suffix = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(directory.ToLowerInvariant()))).Replace("-", "").Substring(0, 24);
            EventWaitHandle handle;
            if (EventWaitHandle.TryOpenExisting("Local\\HearthstoneRank.Quit." + suffix, out handle))
                using (handle) handle.Set();
        }
    }
    static class InstallerGuard
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "--shutdown") InstallSafety.StopApplication();
                else if (args.Length == 2 && args[0] == "--check-install") InstallSafety.CheckInstall(args[1]);
                else if (args.Length == 2 && args[0] == "--check-uninstall") InstallSafety.CheckUninstall(args[1]);
                else return 11;
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 10; }
        }
    }
}

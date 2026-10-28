using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using Microsoft.Win32;
using HearthstoneRank.Installation;

static class InstallSafetyTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string link, string existing, IntPtr security);
    static void Reject(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (IOException) { rejected = true; }
        if (!rejected) throw new Exception("FAIL " + label);
        Console.WriteLine("PASS: " + label);
    }
    static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(root);
            bool registeredElsewhere = false;
            using (var key = Registry.CurrentUser.OpenSubKey(InstallSafety.RegistryKey))
            {
                string existing = key == null ? null : key.GetValue("InstallLocation") as string;
                if (!String.IsNullOrWhiteSpace(existing)) registeredElsewhere = InstallSafety.ManagedFiles.Any(file => File.Exists(Path.Combine(existing, file)));
            }
            if (registeredElsewhere)
                Reject(() => InstallSafety.CheckInstall(Path.Combine(root, "new-empty")), "existing developer installation remains protected");
            else
            {
                InstallSafety.CheckInstall(Path.Combine(root, "new-empty"));
                Console.WriteLine("PASS: absent target with a safe parent is accepted");
            }
            Reject(() => InstallSafety.CanonicalDirectory(Path.GetPathRoot(root)), "disk root is rejected");
            Reject(() => InstallSafety.CanonicalDirectory("\\\\server\\share\\folder"), "network destination is rejected");
            Reject(() => InstallSafety.CanonicalDirectory("C:relative"), "drive-relative destination is rejected");
            string target = Path.Combine(root, "target.txt");
            string link = Path.Combine(root, "hardlink.txt");
            File.WriteAllText(target, "preserve");
            if (!CreateHardLink(link, target, IntPtr.Zero)) throw new Exception("Cannot create hardlink regression fixture: " + Marshal.GetLastWin32Error());
            Reject(() => InstallSafety.CheckFile(link), "hardlinked managed file is rejected");
            if (File.ReadAllText(target) != "preserve") throw new Exception("Hardlink validation modified target");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

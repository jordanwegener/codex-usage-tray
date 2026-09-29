using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex Usage Tray")]
[assembly: AssemblyDescription("Windows tray app for Codex usage limits")]
[assembly: AssemblyCompany("Jordan Wegener")]
[assembly: AssemblyProduct("Codex Usage Tray")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: ComVisible(false)]

namespace CodexUsageTray
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool ownsMutex;
            using (var mutex = new Mutex(true, "Local\\CodexUsageTray", out ownsMutex))
            {
                if (!ownsMutex) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Application.Run(new UsageApplicationContext());
            }
        }
    }
}


using Microsoft.Win32;
using System.Windows.Forms;

namespace CodexUsageTray
{
    internal static class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CodexUsageTray";

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) throw new System.InvalidOperationException("Windows startup settings are unavailable.");
                if (enabled)
                    key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                else
                    key.DeleteValue(ValueName, false);
            }
        }
    }
}

using System;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

namespace CodexUsageTray
{
    internal sealed class SettingsStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public SettingsStore()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexUsageTray");
            _path = Path.Combine(directory, "settings.json");
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_path)) return AppSettings.Defaults();
                var parsed = _json.Deserialize<AppSettings>(File.ReadAllText(_path));
                return Sanitize(parsed);
            }
            catch
            {
                return AppSettings.Defaults();
            }
        }

        public AppSettings Save(AppSettings settings)
        {
            settings = Sanitize(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, _json.Serialize(settings));
            return settings;
        }

        private static AppSettings Sanitize(AppSettings value)
        {
            var defaults = AppSettings.Defaults();
            if (value == null) return defaults;
            value.BackgroundColor = IsColor(value.BackgroundColor) ? value.BackgroundColor : defaults.BackgroundColor;
            value.TextColor = IsColor(value.TextColor) ? value.TextColor : defaults.TextColor;
            if (!value.ShowFiveHour && !value.ShowWeekly) value.ShowFiveHour = true;
            return value;
        }

        private static bool IsColor(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#') return false;
            try
            {
                ColorTranslator.FromHtml(value);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}


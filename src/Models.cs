using System;

namespace CodexUsageTray
{
    internal sealed class AppSettings
    {
        public string BackgroundColor { get; set; }
        public string TextColor { get; set; }
        public bool ShowFiveHour { get; set; }
        public bool ShowWeekly { get; set; }
        public bool ShowPercentageInTrayIcon { get; set; }
        public bool ShowResetInTrayIcon { get; set; }
        public int IconCycleSeconds { get; set; }
        public bool UseSeparateTrayIcons { get; set; }
        public bool StartWithWindows { get; set; }

        public static AppSettings Defaults()
        {
            return new AppSettings
            {
                BackgroundColor = "#111827",
                TextColor = "#F9FAFB",
                ShowFiveHour = true,
                ShowWeekly = true,
                ShowPercentageInTrayIcon = true,
                ShowResetInTrayIcon = false,
                IconCycleSeconds = 3,
                UseSeparateTrayIcons = true,
                StartWithWindows = false
            };
        }
    }

    internal sealed class UsageMeter
    {
        public double UsedPercent { get; set; }
        public double RemainingPercent { get; set; }
        public long? ResetsAt { get; set; }
    }

    internal sealed class UsageState
    {
        public bool SignedIn { get; set; }
        public string AccountEmail { get; set; }
        public UsageMeter FiveHour { get; set; }
        public UsageMeter Weekly { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool Refreshing { get; set; }
        public string Error { get; set; }

        public UsageState Copy()
        {
            return (UsageState)MemberwiseClone();
        }
    }

    internal sealed class AccountStatus
    {
        public bool SignedIn { get; set; }
        public string Email { get; set; }
    }

    internal sealed class RateWindow
    {
        public double UsedPercent { get; set; }
        public double DurationMinutes { get; set; }
        public long? ResetsAt { get; set; }
    }

    internal sealed class UsageWindows
    {
        public RateWindow FiveHour { get; set; }
        public RateWindow Weekly { get; set; }
    }
}


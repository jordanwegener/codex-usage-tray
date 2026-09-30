using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexUsageTray
{
    internal sealed class UsageApplicationContext : ApplicationContext
    {
        private readonly SettingsStore _settingsStore = new SettingsStore();
        private readonly CodexAppServer _codex = new CodexAppServer();
        private readonly SynchronizationContext _ui;
        private readonly NotifyIcon _tray;
        private NotifyIcon _secondaryTray;
        private readonly PopupForm _popup;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ContextMenuStrip _menu;
        private readonly System.Threading.Timer _refreshTimer;
        private readonly System.Threading.Timer _countdownTimer;
        private readonly System.Threading.Timer _iconCycleTimer;
        private Icon _icon;
        private Icon _secondaryIcon;

        private AppSettings _settings;
        private UsageState _state;
        private bool _refreshing;
        private bool _exiting;
        private bool _showResetPhase;

        public UsageApplicationContext()
        {
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _settings = _settingsStore.Load();
            _state = new UsageState();
            if (_settings.StartWithWindows)
            {
                try { StartupRegistration.SetEnabled(true); }
                catch { }
            }
            _popup = new PopupForm(_settings);
            _popup.RefreshRequested += RefreshUsage;
            _popup.LoginRequested += Login;
            _popup.SettingsChanged += SaveSettings;

            _statusItem = new ToolStripMenuItem("Usage unavailable") { Enabled = false };
            _menu = new ContextMenuStrip();
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Open", null, delegate { TogglePopup(); });
            _menu.Items.Add("Refresh", null, delegate { RefreshUsage(); });
            _menu.Items.Add("Sign in with ChatGPT", null, delegate { Login(); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Quit", null, delegate { ExitThread(); });

            _icon = CreateIcon(
                "C",
                ColorTranslator.FromHtml(_settings.BackgroundColor),
                ColorTranslator.FromHtml(_settings.TextColor));
            _tray = new NotifyIcon
            {
                Icon = _icon,
                Text = "Codex Usage — loading…",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _tray.MouseClick += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left) TogglePopup();
            };

            _refreshTimer = new System.Threading.Timer(
                delegate { _ui.Post(delegate { RefreshUsage(); }, null); },
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));
            _countdownTimer = new System.Threading.Timer(
                delegate
                {
                    _ui.Post(delegate
                    {
                        UpdateTrayTooltip();
                        _popup.RefreshCountdown();
                    }, null);
                },
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));
            _iconCycleTimer = new System.Threading.Timer(
                delegate
                {
                    _ui.Post(delegate
                    {
                        if (_exiting || !_settings.ShowPercentageInTrayIcon || !_settings.ShowResetInTrayIcon)
                            return;
                        _showResetPhase = !_showResetPhase;
                        UpdateTrayIcons();
                    }, null);
                },
                null,
                TimeSpan.FromSeconds(_settings.IconCycleSeconds),
                TimeSpan.FromSeconds(_settings.IconCycleSeconds));
            RefreshUsage();
        }

        protected override void ExitThreadCore()
        {
            if (_exiting) return;
            _exiting = true;
            _refreshTimer.Dispose();
            _countdownTimer.Dispose();
            _iconCycleTimer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            DisposeSecondaryTray();
            _menu.Dispose();
            _popup.Dispose();
            _codex.Dispose();
            _icon.Dispose();
            base.ExitThreadCore();
        }

        private async void RefreshUsage()
        {
            if (_refreshing || _exiting) return;
            _refreshing = true;
            _state.Refreshing = true;
            _state.Error = null;
            UpdateUi();
            try
            {
                var account = await _codex.ReadAccountAsync();
                if (!account.SignedIn)
                {
                    _state = new UsageState
                    {
                        SignedIn = false,
                        AccountEmail = null,
                        Refreshing = false,
                        Error = "Sign in with ChatGPT to load your usage."
                    };
                }
                else
                {
                    var windows = await _codex.ReadUsageAsync();
                    _state = new UsageState
                    {
                        SignedIn = true,
                        AccountEmail = account.Email,
                        FiveHour = ToMeter(windows.FiveHour),
                        Weekly = ToMeter(windows.Weekly),
                        UpdatedAt = DateTime.Now,
                        Refreshing = false,
                        Error = windows.FiveHour == null && windows.Weekly == null
                            ? "No 5-hour or weekly usage windows were returned."
                            : null
                    };
                }
            }
            catch (Exception exception)
            {
                _state.Refreshing = false;
                _state.Error = HumanizeError(exception);
            }
            finally
            {
                _refreshing = false;
                UpdateUi();
            }
        }

        private async void Login()
        {
            if (_refreshing || _exiting) return;
            _state.Error = null;
            UpdateUi();
            try
            {
                await _codex.LoginWithChatGptAsync();
                RefreshUsage();
            }
            catch (Exception exception)
            {
                _state.Error = HumanizeError(exception);
                UpdateUi();
            }
        }

        private void SaveSettings(AppSettings value)
        {
            try
            {
                StartupRegistration.SetEnabled(value.StartWithWindows);
                _settings = _settingsStore.Save(value);
                _showResetPhase = false;
                _iconCycleTimer.Change(
                    TimeSpan.FromSeconds(_settings.IconCycleSeconds),
                    TimeSpan.FromSeconds(_settings.IconCycleSeconds));
                _popup.ApplySettings(_settings);
                UpdateUi();
            }
            catch (Exception exception)
            {
                _state.Error = "Could not save settings: " + exception.Message;
                UpdateUi();
            }
        }

        private void UpdateUi()
        {
            if (_exiting) return;
            _popup.UpdateState(_state);
            var parts = new System.Collections.Generic.List<string>();
            if (_settings.ShowFiveHour && _state.FiveHour != null) parts.Add(FormatMeter("5h", _state.FiveHour));
            if (_settings.ShowWeekly && _state.Weekly != null) parts.Add(FormatMeter("7d", _state.Weekly));
            var summary = String.Join("  •  ", parts.ToArray());
            _statusItem.Text = String.IsNullOrWhiteSpace(summary) ? "Usage unavailable" : summary;
            UpdateTrayIcons();
            UpdateTrayTooltip();
        }

        private void UpdateTrayTooltip()
        {
            if (_exiting) return;
            if (UseSeparateTrayIcons())
            {
                SetTooltip(_tray, FormatTooltipMeter("5h", _state.FiveHour));
                if (_secondaryTray != null)
                    SetTooltip(_secondaryTray, FormatTooltipMeter("7d", _state.Weekly));
                return;
            }
            var lines = new System.Collections.Generic.List<string>();
            if (_settings.ShowFiveHour && _state.FiveHour != null)
                lines.Add(FormatTooltipMeter("5h", _state.FiveHour));
            if (_settings.ShowWeekly && _state.Weekly != null)
                lines.Add(FormatTooltipMeter("7d", _state.Weekly));
            var tooltip = lines.Count == 0 ? "Codex usage unavailable" : String.Join("\n", lines.ToArray());
            SetTooltip(_tray, tooltip);
        }

        private static void SetTooltip(NotifyIcon tray, string text)
        {
            tray.Text = text.Length <= 63 ? text : text.Substring(0, 63);
        }

        private void TogglePopup()
        {
            if (_popup.Visible)
            {
                _popup.Hide();
                return;
            }
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            _popup.Location = new Point(screen.Right - _popup.Width - 10, screen.Bottom - _popup.Height - 10);
            _popup.Show();
            _popup.Activate();
        }

        private static UsageMeter ToMeter(RateWindow window)
        {
            if (window == null) return null;
            var used = Math.Max(0, Math.Min(100, window.UsedPercent));
            return new UsageMeter
            {
                UsedPercent = used,
                RemainingPercent = 100 - used,
                DurationMinutes = window.DurationMinutes,
                ResetsAt = window.ResetsAt
            };
        }

        private static string FormatMeter(string name, UsageMeter meter)
        {
            return name + ": " + (meter == null ? "—" : Math.Round(meter.RemainingPercent) + "% left");
        }

        private static string FormatTooltipMeter(string name, UsageMeter meter)
        {
            var text = name + ": " + Math.Round(meter.RemainingPercent) + "%";
            if (meter.ResetsAt.HasValue) text += " • reset in " + FormatCountdown(meter.ResetsAt.Value);
            return text;
        }

        private static string FormatCountdown(long resetTimestamp)
        {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp) - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return "now";
            var days = (int)Math.Floor(remaining.TotalDays);
            if (days > 0) return days + "d " + remaining.Hours + "h";
            if (remaining.Hours > 0) return remaining.Hours + "h " + remaining.Minutes + "m";
            if (remaining.Minutes > 0) return remaining.Minutes + "m";
            return "<1m";
        }

        private static string HumanizeError(Exception exception)
        {
            var current = exception;
            while (current.InnerException != null) current = current.InnerException;
            var message = current.Message ?? current.ToString();
            var win32 = current as Win32Exception;
            if ((win32 != null && win32.NativeErrorCode == 2) ||
                message.IndexOf("cannot find the file", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("ENOENT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Codex CLI was not found. Install it and make sure 'codex' is on PATH.";
            }
            if (message.IndexOf("readonly database", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Codex could not update its local state. Check permissions for your .codex folder.";
            }
            return message.Trim();
        }

        private void UpdateTrayIcons()
        {
            if (_exiting) return;
            var background = ColorTranslator.FromHtml(_settings.BackgroundColor);
            var foreground = ColorTranslator.FromHtml(_settings.TextColor);

            if (UseSeparateTrayIcons())
            {
                EnsureSecondaryTray();
                ReplaceIcon(
                    _tray,
                    ref _icon,
                    GetIconText(_state.FiveHour, _state.FiveHour),
                    background,
                    foreground);
                ReplaceIcon(
                    _secondaryTray,
                    ref _secondaryIcon,
                    GetIconText(_state.Weekly, _state.Weekly),
                    background,
                    foreground);
                return;
            }

            DisposeSecondaryTray();
            UsageMeter percentageMeter = null;
            if (_settings.ShowFiveHour && _state.FiveHour != null) percentageMeter = _state.FiveHour;
            if (_settings.ShowWeekly && _state.Weekly != null &&
                (percentageMeter == null || _state.Weekly.RemainingPercent < percentageMeter.RemainingPercent))
            {
                percentageMeter = _state.Weekly;
            }

            UsageMeter resetMeter = null;
            if (_settings.ShowWeekly && _state.Weekly != null && _state.Weekly.ResetsAt.HasValue)
                resetMeter = _state.Weekly;
            else if (_settings.ShowFiveHour && _state.FiveHour != null && _state.FiveHour.ResetsAt.HasValue)
                resetMeter = _state.FiveHour;
            ReplaceIcon(
                _tray,
                ref _icon,
                GetIconText(percentageMeter, resetMeter),
                background,
                foreground);
        }

        private bool UseSeparateTrayIcons()
        {
            return _settings.UseSeparateTrayIcons &&
                _settings.ShowFiveHour &&
                _settings.ShowWeekly &&
                _state.FiveHour != null &&
                _state.Weekly != null;
        }

        private string GetIconText(UsageMeter percentageMeter, UsageMeter resetMeter)
        {
            var canShowPercentage = _settings.ShowPercentageInTrayIcon && percentageMeter != null;
            var canShowReset = _settings.ShowResetInTrayIcon && resetMeter != null && resetMeter.ResetsAt.HasValue;
            if (canShowReset && (!canShowPercentage || _showResetPhase))
                return FormatIconCountdown(resetMeter.ResetsAt.Value);
            if (canShowPercentage) return Math.Round(percentageMeter.RemainingPercent).ToString("0");
            return "C";
        }

        private void EnsureSecondaryTray()
        {
            if (_secondaryTray != null) return;
            _secondaryIcon = CreateIcon(
                "C",
                ColorTranslator.FromHtml(_settings.BackgroundColor),
                ColorTranslator.FromHtml(_settings.TextColor));
            _secondaryTray = new NotifyIcon
            {
                Icon = _secondaryIcon,
                Text = "Codex 7-day usage",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _secondaryTray.MouseClick += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left) TogglePopup();
            };
        }

        private void DisposeSecondaryTray()
        {
            if (_secondaryTray != null)
            {
                _secondaryTray.Visible = false;
                _secondaryTray.Dispose();
                _secondaryTray = null;
            }
            if (_secondaryIcon != null)
            {
                _secondaryIcon.Dispose();
                _secondaryIcon = null;
            }
        }

        private static void ReplaceIcon(
            NotifyIcon tray,
            ref Icon current,
            string text,
            Color background,
            Color foreground)
        {
            var next = CreateIcon(text, background, foreground);
            var previous = current;
            current = next;
            tray.Icon = next;
            if (previous != null) previous.Dispose();
        }

        private static string FormatIconCountdown(long resetTimestamp)
        {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp) - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return "0h";
            var days = (int)Math.Floor(remaining.TotalDays);
            if (days > 0) return days + "d";
            var hours = (int)Math.Floor(remaining.TotalHours);
            if (hours > 0) return hours + "h";
            var minutes = Math.Max(1, (int)Math.Floor(remaining.TotalMinutes));
            return minutes + "m";
        }

        private static Icon CreateIcon(string text, Color background, Color foreground)
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(background))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.FillEllipse(brush, new Rectangle(1, 1, 30, 30));
                var fontSize = text.Length >= 3 ? 16F : (text.Length == 2 ? 19F : 22F);
                using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    TextRenderer.DrawText(
                        graphics,
                        text,
                        font,
                        new Rectangle(0, 0, 32, 31),
                        foreground,
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding |
                        TextFormatFlags.SingleLine);
                }
                var handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}


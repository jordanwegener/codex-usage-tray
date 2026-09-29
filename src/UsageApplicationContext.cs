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
        private readonly PopupForm _popup;
        private readonly ToolStripMenuItem _statusItem;
        private readonly System.Threading.Timer _refreshTimer;
        private readonly System.Threading.Timer _countdownTimer;
        private Icon _icon;

        private AppSettings _settings;
        private UsageState _state;
        private bool _refreshing;
        private bool _exiting;

        public UsageApplicationContext()
        {
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _settings = _settingsStore.Load();
            _state = new UsageState();
            _popup = new PopupForm(_settings);
            _popup.RefreshRequested += RefreshUsage;
            _popup.LoginRequested += Login;
            _popup.SettingsChanged += SaveSettings;

            _statusItem = new ToolStripMenuItem("Usage unavailable") { Enabled = false };
            var menu = new ContextMenuStrip();
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open", null, delegate { TogglePopup(); });
            menu.Items.Add("Refresh", null, delegate { RefreshUsage(); });
            menu.Items.Add("Sign in with ChatGPT", null, delegate { Login(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, delegate { ExitThread(); });

            _icon = CreateIcon(
                "C",
                ColorTranslator.FromHtml(_settings.BackgroundColor),
                ColorTranslator.FromHtml(_settings.TextColor));
            _tray = new NotifyIcon
            {
                Icon = _icon,
                Text = "Codex Usage — loading…",
                Visible = true,
                ContextMenuStrip = menu
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
                delegate { _ui.Post(delegate { UpdateTrayTooltip(); }, null); },
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));
            RefreshUsage();
        }

        protected override void ExitThreadCore()
        {
            if (_exiting) return;
            _exiting = true;
            _refreshTimer.Dispose();
            _countdownTimer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
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
                _settings = _settingsStore.Save(value);
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
            UpdateTrayTooltip();
            UpdateTrayIcon();
        }

        private void UpdateTrayTooltip()
        {
            if (_exiting) return;
            var lines = new System.Collections.Generic.List<string>();
            if (_settings.ShowFiveHour && _state.FiveHour != null)
                lines.Add(FormatTooltipMeter("5h", _state.FiveHour));
            if (_settings.ShowWeekly && _state.Weekly != null)
                lines.Add(FormatTooltipMeter("7d", _state.Weekly));
            var tooltip = lines.Count == 0 ? "Codex usage unavailable" : String.Join("\n", lines.ToArray());
            _tray.Text = tooltip.Length <= 63 ? tooltip : tooltip.Substring(0, 63);
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

        private void UpdateTrayIcon()
        {
            UsageMeter selected = null;
            if (_settings.ShowFiveHour && _state.FiveHour != null) selected = _state.FiveHour;
            if (_settings.ShowWeekly && _state.Weekly != null &&
                (selected == null || _state.Weekly.RemainingPercent < selected.RemainingPercent))
            {
                selected = _state.Weekly;
            }

            var text = _settings.ShowPercentageInTrayIcon && selected != null
                ? Math.Round(selected.RemainingPercent).ToString("0")
                : "C";
            var next = CreateIcon(
                text,
                ColorTranslator.FromHtml(_settings.BackgroundColor),
                ColorTranslator.FromHtml(_settings.TextColor));
            var previous = _icon;
            _icon = next;
            _tray.Icon = next;
            if (previous != null) previous.Dispose();
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


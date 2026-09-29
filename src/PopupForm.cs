using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexUsageTray
{
    internal sealed class PopupForm : Form
    {
        private readonly Label _account;
        private readonly MeterControl _fiveHour;
        private readonly MeterControl _weekly;
        private readonly Label _error;
        private readonly Button _refresh;
        private readonly Button _login;
        private readonly Button _settingsButton;
        private readonly Panel _settingsPanel;
        private readonly Button _backgroundButton;
        private readonly Button _textButton;
        private readonly CheckBox _showPercentage;
        private readonly CheckBox _showFiveHour;
        private readonly CheckBox _showWeekly;
        private readonly Label _updated;

        private AppSettings _settings;
        private UsageState _state;
        private bool _layingOut;

        public event Action RefreshRequested;
        public event Action LoginRequested;
        public event Action<AppSettings> SettingsChanged;

        public PopupForm(AppSettings settings)
        {
            _settings = settings;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(360, 390);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            KeyPreview = true;

            var eyebrow = MakeLabel("CODEX USAGE", 20, 14, 170, 17, FontStyle.Bold, 8F);
            var title = MakeLabel("Allowance", 20, 30, 220, 32, FontStyle.Bold, 19F);
            var close = MakeButton("×", 315, 15, 28, 28);
            close.Font = new Font("Segoe UI", 14F, FontStyle.Regular);
            close.Click += delegate { Hide(); };

            _account = MakeLabel("Checking sign-in…", 20, 63, 320, 18, FontStyle.Regular, 8F);
            _fiveHour = new MeterControl { MeterName = "5-hour limit" };
            _weekly = new MeterControl { MeterName = "7-day limit" };
            Controls.Add(_fiveHour);
            Controls.Add(_weekly);

            _error = MakeLabel("", 20, 0, 320, 44, FontStyle.Regular, 8F);
            _error.Padding = new Padding(9, 6, 9, 6);
            _error.Visible = false;

            _refresh = MakeButton("Refresh", 20, 0, 112, 34);
            _login = MakeButton("Sign in", 139, 0, 145, 34);
            _settingsButton = MakeButton("⚙", 291, 0, 49, 34);
            _refresh.Click += delegate
            {
                var handler = RefreshRequested;
                if (handler != null) handler();
            };
            _login.Click += delegate
            {
                var handler = LoginRequested;
                if (handler != null) handler();
            };
            _settingsButton.Click += delegate
            {
                _settingsPanel.Visible = !_settingsPanel.Visible;
                PerformCustomLayout();
            };

            _settingsPanel = new Panel { Visible = false };
            Controls.Add(_settingsPanel);
            var backgroundLabel = MakePanelLabel("Tray background", 12, 10, 130, 24);
            var textLabel = MakePanelLabel("Tray text", 12, 38, 130, 24);
            _backgroundButton = MakePanelButton("Choose…", 208, 8, 96, 25);
            _textButton = MakePanelButton("Choose…", 208, 36, 96, 25);
            _showPercentage = new CheckBox
            {
                Text = "Show percentage in tray icon",
                AutoSize = true,
                Location = new Point(12, 70)
            };
            _showFiveHour = new CheckBox { Text = "Show 5-hour", AutoSize = true, Location = new Point(12, 97) };
            _showWeekly = new CheckBox { Text = "Show 7-day", AutoSize = true, Location = new Point(166, 97) };
            _settingsPanel.Controls.Add(backgroundLabel);
            _settingsPanel.Controls.Add(textLabel);
            _settingsPanel.Controls.Add(_backgroundButton);
            _settingsPanel.Controls.Add(_textButton);
            _settingsPanel.Controls.Add(_showPercentage);
            _settingsPanel.Controls.Add(_showFiveHour);
            _settingsPanel.Controls.Add(_showWeekly);
            _backgroundButton.Click += delegate { PickColor(true); };
            _textButton.Click += delegate { PickColor(false); };
            _showPercentage.CheckedChanged += VisibilityChanged;
            _showFiveHour.CheckedChanged += VisibilityChanged;
            _showWeekly.CheckedChanged += VisibilityChanged;

            _updated = MakeLabel("Not updated yet", 20, 0, 320, 16, FontStyle.Regular, 7.5F);
            _updated.TextAlign = ContentAlignment.MiddleCenter;

            Controls.Add(eyebrow);
            Controls.Add(title);
            Controls.Add(close);
            Controls.Add(_account);
            Controls.Add(_error);
            Controls.Add(_refresh);
            Controls.Add(_login);
            Controls.Add(_settingsButton);
            Controls.Add(_updated);

            ApplySettings(settings);
            PerformCustomLayout();
            Deactivate += delegate
            {
                if (!ContainsFocus && !ColorDialogOpen) Hide();
            };
        }

        public bool ColorDialogOpen { get; private set; }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings;
            var background = Color.FromArgb(17, 24, 39);
            var text = Color.FromArgb(249, 250, 251);
            var surface = Blend(background, text, 0.08F);
            BackColor = background;
            ForeColor = text;
            foreach (Control control in Controls) ApplyColors(control, background, text, surface);
            _settingsPanel.BackColor = surface;
            _fiveHour.SetTheme(background, text);
            _weekly.SetTheme(background, text);
            _account.ForeColor = Blend(background, text, 0.66F);
            _updated.ForeColor = Blend(background, text, 0.58F);
            _error.BackColor = Blend(background, Color.FromArgb(239, 68, 68), 0.16F);
            _error.ForeColor = Blend(background, Color.FromArgb(254, 202, 202), 0.90F);
            _backgroundButton.Text = settings.BackgroundColor.ToUpperInvariant();
            _backgroundButton.BackColor = ColorTranslator.FromHtml(settings.BackgroundColor);
            _backgroundButton.ForeColor = ContrastColor(_backgroundButton.BackColor);
            _textButton.Text = settings.TextColor.ToUpperInvariant();
            _textButton.BackColor = ColorTranslator.FromHtml(settings.TextColor);
            _textButton.ForeColor = ContrastColor(_textButton.BackColor);
            var wasLayingOut = _layingOut;
            _layingOut = true;
            _showPercentage.Checked = settings.ShowPercentageInTrayIcon;
            _showFiveHour.Checked = settings.ShowFiveHour;
            _showWeekly.Checked = settings.ShowWeekly;
            _layingOut = wasLayingOut;
            UpdateMeterAvailability();
            PerformCustomLayout();
            Invalidate(true);
        }

        public void UpdateState(UsageState state)
        {
            _state = state;
            _account.Text = state.SignedIn
                ? (String.IsNullOrWhiteSpace(state.AccountEmail) ? "Signed in with ChatGPT" : state.AccountEmail)
                : "Not signed in";
            _login.Text = state.SignedIn ? "Sign in again" : "Sign in";
            _refresh.Enabled = !state.Refreshing;
            _refresh.Text = state.Refreshing ? "Refreshing…" : "Refresh";
            _login.Enabled = !state.Refreshing;
            _fiveHour.Meter = state.FiveHour;
            _weekly.Meter = state.Weekly;
            _error.Text = state.Error ?? "";
            _error.Visible = !String.IsNullOrWhiteSpace(state.Error);
            _updated.Text = state.UpdatedAt.HasValue
                ? "Updated " + state.UpdatedAt.Value.ToString("t")
                : "Not updated yet";
            UpdateMeterAvailability();
            PerformCustomLayout();
        }

        protected override void OnFormClosing(FormClosingEventArgs eventArgs)
        {
            if (eventArgs.CloseReason == CloseReason.UserClosing)
            {
                eventArgs.Cancel = true;
                Hide();
            }
            base.OnFormClosing(eventArgs);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Hide();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void PerformCustomLayout()
        {
            if (_layingOut) return;
            var preserveBottom = Visible ? Bottom : 0;
            _layingOut = true;
            try
            {
                var y = 88;
                if (_fiveHour.Visible)
                {
                    _fiveHour.Bounds = new Rectangle(20, y, 320, 88);
                    y += 98;
                }
                if (_weekly.Visible)
                {
                    _weekly.Bounds = new Rectangle(20, y, 320, 88);
                    y += 98;
                }
                if (_error.Visible)
                {
                    _error.Bounds = new Rectangle(20, y, 320, 44);
                    y += 54;
                }
                _refresh.Location = new Point(20, y);
                _login.Location = new Point(139, y);
                _settingsButton.Location = new Point(291, y);
                y += 44;
                if (_settingsPanel.Visible)
                {
                    _settingsPanel.Bounds = new Rectangle(20, y, 320, 132);
                    y += 142;
                }
                _updated.Bounds = new Rectangle(20, y, 320, 16);
                y += 27;
                ClientSize = new Size(360, y);
                if (Visible)
                {
                    var workArea = Screen.FromControl(this).WorkingArea;
                    var targetBottom = Math.Min(preserveBottom, workArea.Bottom - 10);
                    Top = Math.Max(workArea.Top + 10, targetBottom - Height);
                    Left = Math.Max(workArea.Left + 10, Math.Min(Left, workArea.Right - Width - 10));
                }
            }
            finally
            {
                _layingOut = false;
            }
        }

        private void VisibilityChanged(object sender, EventArgs eventArgs)
        {
            if (_layingOut) return;
            if (!_showFiveHour.Checked && !_showWeekly.Checked)
            {
                if (ReferenceEquals(sender, _showFiveHour)) _showWeekly.Checked = true;
                else _showFiveHour.Checked = true;
            }
            var next = new AppSettings
            {
                BackgroundColor = _settings.BackgroundColor,
                TextColor = _settings.TextColor,
                ShowFiveHour = _showFiveHour.Checked,
                ShowWeekly = _showWeekly.Checked,
                ShowPercentageInTrayIcon = _showPercentage.Checked
            };
            RaiseSettingsChanged(next);
        }

        private void PickColor(bool background)
        {
            using (var dialog = new ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.Color = ColorTranslator.FromHtml(background ? _settings.BackgroundColor : _settings.TextColor);
                ColorDialogOpen = true;
                try
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                }
                finally
                {
                    ColorDialogOpen = false;
                }
                var next = new AppSettings
                {
                    BackgroundColor = background ? ToHtml(dialog.Color) : _settings.BackgroundColor,
                    TextColor = background ? _settings.TextColor : ToHtml(dialog.Color),
                    ShowFiveHour = _settings.ShowFiveHour,
                    ShowWeekly = _settings.ShowWeekly,
                    ShowPercentageInTrayIcon = _settings.ShowPercentageInTrayIcon
                };
                RaiseSettingsChanged(next);
            }
        }

        private void UpdateMeterAvailability()
        {
            var hasSnapshot = _state != null && _state.SignedIn && _state.UpdatedAt.HasValue;
            var fiveHourAvailable = !hasSnapshot || _state.FiveHour != null;
            var weeklyAvailable = !hasSnapshot || _state.Weekly != null;
            _fiveHour.Visible = _settings.ShowFiveHour && fiveHourAvailable;
            _weekly.Visible = _settings.ShowWeekly && weeklyAvailable;
            _showFiveHour.Enabled = fiveHourAvailable;
            _showWeekly.Enabled = weeklyAvailable;
            _showFiveHour.Text = fiveHourAvailable ? "Show 5-hour" : "5-hour unavailable";
            _showWeekly.Text = weeklyAvailable ? "Show 7-day" : "7-day unavailable";
        }

        private void RaiseSettingsChanged(AppSettings settings)
        {
            _settings = settings;
            ApplySettings(settings);
            var handler = SettingsChanged;
            if (handler != null) handler(settings);
        }

        private Label MakeLabel(
            string text,
            int x,
            int y,
            int width,
            int height,
            FontStyle style,
            float size)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font("Segoe UI", size, style, GraphicsUnit.Point),
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
        }

        private Button MakeButton(string text, int x, int y, int width, int height)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = true
            };
        }

        private static Label MakePanelLabel(string text, int x, int y, int width, int height)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height) };
        }

        private static Button MakePanelButton(string text, int x, int y, int width, int height)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
        }

        private static void ApplyColors(Control control, Color background, Color text, Color surface)
        {
            if (control is Button || control is Panel)
                control.BackColor = surface;
            else if (!(control is MeterControl))
                control.BackColor = Color.Transparent;
            control.ForeColor = text;
            var button = control as Button;
            if (button != null) button.FlatAppearance.BorderColor = Blend(background, text, 0.18F);
            foreach (Control child in control.Controls) ApplyColors(child, background, text, surface);
        }

        internal static Color Blend(Color background, Color foreground, float amount)
        {
            return Color.FromArgb(
                255,
                (int)(background.R + (foreground.R - background.R) * amount),
                (int)(background.G + (foreground.G - background.G) * amount),
                (int)(background.B + (foreground.B - background.B) * amount));
        }

        private static string ToHtml(Color color)
        {
            return String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static Color ContrastColor(Color color)
        {
            var luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
            return luminance > 150 ? Color.Black : Color.White;
        }
    }

    internal sealed class MeterControl : Control
    {
        private UsageMeter _meter;
        private Color _background = Color.FromArgb(17, 24, 39);
        private Color _text = Color.White;

        public MeterControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public string MeterName { get; set; }

        public UsageMeter Meter
        {
            get { return _meter; }
            set { _meter = value; Invalidate(); }
        }

        public void SetTheme(Color background, Color text)
        {
            _background = background;
            _text = text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            var graphics = eventArgs.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var surface = new SolidBrush(PopupForm.Blend(_background, _text, 0.08F)))
            using (var path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 11))
            {
                graphics.FillPath(surface, path);
                using (var pen = new Pen(PopupForm.Blend(_background, _text, 0.16F)))
                    graphics.DrawPath(pen, path);
            }

            var muted = PopupForm.Blend(_background, _text, 0.64F);
            using (var nameFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            using (var valueFont = new Font("Segoe UI", 16F, FontStyle.Bold))
            using (var smallFont = new Font("Segoe UI", 7.5F, FontStyle.Regular))
            using (var nameBrush = new SolidBrush(muted))
            using (var textBrush = new SolidBrush(_text))
            {
                graphics.DrawString(MeterName ?? "Usage", nameFont, nameBrush, 13, 10);
                var valueText = _meter == null ? "—" : Math.Round(_meter.RemainingPercent) + "% left";
                var valueSize = graphics.MeasureString(valueText, valueFont);
                graphics.DrawString(valueText, valueFont, textBrush, Width - valueSize.Width - 12, 5);

                var track = new Rectangle(13, 40, Width - 26, 7);
                using (var trackBrush = new SolidBrush(PopupForm.Blend(_background, _text, 0.14F)))
                    graphics.FillRoundedRectangle(trackBrush, track, 4);
                if (_meter != null)
                {
                    var width = Math.Max(0, Math.Min(track.Width, (int)(track.Width * _meter.RemainingPercent / 100.0)));
                    if (width > 0)
                    {
                        using (var accent = new SolidBrush(Color.FromArgb(16, 185, 129)))
                            graphics.FillRoundedRectangle(accent, new Rectangle(track.X, track.Y, width, track.Height), 4);
                    }
                }

                string resetText;
                if (_meter == null) resetText = "Usage window unavailable";
                else if (_meter.ResetsAt.HasValue)
                {
                    var reset = DateTimeOffset.FromUnixTimeSeconds(_meter.ResetsAt.Value).LocalDateTime;
                    resetText = "Resets " + reset.ToString("ddd h:mm tt");
                }
                else resetText = Math.Round(_meter.UsedPercent) + "% used";
                graphics.DrawString(resetText, smallFont, nameBrush, 13, 57);
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
            var actualRadius = Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2);
            using (var path = new GraphicsPath())
            {
                var diameter = actualRadius * 2;
                path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
                path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
                path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                graphics.FillPath(brush, path);
            }
        }
    }
}


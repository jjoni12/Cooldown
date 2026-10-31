using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Cooldown
{
    public partial class FlyoutWindow : Window
    {
        static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(150);
        static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(100);
        static readonly TimeSpan ResizeDuration = TimeSpan.FromMilliseconds(100);
        const double ShadowRoom = 16;
        const double Gap = 12;
        const int Parked = -32000;

        sealed class Slide
        {
            public bool Opening;
            public double From;
            public TimeSpan Duration;
            public bool Rendered;
            public readonly Stopwatch Clock = new Stopwatch();
        }

        readonly UsageStore _store;
        readonly Dictionary<string, ToggleVisual> _serviceToggles = new Dictionary<string, ToggleVisual>();
        ToggleVisual _startupToggle;
        Drawer _groupDrawer;
        Choice _intervalChoice;
        Choice _languageChoice;
        IntPtr _hwnd;
        Slide _slide;
        bool _open;
        bool _closing;
        bool _stale;
        DateTime _hiddenAt;

        public bool IsOpen => _open;
        public event Action OpenChanged;

        public FlyoutWindow(UsageStore store)
        {
            InitializeComponent();
            _store = store;

            _store.Changed += () =>
            {
                if (_open) Render();
                else _stale = true;
            };
            Settings.Changed += UpdateSettings;
            Theme.Changed += () => { ApplyBackdrop(); BuildSettings(); Render(); };
            Text.Changed += () => { BuildSettings(); Render(); };

            Deactivated += (s, e) => Dismiss();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape) return;
                if (_intervalChoice.IsOpen || _languageChoice.IsOpen)
                {
                    _intervalChoice.Close();
                    _languageChoice.Close();
                }
                else
                {
                    Dismiss();
                }
            };
            RefreshButton.Click += (s, e) => _store.RefreshAll();
            SettingsButton.Click += (s, e) => Resize(() => ShowSettings(SettingsPage.Visibility != Visibility.Visible));
            SettingsButton.PreviewMouseLeftButtonDown += (s, e) => SpinGear();
            BackButton.Click += (s, e) => Resize(() => ShowSettings(false));

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW);
            };
            Body.SizeChanged += (s, e) => Body.Clip = new RectangleGeometry(new Rect(Body.RenderSize), 7, 7);
            // Nothing is drawn below the taskbar's top edge: the taskbar is translucent and would show the panel through.
            Root.SizeChanged += (s, e) =>
                Root.Clip = new RectangleGeometry(new Rect(0, 0, Root.ActualWidth, Math.Max(0, Root.ActualHeight - ShadowRoom + Gap)));
            ApplyBackdrop();
            BuildSettings();
        }

        void BuildSettings()
        {
            var palette = Theme.Current;
            var rowStyle = (Style)FindResource("RowButton");
            _intervalChoice?.Close();
            _languageChoice?.Close();
            SettingsBody.Children.Clear();
            _serviceToggles.Clear();

            SettingsBody.Children.Add(Caption(Text.ShownServices, palette, 2));
            var ids = _store.Providers.Select(p => p.Id).ToList();
            foreach (var provider in _store.Providers)
            {
                var id = provider.Id;
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(Logos.Create(id, 16, palette.Text));
                var name = Visuals.Label(provider.Name.Split('·')[0].Trim(), 14, palette.Text);
                name.Margin = new Thickness(10, 0, 0, 0);
                label.Children.Add(name);
                var toggle = new ToggleVisual(Settings.IsShown(id), palette);
                _serviceToggles[id] = toggle;
                SettingsBody.Children.Add(ToggleRow(label, toggle, rowStyle, palette, () => Settings.ToggleShown(id, ids)));
            }

            SettingsBody.Children.Add(Caption(Text.General, palette, 12));
            _groupDrawer = new Drawer(Text.AntigravityGroup, new[] { Text.GroupGemini, Text.GroupClaudeGpt },
                Settings.AntigravityClaudeGpt ? 1 : 0, palette, rowStyle);
            _groupDrawer.Selected += i => Settings.SetAntigravityClaudeGpt(i == 1);
            SettingsBody.Children.Add(_groupDrawer.Root);

            Func<double> overlayBottom = () => Root.ActualHeight - ShadowRoom + Gap;
            _intervalChoice = new Choice(Text.RefreshInterval, Settings.RefreshChoices.Select(Text.Minutes).ToArray(),
                Array.IndexOf(Settings.RefreshChoices, Settings.RefreshMinutes), palette, Overlay, overlayBottom);
            _intervalChoice.Selected += i => Settings.SetRefreshMinutes(Settings.RefreshChoices[i]);
            SettingsBody.Children.Add(_intervalChoice.Root);

            var languages = Text.Available();
            var chosen = languages.ToList().FindIndex(l => string.Equals(l.Code, Settings.Language, StringComparison.OrdinalIgnoreCase));
            _languageChoice = new Choice(Text.LanguageSetting, new[] { Text.UseWindowsLanguage }.Concat(languages.Select(l => l.Name)).ToArray(),
                chosen + 1, palette, Overlay, overlayBottom);
            _languageChoice.Selected += i => Settings.SetLanguage(i == 0 ? null : languages[i - 1].Code);
            SettingsBody.Children.Add(_languageChoice.Root);

            _startupToggle = new ToggleVisual(Startup.Enabled, palette);
            SettingsBody.Children.Add(ToggleRow(Visuals.Label(Text.RunAtStartup, 14, palette.Text), _startupToggle, rowStyle, palette, () =>
            {
                Startup.Enabled = !Startup.Enabled;
                _startupToggle.SetOn(Startup.Enabled, animate: true);
            }));

            var quit = new Button { Style = rowStyle, Margin = new Thickness(-12, 0, -12, 0), Content = Row(Text.Quit, null, palette), Foreground = palette.Text, Tag = palette.Hover };
            quit.Click += (s, e) => Application.Current.Shutdown();
            SettingsBody.Children.Add(quit);
        }

        void UpdateSettings()
        {
            foreach (var pair in _serviceToggles) pair.Value.SetOn(Settings.IsShown(pair.Key), animate: true);
            _groupDrawer.SetSelected(Settings.AntigravityClaudeGpt ? 1 : 0);
            _intervalChoice.SetSelected(Array.IndexOf(Settings.RefreshChoices, Settings.RefreshMinutes));
        }

        static TextBlock Caption(string text, Palette palette, double top)
        {
            var caption = Visuals.Label(text, Visuals.Caption, palette.TextSecondary);
            caption.Margin = new Thickness(0, top, 0, 2);
            return caption;
        }

        static Button ToggleRow(FrameworkElement label, ToggleVisual toggle, Style rowStyle, Palette palette, Action click)
        {
            var row = new Button { Style = rowStyle, Margin = new Thickness(-12, 0, -12, 0), Content = Row(label, toggle), Foreground = palette.Text, Tag = palette.Hover };
            row.Click += (s, e) => click();
            row.MouseEnter += (s, e) => toggle.SetPointer(true, false);
            row.MouseLeave += (s, e) => toggle.SetPointer(false, false);
            row.PreviewMouseLeftButtonDown += (s, e) => toggle.SetPointer(true, true);
            row.PreviewMouseLeftButtonUp += (s, e) => toggle.SetPointer(true, false);
            return row;
        }

        // Height changes animate inside a fixed-size window; resizing a window every frame stutters.
        void Resize(Action change)
        {
            var from = PanelHost.ActualHeight;
            PanelHost.BeginAnimation(HeightProperty, null);
            if (!_open || _closing || _slide != null)
            {
                PanelHost.Height = double.NaN;
                change();
                return;
            }
            PanelHost.Height = from;
            change();
            Panel.Measure(new Size(PanelHost.Width, double.PositiveInfinity));
            var to = Panel.DesiredSize.Height;
            var grow = new DoubleAnimation(from, to, ResizeDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            grow.Completed += (s, e) =>
            {
                PanelHost.BeginAnimation(HeightProperty, null);
                PanelHost.Height = double.NaN;
            };
            PanelHost.BeginAnimation(HeightProperty, grow);
        }

        public void Park()
        {
            Render();
            PanelShift.Y = Height;
            new WindowInteropHelper(this).EnsureHandle();
            ShowActivated = false;
            Native.MoveBelowTaskbar(_hwnd, Parked, Parked);
            Show();
        }

        public void Toggle()
        {
            if (_closing) return;
            if (_open)
            {
                Dismiss();
                return;
            }
            // The click that closed the flyout (by activating something else) must not reopen it.
            if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 300) return;

            if (!IsVisible) Park();
            Memory.Cancel();
            if (_stale)
            {
                _stale = false;
                Render();
            }
            FitToScreen();
            UpdateLayout();
            if (!Layout(out var x, out var y, out var slides)) return;
            PlaceAt(x, y);
            if (slides) StartSlide(opening: true, from: HiddenShift);
            else SetShift(0);
            SetOpen(true);
            Activate();
            _store.RefreshIfOlderThan(TimeSpan.FromMinutes(2));
        }

        void Dismiss()
        {
            if (!_open || _closing) return;
            _closing = true;
            // An open drop-down isn't part of the sliding panel; like a WinUI popup, it closes at once.
            _intervalChoice.Close();
            _languageChoice.Close();
            if (Layout(out _, out _, out var slides) && slides) StartSlide(opening: false, from: PanelShift.Y);
            else FinishHide();
        }

        void FinishHide()
        {
            Native.MoveBelowTaskbar(_hwnd, Parked, Parked);
            PanelShift.Y = Height;
            _groupDrawer.SetExpanded(false, animate: false);
            _intervalChoice.Close();
            _languageChoice.Close();
            Resize(() => ShowSettings(false));
            _closing = false;
            _hiddenAt = DateTime.UtcNow;
            SetOpen(false);
            Memory.TrimSoon(TimeSpan.FromSeconds(5));
        }

        void SetOpen(bool open)
        {
            _open = open;
            OpenChanged?.Invoke();
        }

        double HiddenShift => Gap + PanelHost.ActualHeight;

        void SetShift(double shift)
        {
            var scale = Native.Scale(_hwnd);
            PanelShift.Y = Math.Round(shift * scale) / scale;
        }

        void StartSlide(bool opening, double from)
        {
            _slide = new Slide { Opening = opening, From = from, Duration = opening ? OpenDuration : CloseDuration };
            SetShift(from);
            CompositionTarget.Rendering -= OnFrame;
            CompositionTarget.Rendering += OnFrame;
        }

        void OnFrame(object sender, EventArgs e)
        {
            var slide = _slide;
            if (slide == null)
            {
                StopSlide();
                return;
            }
            // Draw one frame out of sight first, so the panel never rises with a stale picture.
            if (slide.Opening && !slide.Rendered)
            {
                slide.Rendered = true;
                return;
            }
            if (!slide.Clock.IsRunning) slide.Clock.Start();
            var t = Math.Min(1.0, slide.Clock.Elapsed.TotalMilliseconds / slide.Duration.TotalMilliseconds);
            SetShift(slide.Opening
                ? Lerp(slide.From, 0, EaseOut(t))
                : Lerp(slide.From, HiddenShift, EaseIn(t)));

            if (t < 1) return;
            StopSlide();
            if (!slide.Opening) FinishHide();
        }

        void StopSlide()
        {
            CompositionTarget.Rendering -= OnFrame;
            _slide = null;
        }

        void FitToScreen()
        {
            if (_hwnd == IntPtr.Zero || !Native.TryGetTaskbar(out var taskbar, out var edge) || edge != Native.ABE_BOTTOM) return;
            var monitor = Native.MonitorBounds(Native.FindTaskbar());
            if (!monitor.HasValue) return;
            var scale = Native.Scale(_hwnd);
            var heightPx = taskbar.Top - 12 * scale + ShadowRoom * scale - monitor.Value.Top;
            Height = Math.Max(240, heightPx / scale);
        }

        // Sets the size too: a DPI change while parked off screen leaves the window at its old pixel size.
        void PlaceAt(int x, int y)
        {
            var scale = Native.Scale(_hwnd);
            Native.SetBoundsBelowTaskbar(_hwnd, x, y, (int)Math.Round(Width * scale), (int)Math.Round(Height * scale));
        }

        bool Layout(out int x, out int y, out bool slides)
        {
            x = y = 0;
            slides = false;
            if (_hwnd == IntPtr.Zero || !Native.TryGetTaskbar(out var taskbar, out var edge)) return false;
            var scale = Native.Scale(_hwnd);
            var margin = (int)Math.Round(Gap * scale);
            var room = (int)Math.Round(ShadowRoom * scale);
            var windowHeight = (int)Math.Round(Height * scale);
            var panelWidth = (int)Math.Round(PanelHost.Width * scale);
            var panelHeight = (int)Math.Round(PanelHost.ActualHeight * scale);

            switch (edge)
            {
                case Native.ABE_TOP:
                    x = taskbar.Right - margin - panelWidth - room;
                    y = taskbar.Bottom + margin - (windowHeight - room - panelHeight);
                    break;
                case Native.ABE_LEFT:
                    x = taskbar.Right + margin - room;
                    y = taskbar.Bottom - margin - (windowHeight - room);
                    break;
                case Native.ABE_RIGHT:
                    x = taskbar.Left - margin - panelWidth - room;
                    y = taskbar.Bottom - margin - (windowHeight - room);
                    break;
                default:
                    x = taskbar.Right - margin - panelWidth - room;
                    y = taskbar.Top - margin - (windowHeight - room);
                    slides = true;
                    break;
            }
            return true;
        }

        static double Lerp(double from, double to, double t) => from + (to - from) * t;

        static double EaseOut(double x)
        {
            var s = Math.Pow(x, 1.0 / 3.0);
            return 3 * s * s - 2 * s * s * s;
        }

        static double EaseIn(double x) => x * x;

        void SpinGear()
        {
            var spin = new DoubleAnimation(0, 120, TimeSpan.FromMilliseconds(500))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            spin.Completed += (s, e) =>
            {
                SettingsSpin.BeginAnimation(RotateTransform.AngleProperty, null);
                SettingsSpin.Angle = 0;
            };
            SettingsSpin.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        void ShowSettings(bool settings)
        {
            UsagePage.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
            SettingsPage.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        }

        void ApplyBackdrop()
        {
            var palette = Theme.Current;
            Panel.Background = palette.Surface;
            Panel.BorderBrush = palette.PanelStroke;
            Shadow.Background = palette.Surface;
            ShadowEffect.Opacity = palette.IsLight ? 0.18 : 0.45;
        }

        void Render()
        {
            var palette = Theme.Current;
            Foreground = palette.Text;

            UsagePage.Children.Clear();
            var services = _store.Snapshot;
            for (var i = 0; i < services.Count; i++) UsagePage.Children.Add(BuildGroup(services[i], palette, i == 0));

            SettingsTitle.Text = Text.Settings;
            SettingsTitle.Foreground = palette.Text;

            Updated.Text = Text.Updated(_store.LastUpdated);
            Updated.Foreground = palette.TextSecondary;
            Footer.Background = palette.Footer;
            Footer.BorderBrush = palette.FooterLine;

            foreach (var button in new[] { BackButton, RefreshButton, SettingsButton })
            {
                button.Foreground = palette.Text;
                button.Tag = palette.Hover;
            }
            BackButton.ToolTip = Visuals.Tip(Text.Back, palette);
            RefreshButton.ToolTip = Visuals.Tip(Text.Refresh, palette);
            SettingsButton.ToolTip = Visuals.Tip(Text.Settings, palette);
        }

        static FrameworkElement BuildGroup(ServiceUsage usage, Palette palette, bool first)
        {
            var group = new StackPanel();

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            header.Children.Add(Logos.Create(usage.Id, 16, palette.Text));
            var name = Visuals.Label(usage.Name, 14, palette.Text);
            name.Margin = new Thickness(10, 0, 0, 0);
            header.Children.Add(name);
            group.Children.Add(header);

            foreach (var w in usage.Windows.OrderBy(w => w.Length))
            {
                var percent = w.EffectivePercent;
                var row = new Grid { Height = 28 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 44, SharedSizeGroup = "WindowName" });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

                var windowName = Visuals.Label(Text.WindowName(w.Label), Visuals.Caption, palette.TextSecondary);
                windowName.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(windowName);

                var rail = Visuals.Rail(percent, double.NaN, palette);
                Grid.SetColumn(rail, 1);
                row.Children.Add(rail);

                var value = Visuals.Label($"{Math.Round(percent):0}%", Visuals.Caption, palette.Text);
                value.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(value, 2);
                row.Children.Add(value);

                var time = Visuals.Label(w.IsIdle ? "" : w.TimeText, Visuals.Caption, palette.TextSecondary);
                time.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(time, 3);
                row.Children.Add(time);

                group.Children.Add(row);
            }

            if (usage.Windows.Count == 0 || usage.Error != null)
            {
                var note = Visuals.Label(usage.Error ?? Text.Loading, Visuals.Caption, palette.TextSecondary);
                note.Margin = new Thickness(0, 4, 0, 0);
                note.TextWrapping = TextWrapping.Wrap;
                group.Children.Add(note);
            }
            return new Border
            {
                Child = group,
                Padding = new Thickness(24, first ? 18 : 14, 24, 14),
                BorderBrush = palette.Divider,
                BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0),
            };
        }

        static FrameworkElement Row(string label, FrameworkElement trailing, Palette palette) =>
            Row(Visuals.Label(label, 14, palette.Text), trailing);

        static FrameworkElement Row(FrameworkElement label, FrameworkElement trailing)
        {
            var grid = new Grid();
            label.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(label);
            if (trailing != null)
            {
                trailing.HorizontalAlignment = HorizontalAlignment.Right;
                trailing.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(trailing);
            }
            return grid;
        }
    }
}

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Cooldown
{
    public sealed class TaskbarChip
    {
        static readonly Brush Idle = Freeze(new SolidColorBrush(Color.FromArgb(1, 255, 255, 255))); // keeps the whole chip clickable

        readonly UsageStore _store;
        readonly FlyoutWindow _flyout;
        readonly Border _root;
        readonly StackPanel _items;
        readonly DispatcherTimer _watch;
        HwndSource _source;
        IntPtr _taskbar;
        bool _hovered;
        bool _pressed;
        string _shown;
        (int X, int Y)? _placed;

        public TaskbarChip(UsageStore store, FlyoutWindow flyout)
        {
            _store = store;
            _flyout = flyout;

            _items = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _root = new Border
            {
                Height = 40,
                Padding = new Thickness(7, 0, 7, 0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = Idle,
                Child = _items,
                UseLayoutRounding = true,
            };
            TextElement.SetFontFamily(_root, Visuals.UiFont);
            TextElement.SetFontSize(_root, Visuals.Caption);

            _root.MouseEnter += (s, e) => { _hovered = true; UpdateFill(); };
            _root.MouseLeave += (s, e) => { _hovered = false; _pressed = false; UpdateFill(); };
            _root.MouseLeftButtonDown += (s, e) => { _pressed = true; UpdateFill(); };
            _root.MouseLeftButtonUp += (s, e) =>
            {
                var wasPressed = _pressed;
                _pressed = false;
                UpdateFill();
                if (wasPressed) _flyout.Toggle();
            };
            _root.MouseRightButtonUp += (s, e) => _flyout.Toggle();
            _root.SizeChanged += (s, e) => Place();

            _store.Changed += () => Render();
            Theme.Changed += () => Render(force: true);
            Text.Changed += () =>
            {
                TextElement.SetFontFamily(_root, Visuals.UiFont);
                Render(force: true);
            };
            _flyout.OpenChanged += UpdateFill;

            _watch = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => EnsureAttached(), Dispatcher.CurrentDispatcher);
            Render();
        }

        public void Start()
        {
            EnsureAttached();
            _watch.Start();
        }

        void EnsureAttached()
        {
            var taskbar = Native.FindTaskbar();
            if (taskbar == IntPtr.Zero) return;

            var attached = _source != null && !_source.IsDisposed && taskbar == _taskbar && Native.IsWindow(_source.Handle);
            if (!attached)
            {
                Detach();
                _taskbar = taskbar;
                var parameters = new HwndSourceParameters("Cooldown")
                {
                    ParentWindow = taskbar,
                    WindowStyle = Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS,
                    UsesPerPixelTransparency = true,
                    Width = 1,
                    Height = 1,
                };
                _source = new HwndSource(parameters) { SizeToContent = SizeToContent.WidthAndHeight, RootVisual = _root };
                _placed = null;
            }
            Place();

            // MouseLeave can be missed when the pointer jumps straight off the chip.
            if (_hovered && !Native.CursorInside(_source.Handle))
            {
                _hovered = false;
                UpdateFill();
            }
        }

        void Detach()
        {
            if (_source == null) return;
            try
            {
                if (!_source.IsDisposed)
                {
                    _source.RootVisual = null;
                    _source.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Info("chip", "detach: " + ex.Message);
            }
            _source = null;
        }

        void Place()
        {
            if (_source == null || _source.IsDisposed || !Native.GetWindowRect(_taskbar, out var taskbar)) return;
            var hwnd = _source.Handle;
            var scale = Native.Scale(hwnd);
            var width = (int)Math.Round(_root.ActualWidth * scale);
            var height = (int)Math.Round(_root.ActualHeight * scale);
            if (width == 0 || height == 0) return;

            var tray = Native.GetTrayRect();
            int x, y;
            if (taskbar.Width >= taskbar.Height)
            {
                var right = tray.HasValue && tray.Value.Left > taskbar.Left ? tray.Value.Left - taskbar.Left : taskbar.Width - (int)(260 * scale);
                x = right - width;
                y = (taskbar.Height - height) / 2;
            }
            else
            {
                var bottom = tray.HasValue && tray.Value.Top > taskbar.Top ? tray.Value.Top - taskbar.Top : taskbar.Height - (int)(260 * scale);
                x = (taskbar.Width - width) / 2;
                y = bottom - height;
            }
            if (_placed == (x, y) && Native.IsTopChild(hwnd)) return;
            Native.PlaceChildOnTop(hwnd, x, y);
            _placed = (x, y);
        }

        void Render(bool force = false)
        {
            var palette = Theme.Current;
            var services = _store.Snapshot;
            var shown = string.Join("|", services.Select(Describe));
            if (!force && shown == _shown) return;
            _shown = shown;

            _items.Children.Clear();
            for (var i = 0; i < services.Count; i++)
            {
                _items.Children.Add(BuildService(services[i], palette, i == 0 ? 0 : 12));
            }
            UpdateFill();
        }

        static string Describe(ServiceUsage usage) =>
            usage.Id + ";" + usage.Name + ";" + usage.Error + ";" + string.Join(",", usage.ChipWindows.Select(w => $"{Math.Round(w.EffectivePercent, 1)}/{w.TimeText}/{w.IsIdle}"));

        static FrameworkElement BuildService(ServiceUsage usage, Palette palette, double leftMargin)
        {
            var grid = new Grid
            {
                Margin = new Thickness(leftMargin, 0, 0, 0),
                ToolTip = Visuals.TaskbarTip(usage.Error == null ? usage.Name : $"{usage.Name} · {usage.Error}", palette, 8),
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });

            var logo = Logos.Create(usage.Id, 14, palette.Text);
            logo.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRowSpan(logo, 2);
            grid.Children.Add(logo);

            var windows = usage.ChipWindows;
            if (windows.Count == 0)
            {
                AddRow(grid, 0, 2, 0, "—", palette.TextSecondary, palette);
            }
            else
            {
                for (var r = 0; r < windows.Count; r++)
                {
                    var w = windows[r];
                    AddRow(grid, r, windows.Count == 1 ? 2 : 1, w.EffectivePercent, w.TimeText,
                        w.IsIdle ? palette.TextSecondary : palette.Text, palette);
                }
            }
            return grid;
        }

        static void AddRow(Grid grid, int row, int rowSpan, double percent, string time, Brush timeBrush, Palette palette)
        {
            var bar = Visuals.Bar(percent, 28, palette);
            Grid.SetRow(bar, row);
            Grid.SetRowSpan(bar, rowSpan);
            Grid.SetColumn(bar, 2);
            grid.Children.Add(bar);

            var label = Visuals.Label(time, Visuals.Caption, timeBrush);
            label.MinWidth = 40;
            Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
            Grid.SetRow(label, row);
            Grid.SetRowSpan(label, rowSpan);
            Grid.SetColumn(label, 4);
            grid.Children.Add(label);
        }

        void UpdateFill()
        {
            var palette = Theme.Current;
            var active = _hovered || _pressed || _flyout.IsOpen;
            _root.Background = _pressed ? palette.TaskbarPressed : active ? palette.TaskbarHover : Idle;
            _root.BorderBrush = active ? palette.TaskbarEdge : Brushes.Transparent;
            _items.Opacity = _pressed ? palette.TaskbarPressedOpacity : 1.0;
        }

        static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}

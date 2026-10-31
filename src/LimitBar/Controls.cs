using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LimitBar
{
    static class Motion
    {
        public static readonly TimeSpan Faster = TimeSpan.FromMilliseconds(83);
        public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(167);
        public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(250);
        public static readonly TimeSpan Expand = TimeSpan.FromMilliseconds(333);

        public static KeySpline FastOutSlowIn => new KeySpline(0, 0, 0, 1);
        public static KeySpline Accelerate => new KeySpline(1, 1, 0, 1);

        public static void Spline(IAnimatable target, DependencyProperty property, double to, TimeSpan duration, KeySpline spline, Action done = null)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
            if (done != null) animation.Completed += (s, e) => done();
            target.BeginAnimation(property, animation);
        }

        public static void Linear(IAnimatable target, DependencyProperty property, double to, TimeSpan duration) =>
            target.BeginAnimation(property, new DoubleAnimation(to, duration));

        public static void Set(IAnimatable target, DependencyProperty property, double value)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, value);
        }
    }

    sealed class ToggleVisual : Grid
    {
        const double Travel = 20;

        readonly Palette _palette;
        readonly Border _offTrack;
        readonly Border _onTrack;
        readonly Rectangle _knobOff;
        readonly Rectangle _knobOn;
        readonly TranslateTransform _move = new TranslateTransform();

        public bool IsOn { get; private set; }

        public ToggleVisual(bool on, Palette palette)
        {
            _palette = palette;
            IsOn = on;
            Width = 40;
            Height = 20;
            VerticalAlignment = VerticalAlignment.Center;

            _offTrack = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = palette.Track,
                Background = palette.ToggleOffFill,
                Opacity = on ? 0 : 1,
            };
            _onTrack = new Border { CornerRadius = new CornerRadius(10), Background = palette.Normal, Opacity = on ? 1 : 0 };
            _knobOff = Knob(palette.TextSecondary, on ? 0 : 1);
            _knobOn = Knob(palette.OnAccent, on ? 1 : 0);
            var knob = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _move };
            knob.Children.Add(_knobOff);
            knob.Children.Add(_knobOn);
            _move.X = on ? Travel : 0;

            Children.Add(_offTrack);
            Children.Add(_onTrack);
            Children.Add(knob);
        }

        static Rectangle Knob(Brush fill, double opacity) => new Rectangle
        {
            Width = 12,
            Height = 12,
            RadiusX = 7,
            RadiusY = 7,
            Fill = fill,
            Opacity = opacity,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        public void SetOn(bool on, bool animate)
        {
            if (on == IsOn) return;
            IsOn = on;
            if (!animate)
            {
                Motion.Set(_move, TranslateTransform.XProperty, on ? Travel : 0);
                foreach (var e in new UIElement[] { _onTrack, _knobOn }) Motion.Set(e, OpacityProperty, on ? 1 : 0);
                foreach (var e in new UIElement[] { _offTrack, _knobOff }) Motion.Set(e, OpacityProperty, on ? 0 : 1);
                return;
            }
            Motion.Spline(_move, TranslateTransform.XProperty, on ? Travel : 0, Motion.Normal, Motion.FastOutSlowIn);
            foreach (var e in new UIElement[] { _onTrack, _knobOn }) Motion.Linear(e, OpacityProperty, on ? 1 : 0, Motion.Faster);
            foreach (var e in new UIElement[] { _offTrack, _knobOff }) Motion.Linear(e, OpacityProperty, on ? 0 : 1, Motion.Faster);
        }

        public void SetPointer(bool over, bool pressed)
        {
            double width = pressed ? 17 : over ? 14 : 12, height = pressed || over ? 14 : 12;
            foreach (var knob in new[] { _knobOff, _knobOn })
            {
                Motion.Spline(knob, WidthProperty, width, Motion.Faster, Motion.FastOutSlowIn);
                Motion.Spline(knob, HeightProperty, height, Motion.Faster, Motion.FastOutSlowIn);
            }
            _offTrack.Background = pressed ? _palette.ToggleOffFillPressed : over ? _palette.ToggleOffFillHover : _palette.ToggleOffFill;
            _onTrack.Background = pressed ? _palette.NormalPressed : over ? _palette.NormalHover : _palette.Normal;
        }
    }

    sealed class RadioVisual : Grid
    {
        readonly Palette _palette;
        readonly Ellipse _outer;
        readonly Ellipse _dot;
        bool _checked;

        public RadioVisual(bool isChecked, Palette palette)
        {
            _palette = palette;
            Width = 20;
            Height = 20;
            VerticalAlignment = VerticalAlignment.Center;
            _outer = new Ellipse { StrokeThickness = 1 };
            _dot = new Ellipse { Width = 12, Height = 12, Fill = palette.OnAccent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Children.Add(_outer);
            Children.Add(_dot);
            SetChecked(isChecked);
        }

        public void SetChecked(bool isChecked)
        {
            _checked = isChecked;
            _outer.Fill = isChecked ? _palette.Normal : _palette.ToggleOffFill;
            _outer.Stroke = isChecked ? null : _palette.Track;
            _dot.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetPointer(bool over, bool pressed)
        {
            var size = pressed ? 10 : over ? 14 : 12;
            Motion.Spline(_dot, WidthProperty, size, Motion.Faster, Motion.FastOutSlowIn);
            Motion.Spline(_dot, HeightProperty, size, Motion.Faster, Motion.FastOutSlowIn);
            if (_checked) _outer.Fill = pressed ? _palette.NormalPressed : over ? _palette.NormalHover : _palette.Normal;
        }
    }

    sealed class Drawer
    {
        readonly Border _clip;
        readonly StackPanel _options;
        readonly TranslateTransform _slide = new TranslateTransform();
        readonly RotateTransform _chevron = new RotateTransform();
        readonly TextBlock _value;
        readonly string[] _labels;
        readonly List<RadioVisual> _radios = new List<RadioVisual>();

        public StackPanel Root { get; }
        public bool IsExpanded { get; private set; }
        public event Action<int> Selected;

        public Drawer(string title, string[] labels, int selected, Palette palette, Style rowStyle)
        {
            _labels = labels;
            Root = new StackPanel();

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            header.Children.Add(Visuals.Label(title, 14, palette.Text));
            _value = Visuals.Label(labels[selected], 14, palette.TextSecondary);
            _value.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(_value, 1);
            header.Children.Add(_value);
            var chevron = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = 12,
                Foreground = palette.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _chevron,
            };
            Grid.SetColumn(chevron, 2);
            header.Children.Add(chevron);
            var headerButton = new Button { Style = rowStyle, Margin = new Thickness(-12, 0, -12, 0), Content = header, Foreground = palette.Text, Tag = palette.Hover };
            headerButton.Click += (s, e) => SetExpanded(!IsExpanded);
            Root.Children.Add(headerButton);

            _options = new StackPanel { RenderTransform = _slide };
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var radio = new RadioVisual(i == selected, palette);
                _radios.Add(radio);
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(radio);
                var label = Visuals.Label(labels[i], 14, palette.Text);
                label.Margin = new Thickness(12, 0, 0, 0);
                content.Children.Add(label);
                var row = new Button { Style = rowStyle, Content = content, Foreground = palette.Text, Tag = palette.Hover };
                row.Click += (s, e) => Selected?.Invoke(index);
                row.MouseEnter += (s, e) => radio.SetPointer(true, false);
                row.MouseLeave += (s, e) => radio.SetPointer(false, false);
                row.PreviewMouseLeftButtonDown += (s, e) => radio.SetPointer(true, true);
                row.PreviewMouseLeftButtonUp += (s, e) => radio.SetPointer(true, false);
                _options.Children.Add(row);
            }
            _clip = new Border { ClipToBounds = true, Margin = new Thickness(-12, 0, -12, 0), Child = _options, Visibility = Visibility.Collapsed };
            Root.Children.Add(_clip);
        }

        public void SetSelected(int index)
        {
            for (var i = 0; i < _radios.Count; i++) _radios[i].SetChecked(i == index);
            _value.Text = _labels[index];
        }

        public void SetExpanded(bool expanded, bool animate = true)
        {
            if (expanded == IsExpanded) return;
            IsExpanded = expanded;
            _options.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var height = _options.DesiredSize.Height;

            if (!animate)
            {
                Motion.Set(_clip, FrameworkElement.HeightProperty, double.NaN);
                Motion.Set(_slide, TranslateTransform.YProperty, 0);
                Motion.Set(_chevron, RotateTransform.AngleProperty, expanded ? 180 : 0);
                _clip.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            Motion.Spline(_chevron, RotateTransform.AngleProperty, expanded ? 180 : 0, Motion.Fast, Motion.FastOutSlowIn);
            if (expanded)
            {
                _clip.Visibility = Visibility.Visible;
                Motion.Set(_clip, FrameworkElement.HeightProperty, 0);
                Motion.Set(_slide, TranslateTransform.YProperty, -height);
                Motion.Spline(_clip, FrameworkElement.HeightProperty, height, Motion.Expand, Motion.FastOutSlowIn,
                    () => Motion.Set(_clip, FrameworkElement.HeightProperty, double.NaN));
                Motion.Spline(_slide, TranslateTransform.YProperty, 0, Motion.Expand, Motion.FastOutSlowIn);
            }
            else
            {
                Motion.Set(_clip, FrameworkElement.HeightProperty, _clip.ActualHeight);
                Motion.Spline(_clip, FrameworkElement.HeightProperty, 0, Motion.Fast, Motion.Accelerate, () =>
                {
                    _clip.Visibility = Visibility.Collapsed;
                    Motion.Set(_clip, FrameworkElement.HeightProperty, double.NaN);
                    Motion.Set(_slide, TranslateTransform.YProperty, 0);
                });
                Motion.Spline(_slide, TranslateTransform.YProperty, -height, Motion.Fast, Motion.Accelerate);
            }
        }
    }

    // A settings row with a WinUI ComboBox: the list opens over the button with the chosen item where the button was.
    sealed class Choice
    {
        const double Row = 36, Item = 32, Pad = 4, ListMargin = 8;

        readonly string[] _labels;
        readonly Palette _palette;
        readonly Canvas _overlay;
        readonly Func<double> _bottomLimit;
        readonly Border _button;
        readonly TextBlock _value;
        readonly List<UIElement> _shown = new List<UIElement>();
        int _selected;
        bool _hovered;

        public Grid Root { get; }
        public bool IsOpen => _shown.Count > 0;
        public event Action<int> Selected;

        public Choice(string title, string[] labels, int selected, Palette palette, Canvas overlay, Func<double> bottomLimit)
        {
            _labels = labels;
            _palette = palette;
            _overlay = overlay;
            _bottomLimit = bottomLimit;
            _selected = selected;

            _value = Visuals.Label(labels[selected], 14, palette.Text);
            _value.VerticalAlignment = VerticalAlignment.Center;
            var chevron = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = 12,
                Foreground = palette.Text,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 14, 0),
            };
            var content = new Grid();
            content.Children.Add(_value);
            content.Children.Add(chevron);
            _button = new Border
            {
                Height = Item,
                MinWidth = labels.Max(Width) + 11 + 38,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = palette.ControlEdge,
                Background = palette.ControlFill,
                Padding = new Thickness(11, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Child = content,
            };
            _button.MouseEnter += (s, e) => { _hovered = true; _button.Background = palette.ControlFillHover; };
            _button.MouseLeave += (s, e) => { _hovered = false; _button.Background = palette.ControlFill; };
            _button.MouseLeftButtonDown += (s, e) => { _button.Background = palette.ControlFillPressed; e.Handled = true; };
            _button.MouseLeftButtonUp += (s, e) =>
            {
                _button.Background = _hovered ? palette.ControlFillHover : palette.ControlFill;
                Open();
                e.Handled = true;
            };

            Root = new Grid { Height = Row };
            var label = Visuals.Label(title, 14, palette.Text);
            label.VerticalAlignment = VerticalAlignment.Center;
            Root.Children.Add(label);
            Root.Children.Add(_button);
        }

        static double Width(string text)
        {
            var block = Visuals.Label(text, 14, Brushes.Black);
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Ceiling(block.DesiredSize.Width);
        }

        public void SetSelected(int index)
        {
            _selected = index;
            _value.Text = _labels[index];
        }

        public void Close()
        {
            foreach (var element in _shown) _overlay.Children.Remove(element);
            _shown.Clear();
        }

        void Open()
        {
            if (IsOpen) return;
            var origin = _button.TranslatePoint(new Point(0, 0), _overlay);
            var rows = new StackPanel { Margin = new Thickness(0, Pad, 0, Pad) };
            for (var i = 0; i < _labels.Length; i++) rows.Children.Add(ItemFor(i));

            var width = Math.Max(_button.ActualWidth + 8, _labels.Max(Width) + 2 * (5 + 11) + 2);
            var full = 2 + 2 * Pad + _labels.Length * (Item + 4);
            var height = Math.Min(full, _bottomLimit() - 2 * ListMargin);
            var rowTop = 1 + Pad + _selected * (Item + 4) + 2;
            var top = Math.Max(ListMargin, Math.Min(origin.Y - rowTop, _bottomLimit() - ListMargin - height));
            var left = origin.X - 4;

            var dismiss = new Rectangle { Width = _overlay.ActualWidth, Height = _overlay.ActualHeight, Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            dismiss.MouseDown += (s, e) => { Close(); e.Handled = true; };

            var shadow = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(8),
                Background = _palette.Dropdown,
                Opacity = 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 32, ShadowDepth = 8, Direction = 270, Opacity = _palette.IsLight ? 0.14 : 0.37 },
            };
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = rows };
            var list = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = _palette.SurfaceStroke,
                Background = _palette.Dropdown,
                Child = scroller,
            };
            foreach (var element in new FrameworkElement[] { shadow, list })
            {
                Canvas.SetLeft(element, left);
                Canvas.SetTop(element, top);
            }
            foreach (var element in new UIElement[] { dismiss, shadow, list })
            {
                _overlay.Children.Add(element);
                _shown.Add(element);
            }
            if (height < full) scroller.ScrollToVerticalOffset(Math.Max(0, rowTop - (origin.Y - top)));

            // Opens out of the chosen row, as WinUI's drop-down does.
            var reveal = new RectangleGeometry(new Rect(0, Math.Max(0, origin.Y - top), width, Item + 4));
            list.Clip = reveal;
            var grow = new RectAnimationUsingKeyFrames();
            grow.KeyFrames.Add(new SplineRectKeyFrame(new Rect(0, 0, width, height), KeyTime.FromTimeSpan(Motion.Normal), Motion.FastOutSlowIn));
            grow.Completed += (s, e) => list.Clip = null;
            reveal.BeginAnimation(RectangleGeometry.RectProperty, grow);
            Motion.Linear(shadow, UIElement.OpacityProperty, 1, Motion.Normal);
        }

        FrameworkElement ItemFor(int index)
        {
            var chosen = index == _selected;
            var text = Visuals.Label(_labels[index], 14, _palette.Text);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(11, 0, 11, 0);
            var pill = new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(1.5),
                Background = _palette.Normal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = chosen ? Visibility.Visible : Visibility.Collapsed,
            };
            var grid = new Grid();
            grid.Children.Add(text);
            grid.Children.Add(pill);
            Brush rest = chosen ? _palette.Hover : Brushes.Transparent, over = chosen ? _palette.Pressed : _palette.Hover;
            var item = new Border { Height = Item, Margin = new Thickness(5, 2, 5, 2), CornerRadius = new CornerRadius(4), Background = rest, Child = grid };
            item.MouseEnter += (s, e) => item.Background = over;
            item.MouseLeave += (s, e) => item.Background = rest;
            item.MouseLeftButtonUp += (s, e) =>
            {
                Close();
                e.Handled = true;
                if (index == _selected) return;
                SetSelected(index);
                Selected?.Invoke(index);
            };
            return item;
        }
    }
}

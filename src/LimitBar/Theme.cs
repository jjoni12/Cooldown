using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LimitBar
{
    public sealed class Palette
    {
        public bool IsLight;
        public Brush Text;
        public Brush TextSecondary;
        public Brush Track;
        public Brush Normal;
        public Brush Caution;
        public Brush Critical;
        public Brush Hover;
        public Brush TaskbarHover;
        public Brush TaskbarPressed;
        public Brush TaskbarEdge;
        public double TaskbarPressedOpacity;
        public Brush Tooltip;
        public Brush Surface;
        public Brush PanelStroke;
        public Brush SurfaceStroke;
        public Brush Divider;
        public Brush Footer;
        public Brush FooterLine;
        public Brush OnAccent;
        public Brush ToggleOffFill;
        public Brush ToggleOffFillHover;
        public Brush ToggleOffFillPressed;
        public Brush NormalHover;
        public Brush NormalPressed;
        public Brush Pressed;
        public Brush ControlFill;
        public Brush ControlFillHover;
        public Brush ControlFillPressed;
        public Brush ControlEdge;
        public Brush Dropdown;

        public Brush ForUsage(double percent) => percent >= 90 ? Critical : percent >= 70 ? Caution : Normal;

        public static readonly Palette Dark = new Palette
        {
            IsLight = false,
            Text = B("#FFFFFFFF"),
            TextSecondary = B("#C5FFFFFF"),
            Track = B("#8BFFFFFF"),
            Normal = B("#FF4CC2FF"),
            Caution = B("#FFFCE100"),
            Critical = B("#FFFF4343"),
            Hover = B("#0FFFFFFF"),
            TaskbarHover = B("#0FFFFFFF"),
            TaskbarPressed = B("#0AFFFFFF"),
            TaskbarEdge = Edge("#18FFFFFF", "#0FFFFFFF", "#0FFFFFFF"),
            TaskbarPressedOpacity = 0.77,
            Tooltip = B("#FF2E2E2E"),
            Surface = B("#FF292929"),
            PanelStroke = B("#FF3D3D3D"),
            SurfaceStroke = B("#33000000"),
            Divider = B("#FF3B3B3B"),
            Footer = B("#FF202020"),
            FooterLine = B("#FF1C1C1C"),
            OnAccent = B("#FF000000"),
            ToggleOffFill = B("#19000000"),
            ToggleOffFillHover = B("#0BFFFFFF"),
            ToggleOffFillPressed = B("#12FFFFFF"),
            NormalHover = B("#E64CC2FF"),
            NormalPressed = B("#CC4CC2FF"),
            Pressed = B("#0AFFFFFF"),
            ControlFill = B("#0FFFFFFF"),
            ControlFillHover = B("#15FFFFFF"),
            ControlFillPressed = B("#08FFFFFF"),
            ControlEdge = Edge("#12FFFFFF", "#12FFFFFF", "#18FFFFFF", 32),
            Dropdown = B("#FF2C2C2C"),
        };

        public static readonly Palette Light = new Palette
        {
            IsLight = true,
            Text = B("#E4000000"),
            TextSecondary = B("#9E000000"),
            Track = B("#72000000"),
            Normal = B("#FF0067C0"),
            Caution = B("#FF9D5D00"),
            Critical = B("#FFC42B1C"),
            Hover = B("#09000000"),
            TaskbarHover = B("#80FFFFFF"),
            TaskbarPressed = B("#4DFFFFFF"),
            TaskbarEdge = Edge("#06000000", "#06000000", "#0F000000"),
            TaskbarPressedOpacity = 0.70,
            Tooltip = B("#FFFAFAFA"),
            Surface = B("#FFF5F5F5"),
            PanelStroke = B("#FFB1B1B1"),
            SurfaceStroke = B("#0F000000"),
            Divider = B("#FFE7E7E7"),
            Footer = B("#FFF2F2F2"),
            FooterLine = B("#FFE2E2E2"),
            OnAccent = B("#FFFFFFFF"),
            ToggleOffFill = B("#06000000"),
            ToggleOffFillHover = B("#0F000000"),
            ToggleOffFillPressed = B("#18000000"),
            NormalHover = B("#E60067C0"),
            NormalPressed = B("#CC0067C0"),
            Pressed = B("#06000000"),
            ControlFill = B("#B3FFFFFF"),
            ControlFillHover = B("#80F9F9F9"),
            ControlFillPressed = B("#4DF9F9F9"),
            ControlEdge = Edge("#0F000000", "#0F000000", "#29000000", 32),
            Dropdown = B("#FFF9F9F9"),
        };

        static Brush B(string argb)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(argb));
            brush.Freeze();
            return brush;
        }

        static Brush Edge(string top, string side, string bottom, double height = 40)
        {
            Color C(string argb) => (Color)ColorConverter.ConvertFromString(argb);
            var row = 1.0 / height;
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            brush.GradientStops.Add(new GradientStop(C(top), 0));
            brush.GradientStops.Add(new GradientStop(C(top), row));
            brush.GradientStops.Add(new GradientStop(C(side), row));
            brush.GradientStops.Add(new GradientStop(C(side), 1 - row));
            brush.GradientStops.Add(new GradientStop(C(bottom), 1 - row));
            brush.GradientStops.Add(new GradientStop(C(bottom), 1));
            brush.Freeze();
            return brush;
        }
    }

    static class Theme
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static Palette Current { get; private set; } = Read();
        public static event Action Changed;

        static Theme()
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category != UserPreferenceCategory.General) return;
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var next = Read();
                    if (next.IsLight == Current.IsLight) return;
                    Current = next;
                    Changed?.Invoke();
                }));
            };
        }

        static Palette Read()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Key))
            {
                return key?.GetValue("SystemUsesLightTheme") is int v && v == 1 ? Palette.Light : Palette.Dark;
            }
        }
    }
}

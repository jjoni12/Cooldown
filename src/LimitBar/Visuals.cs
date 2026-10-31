using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LimitBar
{
    static class Visuals
    {
        public static FontFamily UiFont { get; private set; } = PickUiFont();

        public static void UpdateFont() => UiFont = PickUiFont();

        public const double Caption = 12;

        static FontFamily PickUiFont()
        {
            var language = Text.Language;
            string font;
            if (language.StartsWith("ko")) font = "Malgun Gothic";
            else if (language.StartsWith("ja")) font = "Yu Gothic UI";
            else if (language.StartsWith("zh-Hant") || language == "zh-TW" || language == "zh-HK" || language == "zh-MO") font = "Microsoft JhengHei UI";
            else if (language.StartsWith("zh")) font = "Microsoft YaHei UI";
            else font = "Segoe UI Variable Text";
            return new FontFamily($"{font}, Segoe UI Variable Text, Segoe UI, Malgun Gothic");
        }

        public static FrameworkElement Bar(double percent, double width, Palette palette) =>
            Split(percent, width, 3, palette, thinTrack: true);

        public static FrameworkElement Rail(double percent, double width, Palette palette) =>
            Split(percent, width, 4, palette, thinTrack: false);

        static FrameworkElement Split(double percent, double width, double height, Palette palette, bool thinTrack)
        {
            var value = Math.Max(0, Math.Min(100, percent));
            var grid = new Grid { Height = height, Width = width, SnapsToDevicePixels = true, VerticalAlignment = VerticalAlignment.Center };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - value, GridUnitType.Star) });

            var track = new Border
            {
                Height = thinTrack ? 1 : height,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(thinTrack ? 0.5 : height / 2),
                Background = palette.Track,
            };
            Grid.SetColumnSpan(track, 2);
            grid.Children.Add(track);

            if (value > 0)
            {
                grid.Children.Add(new Border
                {
                    Height = height,
                    CornerRadius = new CornerRadius(height / 2),
                    Background = palette.ForUsage(value),
                });
            }
            return grid;
        }

        public static TextBlock Label(string text, double size, Brush foreground) => new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = UiFont,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };

        public static ToolTip Tip(string text, Palette palette) => new ToolTip
        {
            Content = text,
            Background = palette.Tooltip,
            Foreground = palette.Text,
            BorderBrush = palette.SurfaceStroke,
        };

        public static ToolTip TaskbarTip(string text, Palette palette, double aboveTarget)
        {
            var tip = Tip(text, palette);
            tip.Placement = PlacementMode.Custom;
            tip.CustomPopupPlacementCallback = (popup, target, offset) => new[]
            {
                new CustomPopupPlacement(
                    new Point((target.Width - popup.Width) / 2, -popup.Height - aboveTarget - 10),
                    PopupPrimaryAxis.Horizontal),
            };
            return tip;
        }
    }
}

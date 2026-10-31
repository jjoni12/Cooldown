using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using WpfPath = System.Windows.Shapes.Path;

namespace LimitBar
{
    static class Logos
    {
        sealed class Logo
        {
            public Geometry Geometry;
            public Rect ViewBox;
        }

        static readonly Dictionary<string, Logo> Cache = new Dictionary<string, Logo>();

        public static FrameworkElement Create(string id, double size, Brush fill)
        {
            var logo = Load(id);
            if (logo == null)
            {
                return new TextBlock
                {
                    Text = id.Substring(0, 1).ToUpperInvariant(),
                    FontSize = size * 0.85,
                    Foreground = fill,
                    Width = size,
                    TextAlignment = TextAlignment.Center,
                };
            }

            var path = new WpfPath
            {
                Data = logo.Geometry,
                Fill = fill,
                RenderTransform = new TranslateTransform(-logo.ViewBox.X, -logo.ViewBox.Y),
            };
            var canvas = new Canvas { Width = logo.ViewBox.Width, Height = logo.ViewBox.Height };
            canvas.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = canvas };
        }

        static Logo Load(string id)
        {
            if (Cache.TryGetValue(id, out var cached)) return cached;
            Logo logo = null;
            try
            {
                var svg = ReadSvg(id);
                if (svg != null) logo = Parse(svg);
            }
            catch (Exception ex)
            {
                Log.Error("logo", $"{id}: {ex.Message}");
            }
            Cache[id] = logo;
            return logo;
        }

        static string ReadSvg(string id)
        {
            foreach (var dir in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LimitBar", "logos"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logos"),
            })
            {
                var file = Path.Combine(dir, id + ".svg");
                if (File.Exists(file)) return File.ReadAllText(file);
            }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"logos.{id}.svg"))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        static Logo Parse(string svg)
        {
            var root = XDocument.Parse(svg).Root;
            var viewBox = ParseViewBox((string)root.Attribute("viewBox")) ?? new Rect(0, 0, 24, 24);
            var rootRule = (string)root.Attribute("fill-rule");

            var group = new GeometryGroup();
            foreach (var element in root.Descendants().Where(e => e.Name.LocalName == "path"))
            {
                var d = (string)element.Attribute("d");
                if (string.IsNullOrWhiteSpace(d)) continue;
                var rule = (string)element.Attribute("fill-rule") ?? rootRule;
                group.Children.Add(Geometry.Parse((rule == "evenodd" ? "F0 " : "F1 ") + NormalizePath(d)));
            }
            if (group.Children.Count == 0) return null;
            group.Freeze();
            return new Logo { Geometry = group, ViewBox = viewBox };
        }

        static Rect? ParseViewBox(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var parts = value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            return parts.Length == 4 ? new Rect(parts[0], parts[1], parts[2], parts[3]) : (Rect?)null;
        }

        static string NormalizePath(string d)
        {
            var sb = new StringBuilder(d.Length + 64);
            var command = ' ';
            var argIndex = 0;
            var i = 0;
            while (i < d.Length)
            {
                var c = d[i];
                if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                    continue;
                }
                if (char.IsLetter(c) && c != 'e' && c != 'E')
                {
                    command = c;
                    argIndex = 0;
                    sb.Append(c).Append(' ');
                    i++;
                    continue;
                }
                if ((command == 'a' || command == 'A') && (argIndex % 7 == 3 || argIndex % 7 == 4))
                {
                    sb.Append(c).Append(' ');
                    i++;
                    argIndex++;
                    continue;
                }

                var start = i;
                if (c == '+' || c == '-') i++;
                var seenDot = false;
                while (i < d.Length)
                {
                    var ch = d[i];
                    if (char.IsDigit(ch))
                    {
                        i++;
                    }
                    else if (ch == '.' && !seenDot)
                    {
                        seenDot = true;
                        i++;
                    }
                    else if (ch == 'e' || ch == 'E')
                    {
                        i++;
                        if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
                    }
                    else
                    {
                        break;
                    }
                }
                if (i == start) i++; // skip an unexpected character instead of looping forever
                sb.Append(d, start, i - start).Append(' ');
                argIndex++;
            }
            return sb.ToString();
        }
    }
}

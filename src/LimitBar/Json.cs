using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace LimitBar
{
    static class Json
    {
        static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        public static object Parse(string text) => Serializer.DeserializeObject(text);

        public static object Get(object node, params string[] path)
        {
            foreach (var key in path)
            {
                if (!(node is IDictionary<string, object> dict) || !dict.TryGetValue(key, out node)) return null;
            }
            return node;
        }

        public static string Str(object node) => node as string;

        public static double? Num(object node)
        {
            switch (node)
            {
                case null:
                case bool _:
                    return null;
                case string s:
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
                default:
                    return Convert.ToDouble(node, CultureInfo.InvariantCulture);
            }
        }

        public static IEnumerable<object> Arr(object node) =>
            node is IEnumerable list && !(node is string) && !(node is IDictionary<string, object>)
                ? list.Cast<object>()
                : Enumerable.Empty<object>();

        public static string ExtractObject(string text)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text.Substring(start, end - start + 1) : null;
        }

        public static DateTimeOffset? Time(object node)
        {
            if (node is string s && !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
                    ? t
                    : (DateTimeOffset?)null;
            }
            var value = Num(node);
            if (!value.HasValue || value.Value <= 0) return null;
            return value.Value > 1e12
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)value.Value)
                : DateTimeOffset.FromUnixTimeSeconds((long)value.Value);
        }
    }
}

using System.Collections;
using System.Globalization;
using System.Reflection;

namespace ServiceLib.Satra;

/// <summary>
/// Read/write a config value by dotted path ("TunModeItem.Mtu", "Inbound[0].UdpEnabled") with text conversion,
/// so Satra's Advanced settings can be declared as a table instead of hand-wiring every field.
/// Lists of strings are shown as comma-separated text.
/// </summary>
public static class ConfigPath
{
    public enum Kind { Bool, Number, Text, List, Unsupported }

    private static (object? owner, PropertyInfo? prop) Resolve(object root, string path, bool create)
    {
        object? cur = root;
        var parts = path.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            if (cur == null) return (null, null);
            var name = parts[i];
            int? idx = null;
            var b = name.IndexOf('[');
            if (b > 0) { idx = int.Parse(name[(b + 1)..name.IndexOf(']')], CultureInfo.InvariantCulture); name = name[..b]; }
            var p = cur.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return (null, null);
            if (i == parts.Length - 1 && idx == null) return (cur, p);
            var next = p.GetValue(cur);
            if (next == null && create && idx == null && p.PropertyType.GetConstructor(Type.EmptyTypes) != null)
            {
                next = Activator.CreateInstance(p.PropertyType);
                p.SetValue(cur, next);
            }
            if (idx != null) next = next is IList l && idx < l.Count ? l[idx.Value] : null;
            cur = next;
        }
        return (null, null);
    }

    public static Kind KindOf(object root, string path)
    {
        var (_, p) = Resolve(root, path, false);
        if (p == null) return Kind.Unsupported;
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        if (t == typeof(bool)) return Kind.Bool;
        if (t == typeof(int) || t == typeof(long) || t == typeof(double)) return Kind.Number;
        if (t == typeof(string)) return Kind.Text;
        if (typeof(IList<string>).IsAssignableFrom(t)) return Kind.List;
        return Kind.Unsupported;
    }

    public static string Get(object root, string path)
    {
        var (o, p) = Resolve(root, path, false);
        if (o == null || p == null) return "";
        var v = p.GetValue(o);
        return v switch
        {
            null => "",
            bool x => x ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            IEnumerable<string> l => string.Join(", ", l),
            _ => v.ToString() ?? "",
        };
    }

    public static bool GetBool(object root, string path) => Get(root, path) == "true";

    /// <summary>Sets the value from text. Returns false (and changes nothing) when the text doesn't fit the type or range.</summary>
    public static bool Set(object root, string path, string text, double? min = null, double? max = null)
    {
        var (o, p) = Resolve(root, path, true);
        if (o == null || p == null || !p.CanWrite) return false;
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        object? val;
        text = text.Trim();
        if (t == typeof(bool)) { if (!bool.TryParse(text, out var b)) return false; val = b; }
        else if (t == typeof(int) || t == typeof(long) || t == typeof(double))
        {
            if (text.Length == 0 && Nullable.GetUnderlyingType(p.PropertyType) != null) val = null;
            else
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return false;
                if ((min != null && d < min) || (max != null && d > max)) return false;
                if (t != typeof(double) && d != Math.Floor(d)) return false;
                val = t == typeof(int) ? (object)(int)d : t == typeof(long) ? (object)(long)d : d;
            }
        }
        else if (t == typeof(string)) val = text;
        else if (typeof(IList<string>).IsAssignableFrom(t))
            val = text.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        else return false;
        p.SetValue(o, val);
        return true;
    }
}

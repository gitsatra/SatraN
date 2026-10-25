namespace v2rayN.Desktop.Common;

public static class AppBuilderExtension
{
    // Satra: bundled Inter (Latin/Cyrillic) instead of the 10.5 MB embedded Noto Sans SC; Persian/CJK come from system fonts.
    private const string DefaultFontFamilyName = "fonts:Inter#Inter";

    public static AppBuilder WithFontByDefault(this AppBuilder appBuilder)
    {
        var fallbacks = new List<FontFallback>();
        // Bundled Persian subset (Vazirmatn, ~107 KB) so Persian looks the same on every OS.
        fallbacks.Add(new FontFallback { FontFamily = new FontFamily("avares://SatraN/Assets/Fonts#Vazirmatn") });

        if (OperatingSystem.IsWindows())
        {
            AddFontFallback(fallbacks, "Segoe UI");
            AddFontFallback(fallbacks, "Tahoma");
            AddFontFallback(fallbacks, "Microsoft YaHei");
            AddFontFallback(fallbacks, "Segoe UI Emoji");
            AddFontFallback(fallbacks, "Segoe UI Symbol");
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddFontFallback(fallbacks, "Geeza Pro");
            AddFontFallback(fallbacks, "PingFang SC");
            AddFontFallback(fallbacks, "Apple Color Emoji");
            AddFontFallback(fallbacks, "Apple Symbols");
        }
        else if (OperatingSystem.IsLinux())
        {
            AddFontFallback(fallbacks, "Vazirmatn");
            AddFontFallback(fallbacks, "Noto Sans Arabic");
            AddFontFallback(fallbacks, "Noto Sans");
            AddFontFallback(fallbacks, "DejaVu Sans");
            AddFontFallback(fallbacks, "Noto Sans CJK SC");
            AddFontFallback(fallbacks, "Noto Color Emoji");
            AddFontFallback(fallbacks, "Noto Sans Symbols");
        }

        return appBuilder.With(new FontManagerOptions
        {
            DefaultFamilyName = DefaultFontFamilyName,
            FontFallbacks = fallbacks.ToArray()
        });
    }

    private static void AddFontFallback(List<FontFallback> fallbacks, string fontFamilyName)
    {
        fallbacks.Add(new FontFallback
        {
            FontFamily = new FontFamily(fontFamilyName)
        });
    }
}

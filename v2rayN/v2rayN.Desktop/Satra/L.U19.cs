namespace v2rayN.Desktop.Satra;

/// <summary>v1.9 strings: Usage rebuild + Advanced settings rebuild.</summary>
public static partial class L
{
    private static readonly Dictionary<string, (string en, string fa, string ru)> W = new()
    {
        ["u_n_sites"] = ("{0} destinations", "{0} مقصد", "{0} адресов"),
        ["u_hosts"] = ("Destinations · {0}", "مقصدها · {0}", "Адреса · {0}"),
        ["u_no_hosts"] = ("No destinations recorded yet for this period.", "هنوز مقصدی برای این بازه ثبت نشده.", "За этот период адресов нет."),
        ["u_app_detail"] = ("App usage", "مصرف برنامه", "Трафик приложения"),
    };
}

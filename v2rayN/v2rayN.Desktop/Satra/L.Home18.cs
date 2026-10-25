namespace v2rayN.Desktop.Satra;

/// <summary>v1.8 Home strings (en / fa / ru), checked before the main tables.</summary>
public static partial class L
{
    private static readonly Dictionary<string, (string en, string fa, string ru)> X = new()
    {
        ["mode_proxy_only"] = ("Ports only", "فقط پورت‌ها", "Только порты"),
        ["mode_proxy"] = ("System proxy", "پراکسی سیستم", "Системный прокси"),
        ["mode_vpn"] = ("Whole device", "کل دستگاه (TUN)", "Всё устройство"),
        ["route_title"] = ("How apps use it", "نحوهٔ استفاده", "Как это используют"),
        ["st_on_ports"] = ("Running", "روشن", "Работает"),
        ["st_on_proxy"] = ("System proxy on", "پراکسی سیستم روشن", "Системный прокси включён"),
        ["st_on_tun"] = ("Whole device protected", "کل دستگاه وصل است", "Всё устройство защищено"),
        ["st_off"] = ("Off", "خاموش", "Выключено"),
        ["d_on_ports"] = ("Local ports are open. Apps you point at them go through the server; nothing else changes.", "پورت‌های محلی باز هستند. هر برنامه‌ای که روی این پورت تنظیم شود از سرور رد می‌شود؛ بقیهٔ سیستم تغییری نمی‌کند.", "Локальные порты открыты. Через сервер идут приложения, настроенные на них; остальное не меняется."),
        ["d_on_proxy"] = ("Browsers and apps that follow the system proxy go through the server.", "مرورگرها و برنامه‌هایی که از پراکسی سیستم پیروی می‌کنند از سرور رد می‌شوند.", "Браузеры и приложения, использующие системный прокси, идут через сервер."),
        ["d_on_tun"] = ("All traffic of this computer goes through the server.", "همهٔ ترافیک این کامپیوتر از سرور رد می‌شود.", "Весь трафик компьютера идёт через сервер."),
        ["d_off"] = ("Nothing is running. Turn on to open the ports.", "چیزی در حال اجرا نیست. برای باز شدن پورت‌ها روشن کنید.", "Ничего не запущено. Включите, чтобы открыть порты."),
        ["d_connecting"] = ("Starting on the selected server…", "در حال راه‌اندازی روی سرور انتخاب‌شده…", "Запуск на выбранном сервере…"),
        ["m_ports_d"] = ("Only apps you set to 127.0.0.1 use it", "فقط برنامه‌هایی که روی 127.0.0.1 تنظیم کنید", "Только приложения, настроенные на 127.0.0.1"),
        ["m_proxy_d"] = ("Browsers and most apps", "مرورگرها و بیشتر برنامه‌ها", "Браузеры и большинство приложений"),
        ["m_tun_d"] = ("Every app, like a VPN", "همهٔ برنامه‌ها، مثل VPN", "Все приложения, как VPN"),
        ["srv_no_answer"] = ("The server isn't answering — try another one", "سرور جواب نمی‌دهد — یکی دیگر را امتحان کنید", "Сервер не отвечает — попробуйте другой"),
        ["mode_fell_back"] = ("{0} couldn't start — back to Ports only", "{0} راه نیفتاد — به «فقط پورت‌ها» برگشت", "{0} не запустился — возврат к «Только порты»"),
        ["core_refused"] = ("The core rejected this config", "هسته این کانفیگ را نپذیرفت", "Ядро отклонило конфиг"),
        ["server"] = ("Server", "سرور", "Сервер"),
        ["best_servers"] = ("Fastest", "سریع‌ترین‌ها", "Быстрые"),
        ["all_servers"] = ("All servers", "همهٔ سرورها", "Все серверы"),
        ["ping_all_short"] = ("Test pings", "تست پینگ", "Проверить пинг"),
        ["turn_on"] = ("Turn on", "روشن کن", "Включить"),
        ["turn_off"] = ("Turn off", "خاموش کن", "Выключить"),
        ["sc_need_vpn"] = ("SatraN is off", "SatraN خاموش است", "SatraN выключен"),
        ["sc_need_vpn_d"] = ("The check runs through the selected server. Turn it on and the scan starts right away.", "بررسی از طریق سرور انتخاب‌شده انجام می‌شود. روشنش کنید تا بلافاصله شروع شود.", "Проверка идёт через выбранный сервер. Включите — и она начнётся сразу."),
        ["sc_connect"] = ("Turn on & check", "روشن کن و بررسی کن", "Включить и проверить"),
        ["auto_start"] = ("Start with the app", "روشن شدن همراه برنامه", "Запускать вместе с приложением"),
        ["auto_start_d"] = ("Open the ports on the selected server as soon as SatraN opens", "با باز شدن SatraN، پورت‌ها روی سرور انتخاب‌شده باز شوند", "Открывать порты на выбранном сервере при запуске SatraN"),
        ["copy"] = ("Copy", "کپی", "Копировать"),
    };
}

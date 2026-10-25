using v2rayN.Desktop.Satra.Services;
using ServiceLib.Satra;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>
/// v1.9 Advanced settings in Satra's own design language: every engine option the v2rayN windows had (core, inbound,
/// TUN, mux, fragment, DNS, system proxy, speed test, interface, sources) grouped into cards with a plain one-line
/// explanation, edited in place (sheets), saved and applied automatically. The original v2rayN editors stay
/// reachable under "Expert editors" so nothing is lost.
/// </summary>
public static class AdvancedSettings
{
    private static Config Cfg => AppManager.Instance.Config;
    private record T3(string En, string Fa, string Ru) { public string S => L.Lang == "fa" ? Fa : L.Lang == "ru" ? Ru : En; }
    private record F(string Path, T3 Label, T3? Help = null, IReadOnlyList<string>? Choices = null, double? Min = null, double? Max = null, string? Hint = null);
    private record G(string Id, string Glyph, Color Tint, T3 Title, T3 Sub, F[] Fields);

    private static readonly string[] Fps = Global.Fingerprints.ToArray();

    private static readonly G[] Groups =
    [
        new("core", "⚙", P.Accent, new("Engine", "موتور", "Ядро"), new("Logging, TLS fingerprint, user-agent, outgoing interface", "لاگ، اثر انگشت TLS، User-Agent، کارت شبکهٔ خروجی", "Журнал, TLS-отпечаток, User-Agent, интерфейс"),
        [
            new("CoreBasicItem.LogEnabled", new("Write core log", "ذخیرهٔ لاگ هسته", "Писать журнал ядра"), new("Needed for the Logs page to show core messages", "برای نمایش پیام‌های هسته در صفحهٔ لاگ لازم است", "Нужно для страницы «Журнал»")),
            new("CoreBasicItem.Loglevel", new("Log level", "سطح لاگ", "Уровень журнала"), new("How much the core writes. ‘warning’ is a good default", "هسته چقدر بنویسد. warning پیش‌فرض خوبی است", "Сколько пишет ядро. ‘warning’ — хороший выбор"), Global.LogLevels),
            new("CoreBasicItem.DefFingerprint", new("TLS fingerprint", "اثر انگشت TLS", "TLS-отпечаток"), new("Makes TLS look like a real browser (chrome is safest)", "TLS را شبیه یک مرورگر واقعی می‌کند (chrome امن‌ترین است)", "Маскирует TLS под браузер (chrome надёжнее)"), Fps),
            new("CoreBasicItem.DefUserAgent", new("User-Agent", "User-Agent", "User-Agent"), new("Sent on HTTP-based transports when a server has none", "روی ترابری‌های HTTP وقتی سرور مقداری ندارد ارسال می‌شود", "Для HTTP-транспортов, если у сервера нет своего"), Hint: "chrome"),
            new("CoreBasicItem.SendThrough", new("Send through (local IP)", "ارسال از آی‌پی محلی", "Исходящий локальный IP"), new("Leave empty unless you have several network cards", "خالی بگذارید مگر چند کارت شبکه دارید", "Оставьте пустым, если одна сеть"), Hint: "0.0.0.0"),
            new("CoreBasicItem.BindInterface", new("Bind to interface", "اتصال به کارت شبکه", "Привязка к интерфейсу"), new("Force outgoing traffic through one adapter (e.g. eth0)", "ترافیک خروجی را از یک کارت شبکهٔ خاص می‌فرستد", "Весь исходящий трафик через один адаптер"), Hint: "eth0"),
            new("CoreBasicItem.EnableCacheFile4Sbox", new("sing-box cache file", "فایل کش sing-box", "Кэш-файл sing-box"), new("Remembers DNS/fake-IP between restarts", "DNS و fake-IP را بین اجراها نگه می‌دارد", "Сохраняет DNS/fake-IP между запусками")),
            new("GuiItem.EnableStatistics", new("Traffic statistics", "آمار ترافیک", "Статистика трафика"), new("Live speed and per-config usage (tiny overhead)", "سرعت زنده و مصرف هر کانفیگ (سربار ناچیز)", "Скорость и трафик по конфигам")),
        ]),
        new("inbound", "⌁", P.Info, new("Local proxy", "پروکسی محلی", "Локальный прокси"), new("Port, UDP, sniffing, LAN sharing, password", "پورت، UDP، شناسایی دامنه، اشتراک در شبکه، رمز", "Порт, UDP, sniffing, LAN, пароль"),
        [
            new("Inbound[0].LocalPort", new("Port", "پورت", "Порт"), new("SOCKS5 + HTTP on 127.0.0.1", "SOCKS5 و HTTP روی 127.0.0.1", "SOCKS5 + HTTP на 127.0.0.1"), Min: 1024, Max: 65000),
            new("Inbound[0].UdpEnabled", new("UDP", "UDP", "UDP"), new("Lets games and calls use UDP through the proxy", "اجازهٔ UDP برای بازی و تماس از طریق پروکسی", "UDP для игр и звонков")),
            new("Inbound[0].SniffingEnabled", new("Sniffing", "شناسایی دامنه", "Sniffing"), new("Reads the site name so routing rules work", "نام سایت را می‌خواند تا قوانین مسیریابی کار کنند", "Определяет домен для правил")),
            new("Inbound[0].RouteOnly", new("Sniff for routing only", "شناسایی فقط برای مسیریابی", "Sniffing только для маршрутизации"), new("Keeps the original destination (fixes some apps)", "مقصد اصلی را نگه می‌دارد (برای بعضی برنامه‌ها)", "Сохраняет исходный адрес")),
            new("Inbound[0].DestOverride", new("Sniff protocols", "پروتکل‌های شناسایی", "Протоколы sniffing"), new("Comma separated: http, tls, quic, fakedns", "با کاما: http, tls, quic, fakedns", "Через запятую: http, tls, quic, fakedns")),
            new("Inbound[0].AllowLANConn", new("Allow devices on my network", "اجازه به دستگاه‌های شبکه", "Разрешить устройства в сети"), new("Phones/TVs on your Wi-Fi can use this proxy", "گوشی و تلویزیون روی وای‌فای از این پروکسی استفاده کنند", "Телефоны и ТВ в вашей сети смогут подключаться")),
            new("Inbound[0].NewPort4LAN", new("Separate port for LAN", "پورت جدا برای شبکه", "Отдельный порт для LAN"), new("LAN uses port+2 with optional password", "شبکه از پورت+۲ با رمز اختیاری استفاده می‌کند", "LAN на порту+2 с паролем")),
            new("Inbound[0].User", new("LAN user", "نام کاربری شبکه", "Пользователь LAN")),
            new("Inbound[0].Pass", new("LAN password", "رمز شبکه", "Пароль LAN")),
        ]),
        new("tun", "⛨", P.Success, new("Whole device (TUN)", "کل دستگاه (TUN)", "Всё устройство (TUN)"), new("Network stack, MTU, strict route, IPv6, excluded addresses", "پشتهٔ شبکه، MTU، مسیر سخت‌گیر، IPv6، آدرس‌های مستثنا", "Стек, MTU, строгий маршрут, IPv6, исключения"),
        [
            new("TunModeItem.Stack", new("Network stack", "پشتهٔ شبکه", "Сетевой стек"), new("gvisor is most compatible; system is fastest", "gvisor سازگارتر و system سریع‌تر است", "gvisor совместимее, system быстрее"), Global.TunStacks),
            new("TunModeItem.Mtu", new("MTU", "MTU", "MTU"), new("Lower (1400) if some sites stall", "اگر بعضی سایت‌ها گیر می‌کنند کمتر کنید (۱۴۰۰)", "Уменьшите (1400), если сайты зависают"), Min: 1280, Max: 9000),
            new("TunModeItem.StrictRoute", new("Strict route", "مسیر سخت‌گیر", "Строгий маршрут"), new("Prevents leaks around the tunnel", "از نشت ترافیک بیرون از تونل جلوگیری می‌کند", "Предотвращает утечки мимо туннеля")),
            new("TunModeItem.AutoRoute", new("Automatic routes", "مسیرهای خودکار", "Автомаршруты"), new("Leave on; off only for manual routing setups", "روشن بماند؛ فقط برای تنظیم دستی خاموش کنید", "Оставьте включённым")),
            new("TunModeItem.EnableIPv6Address", new("IPv6 in the tunnel", "IPv6 در تونل", "IPv6 в туннеле"), new("Off avoids IPv6 leaks when the server has no IPv6", "خاموش بودن از نشت IPv6 جلوگیری می‌کند", "Выкл. — нет утечек IPv6")),
            new("TunModeItem.EnableLegacyProtect", new("Legacy protect", "محافظ قدیمی", "Старая защита"), new("Compatibility for older systems", "سازگاری با سیستم‌های قدیمی", "Совместимость со старыми системами")),
            new("TunModeItem.IcmpRouting", new("Ping (ICMP)", "پینگ (ICMP)", "Ping (ICMP)"), new("How ping packets are handled in the tunnel", "پینگ در تونل چگونه رد شود", "Обработка ping в туннеле"), Hint: "default"),
            new("TunModeItem.IPv4Address", new("Tunnel IPv4", "آی‌پی ۴ تونل", "IPv4 туннеля"), Hint: "172.18.0.1/30"),
            new("TunModeItem.IPv6Address", new("Tunnel IPv6", "آی‌پی ۶ تونل", "IPv6 туннеля"), Hint: "fdfe:dcba:9876::1/126"),
            new("TunModeItem.RouteExcludeAddress", new("Never through the tunnel", "هرگز از تونل", "Никогда через туннель"), new("Comma separated IPs/ranges kept on your normal network", "آی‌پی یا رنج‌ها با کاما که از شبکهٔ عادی بروند", "IP/диапазоны через запятую")),
        ]),
        new("mux", "⇶", Color.Parse("#A78BFA"), new("Multiplexing & fragment", "Mux و Fragment", "Mux и fragment"), new("Mux concurrency, XUDP, TLS fragment against SNI filtering", "هم‌زمانی Mux، XUDP، تکه‌تکه کردن TLS برای دور زدن فیلتر SNI", "Mux, XUDP, фрагментация TLS"),
        [
            new("Mux4RayItem.Concurrency", new("Xray mux streams", "جریان‌های Mux در Xray", "Потоки mux Xray"), new("Connections shared per tunnel (8 is typical, empty = default)", "تعداد اتصال در هر تونل (معمولاً ۸)", "Соединений на туннель (обычно 8)"), Min: -1, Max: 1024),
            new("Mux4RayItem.XudpConcurrency", new("XUDP streams", "جریان‌های XUDP", "Потоки XUDP"), Min: -1, Max: 1024),
            new("Mux4RayItem.XudpProxyUDP443", new("UDP 443 (QUIC) with mux", "UDP 443 (QUIC) با Mux", "UDP 443 (QUIC) с mux"), new("reject / allow / skip", "reject / allow / skip", "reject / allow / skip"), ["reject", "allow", "skip", ""]),
            new("Mux4SboxItem.Protocol", new("sing-box mux protocol", "پروتکل Mux در sing-box", "Протокол mux sing-box"), Choices: Global.SingboxMuxs),
            new("Mux4SboxItem.MaxConnections", new("sing-box max connections", "حداکثر اتصال sing-box", "Макс. соединений sing-box"), Min: 0, Max: 64),
            new("CoreBasicItem.EnableFragment", new("TLS fragment", "تکه‌تکه کردن TLS", "Фрагментация TLS"), new("Helps against SNI filtering on some networks", "در بعضی شبکه‌ها فیلتر SNI را دور می‌زند", "Помогает против фильтрации SNI")),
            new("CoreBasicItem.EnableFinalFragment", new("Fragment on final outbound", "Fragment روی خروجی نهایی", "Fragment на финальном выходе")),
            new("Fragment4RayItem.Packets", new("Fragment packets", "بسته‌های Fragment", "Пакеты fragment"), Hint: "tlshello"),
            new("Fragment4RayItem.Lengths", new("Fragment lengths", "طول تکه‌ها", "Длины фрагментов"), Hint: "100-200"),
            new("Fragment4RayItem.Delays", new("Fragment delays (ms)", "تأخیر تکه‌ها (ms)", "Задержки (мс)"), Hint: "10-20"),
            new("Fragment4RayItem.MaxSplit", new("Max split", "حداکثر تقسیم", "Макс. разбиение")),
            new("HysteriaItem.UpMbps", new("Hysteria upload (Mbps)", "آپلود Hysteria (Mbps)", "Hysteria отдача (Мбит/с)"), Min: 0, Max: 100000),
            new("HysteriaItem.DownMbps", new("Hysteria download (Mbps)", "دانلود Hysteria (Mbps)", "Hysteria загрузка (Мбит/с)"), Min: 0, Max: 100000),
            new("HysteriaItem.HopInterval", new("Hysteria port-hop interval (s)", "فاصلهٔ پرش پورت Hysteria (ثانیه)", "Интервал смены порта (с)"), Min: 5, Max: 3600),
        ]),
        new("dns", "◎", P.Warning, new("DNS", "DNS", "DNS"), new("Direct/remote DNS, fake-IP, IPv6 answers, hosts", "DNS مستقیم/راه دور، fake-IP، پاسخ IPv6، hosts", "Прямой/удалённый DNS, fake-IP, hosts"),
        [
            new("SimpleDNSItem.RemoteDNS", new("DNS through the VPN", "DNS از داخل VPN", "DNS через VPN"), new("Used for blocked sites (encrypted, e.g. https://1.1.1.1/dns-query)", "برای سایت‌های فیلتر (رمزشده، مثلاً https://1.1.1.1/dns-query)", "Для заблокированных сайтов (DoH)"), Hint: "https://1.1.1.1/dns-query"),
            new("SimpleDNSItem.DirectDNS", new("DNS for direct sites", "DNS برای سایت‌های مستقیم", "DNS для прямых сайтов"), new("Used for local/Iranian sites that go direct", "برای سایت‌های داخلی که مستقیم می‌روند", "Для локальных сайтов"), Hint: "https://dns.alidns.com/dns-query"),
            new("SimpleDNSItem.BootstrapDNS", new("Bootstrap DNS", "DNS راه‌انداز", "Начальный DNS"), new("Plain IP used only to find the DNS servers above", "آی‌پی ساده فقط برای پیدا کردن DNSهای بالا", "Простой IP для поиска DNS выше"), Hint: "223.5.5.5"),
            new("SimpleDNSItem.Strategy4Proxy", new("Remote answers", "پاسخ‌های راه دور", "Ответы удалённого DNS"), new("Which IP family to ask for through the VPN", "نوع آی‌پی درخواستی از داخل VPN", "Какие IP запрашивать через VPN"), Global.DomainStrategy),
            new("SimpleDNSItem.Strategy4Freedom", new("Direct answers", "پاسخ‌های مستقیم", "Ответы прямого DNS"), Choices: Global.DomainStrategy),
            new("SimpleDNSItem.Strategy4ProxyDial", new("Server address lookup", "پیدا کردن آدرس سرور", "Поиск адреса сервера"), Choices: Global.DomainStrategy),
            new("SimpleDNSItem.FakeIP", new("Fake-IP", "Fake-IP", "Fake-IP"), new("Faster first connection; apps see placeholder IPs", "اتصال اول سریع‌تر؛ برنامه‌ها آی‌پی موقت می‌بینند", "Быстрее первое соединение")),
            new("SimpleDNSItem.GlobalFakeIp", new("Fake-IP for everything", "Fake-IP برای همه", "Fake-IP для всего")),
            new("SimpleDNSItem.FakeIPRange", new("Fake-IP range", "رنج Fake-IP", "Диапазон Fake-IP"), Hint: "198.18.0.0/15"),
            new("SimpleDNSItem.BlockAAAAQuery", new("Block IPv6 answers", "مسدود کردن پاسخ IPv6", "Блокировать IPv6-ответы"), new("Avoids IPv6 leaks on IPv4-only servers", "از نشت IPv6 روی سرورهای بدون IPv6 جلوگیری می‌کند", "Нет утечек IPv6")),
            new("SimpleDNSItem.BlockBindingQuery", new("Block binding queries", "مسدود کردن کوئری‌های SVCB/HTTPS", "Блокировать SVCB/HTTPS")),
            new("SimpleDNSItem.ParallelQuery", new("Ask in parallel", "پرسش هم‌زمان", "Параллельные запросы"), new("Faster answers using all DNS servers at once", "پاسخ سریع‌تر با پرسیدن هم‌زمان", "Быстрее, опрашивая все DNS")),
            new("SimpleDNSItem.ServeStale", new("Serve stale answers", "استفاده از پاسخ قدیمی", "Отдавать устаревшие ответы"), new("Keeps sites opening while DNS is slow", "وقتی DNS کند است سایت‌ها باز می‌مانند", "Сайты открываются при медленном DNS")),
            new("SimpleDNSItem.EnableHappyEyeballs", new("Happy Eyeballs", "Happy Eyeballs", "Happy Eyeballs"), new("Tries IPv4 and IPv6 together, uses the faster", "IPv4 و IPv6 را با هم امتحان می‌کند", "Пробует IPv4 и IPv6 сразу")),
            new("SimpleDNSItem.UseSystemHosts", new("Use system hosts file", "استفاده از فایل hosts سیستم", "Системный hosts")),
            new("SimpleDNSItem.AddCommonHosts", new("Add common hosts", "افزودن hosts رایج", "Добавить общие hosts")),
            new("SimpleDNSItem.Hosts", new("Custom hosts", "hosts سفارشی", "Свои hosts"), new("One per line: domain ip", "هر خط: دامنه آی‌پی", "По строке: домен ip")),
            new("SimpleDNSItem.DirectExpectedIPs", new("Expected direct IPs", "آی‌پی‌های مورد انتظار مستقیم", "Ожидаемые прямые IP"), Hint: "geoip:ir"),
        ]),
        new("sysproxy", "⇄", P.Info, new("System proxy", "پروکسی سیستم", "Системный прокси"), new("Exceptions, local addresses, PAC", "استثناها، آدرس‌های محلی، PAC", "Исключения, локальные адреса, PAC"),
        [
            new("SystemProxyItem.SystemProxyExceptions", new("Don't proxy", "بدون پروکسی", "Без прокси"), new("Hosts the system proxy skips (separated by ;)", "میزبان‌هایی که پروکسی سیستم رد نمی‌کند (با ;)", "Адреса без прокси (через ;)")),
            new("SystemProxyItem.NotProxyLocalAddress", new("Skip local addresses", "رد کردن آدرس‌های محلی", "Пропускать локальные адреса"), new("Router, printers and LAN stay direct", "مودم، پرینتر و شبکهٔ محلی مستقیم بمانند", "Роутер и LAN напрямую")),
            new("SystemProxyItem.SystemProxyAdvancedProtocol", new("Proxy protocol string", "رشتهٔ پروتکل پروکسی", "Строка протокола"), Hint: "{ip}:{http_port}"),
            new("SystemProxyItem.CustomSystemProxyPacPath", new("Custom PAC file", "فایل PAC سفارشی", "Свой PAC-файл")),
            new("SystemProxyItem.CustomSystemProxyScriptPath", new("Custom proxy script", "اسکریپت پروکسی سفارشی", "Свой скрипт прокси")),
        ]),
        new("tests", "◔", P.Accent, new("Ping & speed tests", "پینگ و تست سرعت", "Пинг и скорость"), new("Test URLs, timeout, how many at once", "آدرس‌های تست، زمان انتظار، تعداد هم‌زمان", "URL тестов, таймаут, параллельность"),
        [
            new("SpeedTestItem.SpeedPingTestUrl", new("Ping URL", "آدرس پینگ", "URL пинга"), Choices: Global.SpeedPingTestUrls),
            new("SpeedTestItem.SpeedTestUrl", new("Speed test URL", "آدرس تست سرعت", "URL скорости"), Choices: Global.SpeedTestUrls),
            new("SpeedTestItem.SpeedTestTimeout", new("Timeout (s)", "زمان انتظار (ثانیه)", "Таймаут (с)"), Min: 2, Max: 120),
            new("SpeedTestItem.MixedConcurrencyCount", new("Tests at once", "تعداد تست هم‌زمان", "Тестов одновременно"), Min: 1, Max: 100),
            new("SpeedTestItem.IPAPIUrl", new("Exit-IP lookup URL", "آدرس شناسایی آی‌پی خروجی", "URL определения IP"), Choices: Global.IPAPIUrls),
            new("SpeedTestItem.UdpTestTarget", new("UDP test target", "مقصد تست UDP", "Цель UDP-теста")),
        ]),
        new("ui", "▣", P.Text2, new("Interface & startup", "ظاهر و راه‌اندازی", "Интерфейс и запуск"), new("Start with the computer, tray, subscription interval", "اجرا با روشن شدن، سینی سیستم، بازهٔ اشتراک", "Автозапуск, трей, интервал подписок"),
        [
            new("GuiItem.AutoRun", new("Open at login", "اجرا هنگام ورود", "Запуск при входе")),
            new("UiItem.AutoHideStartup", new("Start hidden in tray", "شروع پنهان در سینی", "Запуск в трее")),
            new("UiItem.Hide2TrayWhenClose", new("Close to tray", "بستن به سینی", "Закрывать в трей"), new("The window's close button keeps SatraN running", "دکمهٔ بستن، SatraN را در پس‌زمینه نگه می‌دارد", "Крестик оставляет SatraN работать")),
            new("GuiItem.TrayMenuServersLimit", new("Servers in tray menu", "تعداد سرور در منوی سینی", "Серверов в меню трея"), Min: 0, Max: 200),
            new("GuiItem.AutoUpdateInterval", new("Subscription refresh (hours)", "بازهٔ به‌روزرسانی اشتراک (ساعت)", "Обновление подписок (ч)"), Min: 0, Max: 720),
            new("GuiItem.KeepOlderDedupl", new("Keep older duplicates", "نگه داشتن تکراری قدیمی‌تر", "Оставлять старые дубликаты")),
            new("GuiItem.EnableHWA", new("Hardware acceleration", "شتاب سخت‌افزاری", "Аппаратное ускорение"), new("Takes effect after restart", "بعد از اجرای دوباره اعمال می‌شود", "После перезапуска")),
            new("GuiItem.EnableLog", new("App log file", "فایل لاگ برنامه", "Журнал приложения")),
        ]),
        new("sources", "⇩", P.Success, new("Update sources", "منابع به‌روزرسانی", "Источники обновлений"), new("Where geo files, rule sets and subscriptions convert from", "منبع فایل‌های geo، قوانین و تبدیل اشتراک", "Откуда geo, правила, конвертер"),
        [
            new("ConstItem.GeoSourceUrl", new("Geo files source", "منبع فایل‌های geo", "Источник geo"), new("Empty = default (with Hub fallback)", "خالی = پیش‌فرض (با پشتیبان Hub)", "Пусто = по умолчанию")),
            new("ConstItem.SrsSourceUrl", new("Rule sets source", "منبع قوانین", "Источник правил")),
            new("ConstItem.RouteRulesTemplateSourceUrl", new("Routing template source", "منبع الگوی مسیریابی", "Источник шаблона маршрутов")),
            new("ConstItem.SubConvertUrl", new("Subscription converter", "مبدل اشتراک", "Конвертер подписок")),
            new("CheckUpdateItem.UpdateViaProxy", new("Updates through the VPN", "به‌روزرسانی از طریق VPN", "Обновления через VPN"), new("When connected, downloads go through the tunnel", "وقتی وصل هستید، دانلودها از تونل می‌روند", "При подключении — через туннель")),
        ]),
    ];

    public static int Count => Groups.Sum(g => g.Fields.Length);

    /// <summary>The Advanced category body: one card per group, then the expert editors.</summary>
    public static Control View(Action rebuild)
    {
        var rows = Groups.Select(g =>
        {
            var n = g.Fields.Count(f => ConfigPath.KindOf(Cfg, f.Path) != ConfigPath.Kind.Unsupported);
            return (Control)Ui.ListRow(g.Title.S, g.Sub.S, Chevron(n), () => OpenGroup(g), g.Glyph, g.Tint);
        }).ToArray();
        return Ui.Enter(Ui.Group(rows));
    }

    private static Control Chevron(int n)
    {
        var c = Ui.T(L.Lang == "fa" ? "‹" : "›", 18, FontWeight.Bold, P.Text3);
        return Ui.H(8, Ui.Pill(n.ToString(), P.Text3, P.Fill), c);
    }

    private static CancellationTokenSource? _apply;
    /// Save now; restart the core (debounced) only if it is running — never turns anything on.
    private static void Changed(string label)
    {
        _ = ConfigHandler.SaveConfig(Cfg);
        _apply?.Cancel();
        var cts = _apply = new CancellationTokenSource();
        _ = Task.Delay(900, cts.Token).ContinueWith(async t =>
        {
            if (t.IsCanceled) return;
            if (SatraCore.I.Phase == ConnPhase.On) await SatraCore.I.Main.Reload();
        }, TaskScheduler.Default);
        SatraShell.I.Toast(L.S("saved") + " · " + label);
    }

    internal static void DevOpen(string id) { if (Groups.FirstOrDefault(g => g.Id == id) is { } g) OpenGroup(g); }

    private static void OpenGroup(G g)
    {
        var list = new StackPanel();
        var first = true;
        foreach (var f in g.Fields)
        {
            var kind = ConfigPath.KindOf(Cfg, f.Path);
            if (kind == ConfigPath.Kind.Unsupported) continue;
            if (!first) list.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(16, 0, 0, 0) });
            first = false;
            list.Children.Add(FieldRow(f, kind));
        }
        var head = Ui.H(12, Glyph(g.Glyph, g.Tint), Ui.V(2, Ui.T(g.Title.S, 17, FontWeight.Bold), Ui.Caption(g.Sub.S, P.Text3)));
        var note = Ui.Caption(L.S("adv_autosave"), P.Text3);
        SatraShell.I.Sheet(g.Title.S, Ui.V(14, head,
            new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = new ScrollViewer { MaxHeight = 470, Content = list } }, note), 640);
    }

    private static Control Glyph(string g, Color tint)
    {
        var t = Ui.T(g, 17, FontWeight.Bold, tint);
        t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(11), Background = P.B(tint, 0.16), Child = t };
    }

    private static Control FieldRow(F f, ConfigPath.Kind kind)
    {
        Control editor;
        var wide = false;
        switch (kind)
        {
            case ConfigPath.Kind.Bool:
                editor = Ui.Switch(ConfigPath.GetBool(Cfg, f.Path), v => { if (ConfigPath.Set(Cfg, f.Path, v ? "true" : "false")) Changed(f.Label.S); });
                break;
            case ConfigPath.Kind.Text when f.Choices != null:
            {
                var cur = ConfigPath.Get(Cfg, f.Path);
                var items = f.Choices.Select(c => c.Length == 0 ? L.S("adv_default") : c).ToList();
                if (!f.Choices.Contains(cur)) items.Insert(0, cur.Length == 0 ? L.S("adv_default") : cur);
                var vals = f.Choices.Contains(cur) ? f.Choices.ToList() : f.Choices.Prepend(cur).ToList();
                var cb = new ComboBox { ItemsSource = items, SelectedIndex = Math.Max(0, vals.IndexOf(cur)), MinWidth = 170, MaxWidth = 260 };
                cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0 && ConfigPath.Set(Cfg, f.Path, vals[cb.SelectedIndex])) Changed(f.Label.S); };
                editor = cb;
                break;
            }
            default:
            {
                var tb = Ui.Input(f.Hint ?? "", ConfigPath.Get(Cfg, f.Path));
                var multi = f.Path.EndsWith(".Hosts");
                if (multi) { tb.AcceptsReturn = true; tb.Height = 90; tb.TextWrapping = TextWrapping.Wrap; }
                wide = kind != ConfigPath.Kind.Number;
                tb.Width = kind == ConfigPath.Kind.Number ? 110 : double.NaN;
                var last = tb.Text ?? "";
                tb.LostFocus += (_, _) =>
                {
                    var txt = tb.Text ?? "";
                    if (txt == last) return;
                    if (ConfigPath.Set(Cfg, f.Path, txt, f.Min, f.Max)) { last = ConfigPath.Get(Cfg, f.Path); tb.Text = last; Changed(f.Label.S); }
                    else { tb.Text = last; SatraShell.I.Toast(L.S("adv_invalid")); }
                };
                editor = tb;
                break;
            }
        }
        var title = Ui.T(f.Label.S, 14, FontWeight.Medium);
        var left = f.Help == null ? (Control)title : Ui.V(2, title, Ui.Caption(f.Help.S, P.Text3));
        if (wide) return new Border { Padding = new Thickness(16, 11), Child = Ui.V(8, left, editor) };
        return new Border { Padding = new Thickness(16, 11), Child = Ui.Row(left, editor) };
    }
}

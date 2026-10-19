namespace v2rayN.Desktop.Satra;

/// <summary>SatraN UI strings: English, فارسی, Русский. Missing keys fall back to English.</summary>
public static class L
{
    public static string Lang => AppManager.Instance.Config.UiItem.CurrentLanguage ?? "en";
    public static bool Rtl => Lang == "fa";
    public static CultureInfo Culture => Lang == "fa" ? new CultureInfo("fa-IR") : Lang == "ru" ? new CultureInfo("ru-RU") : CultureInfo.InvariantCulture;

    public static string S(string key)
    {
        if (Lang == "fa" && Fa.TryGetValue(key, out var f)) return f;
        if (Lang == "ru" && Ru.TryGetValue(key, out var r)) return r;
        return En.TryGetValue(key, out var e) ? e : key;
    }

    public static string F(string key, params object[] a) => string.Format(S(key), a);

    private static readonly Dictionary<string, string> En = new()
    {
        ["home"] = "Home", ["servers"] = "Servers", ["subs"] = "Subscriptions", ["usage"] = "Usage", ["settings"] = "Settings",
        ["connected"] = "Connected", ["disconnected"] = "Not connected", ["connecting"] = "Connecting…", ["disconnecting"] = "Disconnecting…", ["failed"] = "Couldn't connect",
        ["tap_connect"] = "Click to connect", ["tap_disconnect"] = "Click to disconnect", ["retry"] = "Click to try again",
        ["download"] = "Download", ["upload"] = "Upload", ["today"] = "Today", ["session"] = "Session", ["total"] = "Total",
        ["ping"] = "Ping", ["smart"] = "Smart connect", ["smart_desc"] = "Picks the fastest server for you", ["no_server"] = "No server yet",
        ["add"] = "Add", ["add_clip"] = "Add from clipboard", ["add_hint"] = "Copy a subscription link or a config, then press Add. SatraN detects what it is.",
        ["search"] = "Search servers", ["all"] = "All", ["favorites"] = "Favorites", ["ping_all"] = "Ping all", ["speed_all"] = "Speed test all", ["sort_best"] = "Best first",
        ["speed"] = "Speed test", ["use"] = "Use", ["active"] = "Active", ["tunnel"] = "Tunnel", ["exit"] = "Exit",
        ["update_all"] = "Update all", ["update"] = "Update", ["remaining"] = "remaining", ["days_left"] = "{0} days left", ["expired"] = "Expired", ["unlimited"] = "Unlimited", ["no_expiry"] = "No expiry",
        ["support"] = "Support", ["renew"] = "Renew", ["devices"] = "Devices", ["limits"] = "Limits", ["owner_pin"] = "Owner PIN",
        ["hub_on"] = "This subscription supports Satra Hub", ["hub_register"] = "Connect this computer", ["hub_registered"] = "This computer is registered",
        ["last7"] = "Last 7 days", ["last30"] = "Last 30 days", ["data_per_day"] = "Data per day", ["hours_per_day"] = "Hours connected per day",
        ["local_limits"] = "Limits on this computer", ["server_limits"] = "Limits for the whole subscription", ["add_limit"] = "Add limit",
        ["what"] = "What", ["site"] = "Website", ["app"] = "Program", ["category"] = "Category", ["all_traffic"] = "All traffic", ["per_day"] = "per day", ["per_week"] = "per week", ["per_month"] = "per month",
        ["blocked"] = "Blocked", ["topup"] = "Add more", ["delete"] = "Delete", ["save"] = "Save", ["cancel"] = "Cancel",
        ["mode"] = "Connection mode", ["mode_proxy"] = "System proxy", ["mode_tun"] = "Whole computer (TUN)", ["mode_proxy_only"] = "Proxy only",
        ["proxy_addr"] = "Connect apps to SOCKS {0}  ·  HTTP {1}", ["reconnect_note"] = "Takes effect after reconnecting.",
        ["routing"] = "Routing", ["routing_iran"] = "Iran (direct for Iranian sites)", ["routing_russia"] = "Russia (direct for Russian sites)", ["routing_global"] = "Everything through VPN",
        ["language"] = "Language", ["advanced"] = "Advanced", ["show_advanced"] = "Show advanced settings", ["about"] = "About",
        ["auto_update"] = "Update subscriptions automatically", ["autostart"] = "Start with the computer",
        ["core_settings"] = "Core settings", ["dns"] = "DNS", ["routing_rules"] = "Routing rules", ["sub_settings"] = "Subscription details", ["backup"] = "Backup & restore", ["logs"] = "Logs",
        ["pick_pin"] = "Enter the owner PIN", ["set_pin"] = "Set an owner PIN (4+ characters)", ["wrong_pin"] = "Wrong PIN",
        ["online"] = "online", ["last_seen"] = "seen {0}", ["this_device"] = "This computer",
        ["limit_reached"] = "{0} reached its limit", ["added"] = "Added", ["nothing_clip"] = "Clipboard is empty",
        ["simple_mode_hint"] = "Simple mode keeps only what most people need.",
    };

    private static readonly Dictionary<string, string> Fa = new()
    {
        ["home"] = "خانه", ["servers"] = "سرورها", ["subs"] = "اشتراک‌ها", ["usage"] = "مصرف", ["settings"] = "تنظیمات",
        ["connected"] = "وصل است", ["disconnected"] = "وصل نیست", ["connecting"] = "در حال اتصال…", ["disconnecting"] = "در حال قطع…", ["failed"] = "اتصال برقرار نشد",
        ["tap_connect"] = "برای اتصال کلیک کنید", ["tap_disconnect"] = "برای قطع کلیک کنید", ["retry"] = "برای تلاش دوباره کلیک کنید",
        ["download"] = "دانلود", ["upload"] = "آپلود", ["today"] = "امروز", ["session"] = "این اتصال", ["total"] = "مجموع",
        ["ping"] = "پینگ", ["smart"] = "اتصال هوشمند", ["smart_desc"] = "سریع‌ترین سرور را خودش انتخاب می‌کند", ["no_server"] = "هنوز سروری ندارید",
        ["add"] = "افزودن", ["add_clip"] = "افزودن از کلیپ‌بورد", ["add_hint"] = "لینک اشتراک یا کانفیگ را کپی کنید و «افزودن» را بزنید؛ خودش تشخیص می‌دهد.",
        ["search"] = "جستجوی سرور", ["all"] = "همه", ["favorites"] = "علاقه‌مندی‌ها", ["ping_all"] = "پینگ همه", ["speed_all"] = "تست سرعت همه", ["sort_best"] = "بهترین‌ها اول",
        ["speed"] = "تست سرعت", ["use"] = "استفاده", ["active"] = "فعال", ["tunnel"] = "تونل", ["exit"] = "خروجی",
        ["update_all"] = "به‌روزرسانی همه", ["update"] = "به‌روزرسانی", ["remaining"] = "باقی‌مانده", ["days_left"] = "{0} روز مانده", ["expired"] = "منقضی شده", ["unlimited"] = "نامحدود", ["no_expiry"] = "بدون انقضا",
        ["support"] = "پشتیبانی", ["renew"] = "تمدید", ["devices"] = "دستگاه‌ها", ["limits"] = "محدودیت‌ها", ["owner_pin"] = "رمز مالک",
        ["hub_on"] = "این اشتراک از Satra Hub پشتیبانی می‌کند", ["hub_register"] = "ثبت این کامپیوتر", ["hub_registered"] = "این کامپیوتر ثبت شده است",
        ["last7"] = "۷ روز اخیر", ["last30"] = "۳۰ روز اخیر", ["data_per_day"] = "مصرف روزانه", ["hours_per_day"] = "ساعت اتصال در روز",
        ["local_limits"] = "محدودیت‌های این کامپیوتر", ["server_limits"] = "محدودیت برای کل اشتراک", ["add_limit"] = "افزودن محدودیت",
        ["what"] = "چه چیزی", ["site"] = "سایت", ["app"] = "برنامه", ["category"] = "دسته", ["all_traffic"] = "کل ترافیک", ["per_day"] = "در روز", ["per_week"] = "در هفته", ["per_month"] = "در ماه",
        ["blocked"] = "مسدود", ["topup"] = "افزایش", ["delete"] = "حذف", ["save"] = "ذخیره", ["cancel"] = "انصراف",
        ["mode"] = "حالت اتصال", ["mode_proxy"] = "پراکسی سیستم", ["mode_tun"] = "کل کامپیوتر (TUN)", ["mode_proxy_only"] = "فقط پراکسی",
        ["proxy_addr"] = "برنامه‌ها را به SOCKS {0}  ·  HTTP {1} وصل کنید", ["reconnect_note"] = "بعد از اتصال دوباره اعمال می‌شود.",
        ["routing"] = "مسیریابی", ["routing_iran"] = "ایران (سایت‌های ایرانی مستقیم)", ["routing_russia"] = "روسیه (سایت‌های روسی مستقیم)", ["routing_global"] = "همه از طریق VPN",
        ["language"] = "زبان", ["advanced"] = "پیشرفته", ["show_advanced"] = "نمایش تنظیمات پیشرفته", ["about"] = "درباره",
        ["auto_update"] = "به‌روزرسانی خودکار اشتراک‌ها", ["autostart"] = "اجرا با روشن شدن کامپیوتر",
        ["core_settings"] = "تنظیمات هسته", ["dns"] = "DNS", ["routing_rules"] = "قوانین مسیریابی", ["sub_settings"] = "جزئیات اشتراک", ["backup"] = "پشتیبان‌گیری", ["logs"] = "گزارش‌ها",
        ["pick_pin"] = "رمز مالک را وارد کنید", ["set_pin"] = "یک رمز مالک بگذارید (حداقل ۴ کاراکتر)", ["wrong_pin"] = "رمز اشتباه است",
        ["online"] = "آنلاین", ["last_seen"] = "آخرین بار {0}", ["this_device"] = "این کامپیوتر",
        ["limit_reached"] = "{0} به محدودیت رسید", ["added"] = "اضافه شد", ["nothing_clip"] = "کلیپ‌بورد خالی است",
        ["simple_mode_hint"] = "حالت ساده فقط چیزهایی را نشان می‌دهد که بیشتر افراد لازم دارند.",
    };

    private static readonly Dictionary<string, string> Ru = new()
    {
        ["home"] = "Главная", ["servers"] = "Серверы", ["subs"] = "Подписки", ["usage"] = "Трафик", ["settings"] = "Настройки",
        ["connected"] = "Подключено", ["disconnected"] = "Не подключено", ["connecting"] = "Подключение…", ["disconnecting"] = "Отключение…", ["failed"] = "Не удалось подключиться",
        ["tap_connect"] = "Нажмите, чтобы подключиться", ["tap_disconnect"] = "Нажмите, чтобы отключиться", ["retry"] = "Нажмите, чтобы повторить",
        ["download"] = "Загрузка", ["upload"] = "Отдача", ["today"] = "Сегодня", ["session"] = "Сеанс", ["total"] = "Всего",
        ["ping"] = "Пинг", ["smart"] = "Умное подключение", ["smart_desc"] = "Сам выбирает самый быстрый сервер", ["no_server"] = "Серверов пока нет",
        ["add"] = "Добавить", ["add_clip"] = "Добавить из буфера", ["add_hint"] = "Скопируйте ссылку подписки или конфиг и нажмите «Добавить».",
        ["search"] = "Поиск серверов", ["all"] = "Все", ["favorites"] = "Избранное", ["ping_all"] = "Пинг всех", ["speed_all"] = "Тест скорости всех", ["sort_best"] = "Лучшие сверху",
        ["speed"] = "Тест скорости", ["use"] = "Выбрать", ["active"] = "Активен", ["tunnel"] = "Туннель", ["exit"] = "Выход",
        ["update_all"] = "Обновить все", ["update"] = "Обновить", ["remaining"] = "осталось", ["days_left"] = "Осталось {0} дн.", ["expired"] = "Истекла", ["unlimited"] = "Безлимит", ["no_expiry"] = "Бессрочно",
        ["support"] = "Поддержка", ["renew"] = "Продлить", ["devices"] = "Устройства", ["limits"] = "Лимиты", ["owner_pin"] = "PIN владельца",
        ["hub_on"] = "Подписка поддерживает Satra Hub", ["hub_register"] = "Подключить этот компьютер", ["hub_registered"] = "Компьютер зарегистрирован",
        ["last7"] = "7 дней", ["last30"] = "30 дней", ["data_per_day"] = "Трафик по дням", ["hours_per_day"] = "Часы подключения",
        ["local_limits"] = "Лимиты на этом компьютере", ["server_limits"] = "Лимиты для всей подписки", ["add_limit"] = "Добавить лимит",
        ["what"] = "Что", ["site"] = "Сайт", ["app"] = "Программа", ["category"] = "Категория", ["all_traffic"] = "Весь трафик", ["per_day"] = "в день", ["per_week"] = "в неделю", ["per_month"] = "в месяц",
        ["blocked"] = "Заблокировано", ["topup"] = "Добавить", ["delete"] = "Удалить", ["save"] = "Сохранить", ["cancel"] = "Отмена",
        ["mode"] = "Режим", ["mode_proxy"] = "Системный прокси", ["mode_tun"] = "Весь компьютер (TUN)", ["mode_proxy_only"] = "Только прокси",
        ["proxy_addr"] = "Подключайте программы к SOCKS {0}  ·  HTTP {1}", ["reconnect_note"] = "Применится после переподключения.",
        ["routing"] = "Маршрутизация", ["routing_iran"] = "Иран (иранские сайты напрямую)", ["routing_russia"] = "Россия (российские сайты напрямую)", ["routing_global"] = "Всё через VPN",
        ["language"] = "Язык", ["advanced"] = "Дополнительно", ["show_advanced"] = "Показать расширенные настройки", ["about"] = "О программе",
        ["auto_update"] = "Автообновление подписок", ["autostart"] = "Запуск вместе с системой",
        ["core_settings"] = "Настройки ядра", ["dns"] = "DNS", ["routing_rules"] = "Правила маршрутизации", ["sub_settings"] = "Параметры подписки", ["backup"] = "Резервная копия", ["logs"] = "Журнал",
        ["pick_pin"] = "Введите PIN владельца", ["set_pin"] = "Задайте PIN владельца (от 4 символов)", ["wrong_pin"] = "Неверный PIN",
        ["online"] = "онлайн", ["last_seen"] = "был {0}", ["this_device"] = "Этот компьютер",
        ["limit_reached"] = "{0}: лимит исчерпан", ["added"] = "Добавлено", ["nothing_clip"] = "Буфер обмена пуст",
        ["simple_mode_hint"] = "Простой режим показывает только самое нужное.",
    };
}

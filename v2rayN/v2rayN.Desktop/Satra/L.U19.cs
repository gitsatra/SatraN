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
        ["adv_engine_section"] = ("Engine settings", "تنظیمات موتور", "Настройки ядра"),
        ["adv_rules_section"] = ("Routing & diagnostics", "مسیریابی و عیب‌یابی", "Маршруты и диагностика"),
        ["adv_logs_d"] = ("Live core and app messages at the level you choose", "پیام‌های زندهٔ هسته و برنامه در سطحی که انتخاب می‌کنید", "Сообщения ядра и приложения"),
        ["adv_expert"] = ("Expert editors", "ویرایشگرهای حرفه‌ای", "Экспертные редакторы"),
        ["adv_expert_d"] = ("The full v2rayN editors for custom routing rule sets, raw DNS JSON and subscription conversion. Everything above is the same settings in plain words.", "ویرایشگرهای کامل v2rayN برای مجموعه‌قوانین مسیریابی، DNS به‌صورت JSON و تبدیل اشتراک. همهٔ موارد بالا همین تنظیمات با زبان ساده است.", "Полные редакторы v2rayN: наборы правил, DNS в JSON, конвертация подписок. Всё выше — те же настройки простыми словами."),
        ["adv_log_folder"] = ("Open log folder", "باز کردن پوشهٔ لاگ", "Открыть папку журналов"),
        ["adv_autosave"] = ("Changes are saved right away and applied on the running connection. Nothing turns on by itself.", "تغییرات فوراً ذخیره و روی اتصال فعلی اعمال می‌شوند. چیزی خودبه‌خود روشن نمی‌شود.", "Изменения сохраняются сразу и применяются к текущему подключению. Ничего не включается само."),
        ["adv_default"] = ("Default", "پیش‌فرض", "По умолчанию"),
        ["adv_invalid"] = ("That value doesn't fit — kept the previous one", "این مقدار مجاز نیست — مقدار قبلی ماند", "Недопустимое значение — оставлено прежнее"),
    };
}

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// v1.9: user-started pings go through here. In whole-device (TUN) mode every test connection is itself routed into
/// the tunnel, so latencies come out inflated/skewed; we say so calmly and offer "turn off, test, turn back on".
/// Ports / system proxy do not route the test cores, so there is nothing to ask there. Never refreshes a page.
/// </summary>
public static class PingGuard
{
    private static bool _askedThisSession;
    public static bool Matters => SatraCore.I.Phase == ConnPhase.On && SatraState.I.Mode == ConnModes.Tun;

    public static Task One(ProfileItemModel p) => Run(() => PingEngine.I.PingOneAsync(p));
    public static Task All(IList<ProfileItemModel> items) => Run(() => PingEngine.I.PingAllAsync(items));

    private static async Task Run(Func<Task> ping)
    {
        if (!Matters || _askedThisSession && SatraState.I.PingChoice == "anyway") { await ping(); return; }
        if (SatraState.I.PingChoice == "pause") { await PausedAsync(ping); return; }
        var tcs = new TaskCompletionSource<string?>();
        Action close = () => { };
        var remember = new CheckBox { Content = Ui.T(L.S("pg_remember"), 12.5, FontWeight.Normal, P.Text2) };
        void Pick(string? v) { if (v != null && remember.IsChecked == true) { SatraState.I.PingChoice = v; SatraState.Save(); } close(); tcs.TrySetResult(v); }
        var body = Ui.V(14,
            Ui.H(12, Ui.T("◎", 22, FontWeight.Normal, P.Info), Ui.Caption(L.S("pg_body"))),
            Ui.Card(Ui.V(6, Ui.T(L.S("pg_pause"), 14, FontWeight.SemiBold), Ui.Caption(L.S("pg_pause_d"), P.Text3)), new Thickness(14, 10), P.Raised),
            remember,
            Ui.Row(Ui.Btn(L.S("pg_anyway"), Ui.Kind.Ghost, () => Pick("anyway"), size: 13),
                Ui.H(8, Ui.Btn(L.S("cancel"), Ui.Kind.Ghost, () => Pick(null), size: 13), Ui.Btn(L.S("pg_pause_btn"), Ui.Kind.Primary, () => Pick("pause"), size: 13))));
        close = SatraShell.I.Sheet(L.S("pg_title"), body, 480);
        var v = await tcs.Task;
        _askedThisSession = true;
        if (v == "anyway") await ping();
        else if (v == "pause") await PausedAsync(ping);
    }

    /// Off → measure on the real network → back on, on the same server, same mode.
    private static async Task PausedAsync(Func<Task> ping)
    {
        var c = SatraCore.I;
        c.Notify(L.S("pg_pausing"));
        await c.DisconnectAsync();
        try { await ping(); }
        finally { await c.ConnectAsync(pick: false); c.Notify(L.S("pg_restored")); }
    }
}

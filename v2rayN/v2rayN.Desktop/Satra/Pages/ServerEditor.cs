using ServiceLib.Handler.Fmt;
using System.Text.Json.Nodes;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>
/// Manual add / edit for every common protocol. Simple fields by default; transport, TLS/Reality and
/// a raw-link box under "Advanced". The form is converted to a standard share link and parsed by the
/// same importer used for subscriptions, so whatever is saved is exactly what an import would produce.
/// </summary>
public static class ServerEditor
{
    public static readonly string[] Protocols = ["vless", "vmess", "trojan", "ss", "hysteria2", "tuic", "wireguard", "socks", "http"];
    private static readonly string[] Nets = ["tcp", "ws", "grpc", "httpupgrade", "xhttp", "kcp"];
    private static readonly string[] Secs = ["none", "tls", "reality"];
    private static readonly string[] Fps = ["", "chrome", "firefox", "safari", "edge", "ios", "android", "random", "randomized"];
    private static readonly string[] SsMethods = ["aes-128-gcm", "aes-256-gcm", "chacha20-ietf-poly1305", "2022-blake3-aes-128-gcm", "2022-blake3-aes-256-gcm", "2022-blake3-chacha20-poly1305", "none"];
    private static readonly string[] Flows = ["", "xtls-rprx-vision", "xtls-rprx-vision-udp443"];
    private static readonly string[] Vsec = ["auto", "aes-128-gcm", "chacha20-poly1305", "none", "zero"];
    private static readonly string[] Ccs = ["bbr", "cubic", "new_reno"];

    public static Action<string, string>? DevSet;
    public static Action? DevSave;

    public static async void Open(ProfileItemModel? existing = null)
    {
        var v = new Dictionary<string, string> { ["proto"] = "vless", ["net"] = "tcp", ["sec"] = "none", ["port"] = "443", ["method"] = "aes-128-gcm", ["scy"] = "auto", ["aid"] = "0", ["cc"] = "bbr", ["mtu"] = "1280" };
        ProfileItem? orig = null;
        if (existing != null)
        {
            var eid = existing.IndexId;
            orig = await Task.Run(() => AppManager.Instance.GetProfileItem(eid));
            if (orig != null)
            {
                if (orig.ConfigType == EConfigType.HTTP)
                {
                    v["proto"] = "http"; v["name"] = orig.Remarks; v["addr"] = orig.Address; v["port"] = orig.Port.ToString(); v["user"] = orig.Username ?? ""; v["pass"] = orig.Password ?? "";
                }
                else if (FmtHandler.GetShareUri(orig) is { Length: > 0 } link) foreach (var kv in Parse(link)) v[kv.Key] = kv.Value;
            }
        }
        var body = new StackPanel { Spacing = 10 };
        var err = Ui.Caption("", P.Danger);
        var advOpen = existing != null && (v.GetValueOrDefault("sec") != "none" || v.GetValueOrDefault("net") != "tcp");
        TextBox raw = null!;
        var rawEdited = false;
        TextBox NewRaw()
        {
            var t = Ui.Input("vless://… / vmess://… / ss://…");
            t.TextChanged += (_, _) => { if (t.IsFocused) rawEdited = true; };
            return t;
        }
        raw = NewRaw();
        Action close = () => { };

        Control Field(string label, string key, string hint = "", bool secret = false)
        {
            var tb = Ui.Input(hint, v.GetValueOrDefault(key) ?? "");
            if (secret) tb.PasswordChar = '•';
            tb.TextChanged += (_, _) => { v[key] = tb.Text ?? ""; if (!rawEdited) raw.Text = Build(v) ?? ""; };
            return Ui.V(4, Ui.Caption(label, P.Text2), tb);
        }
        Control Pick(string label, string key, string[] items, bool rebuild = false, Action? after = null)
        {
            var cur = v.GetValueOrDefault(key) ?? "";
            var cb = new ComboBox { ItemsSource = items.Select(i => i == "" ? "—" : i).ToArray(), MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
            cb.SelectedIndex = Math.Max(0, Array.IndexOf(items, cur));
            if (Array.IndexOf(items, cur) < 0 && cur != "") { cb.ItemsSource = items.Append(cur).ToArray(); cb.SelectedIndex = items.Length; }
            cb.SelectionChanged += (_, _) =>
            {
                var s = cb.SelectedItem as string ?? "";
                v[key] = s == "—" ? "" : s;
                if (rebuild) Dispatcher.UIThread.Post(Render); else if (!rawEdited) raw.Text = Build(v) ?? "";
            };
            return Ui.V(4, Ui.Caption(label, P.Text2), cb);
        }
        Control Check(string label, string key)
        {
            var sw = Ui.Switch(v.GetValueOrDefault(key) == "1", on => { v[key] = on ? "1" : ""; if (!rawEdited) raw.Text = Build(v) ?? ""; });
            return Ui.Row(Ui.T(label, 13.5), sw);
        }
        static Grid Two(Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
            g.Children.Add(a); Grid.SetColumn(b, 2); g.Children.Add(b);
            return g;
        }

        void Render()
        {
            body.Children.Clear();
            raw = NewRaw();
            var p = v["proto"];
            body.Children.Add(Two(Pick(L.S("ed_protocol"), "proto", Protocols, true), Field(L.S("ed_name"), "name", L.S("ed_name_hint"))));
            var addr = Field(L.S("ed_address"), "addr", "example.com / 1.2.3.4");
            var port = Field(L.S("ed_port"), "port", "443");
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,120") };
            g.Children.Add(addr); Grid.SetColumn(port, 2); g.Children.Add(port);
            body.Children.Add(g);
            switch (p)
            {
                case "vless":
                    body.Children.Add(Field("UUID", "id", "xxxxxxxx-xxxx-…"));
                    body.Children.Add(Pick("Flow", "flow", Flows));
                    break;
                case "vmess":
                    body.Children.Add(Field("UUID", "id", "xxxxxxxx-xxxx-…"));
                    body.Children.Add(Two(Pick(L.S("ed_encryption"), "scy", Vsec), Field("alterId", "aid", "0")));
                    break;
                case "trojan": body.Children.Add(Field(L.S("ed_password"), "pass", "", true)); break;
                case "ss": body.Children.Add(Two(Pick(L.S("ed_method"), "method", SsMethods), Field(L.S("ed_password"), "pass", "", true))); break;
                case "hysteria2":
                    body.Children.Add(Field(L.S("ed_password"), "pass", "", true));
                    body.Children.Add(Two(Field("SNI", "sni", "example.com"), Field(L.S("ed_hop_ports"), "mport", "20000-30000")));
                    body.Children.Add(Field(L.S("ed_obfs_pass"), "obfs", "salamander"));
                    body.Children.Add(Check(L.S("ed_insecure"), "insecure"));
                    break;
                case "tuic":
                    body.Children.Add(Two(Field("UUID", "id"), Field(L.S("ed_password"), "pass", "", true)));
                    body.Children.Add(Two(Field("SNI", "sni"), Pick(L.S("ed_congestion"), "cc", Ccs)));
                    body.Children.Add(Field("ALPN", "alpn", "h3"));
                    body.Children.Add(Check(L.S("ed_insecure"), "insecure"));
                    break;
                case "wireguard":
                    body.Children.Add(Field(L.S("ed_wg_priv"), "id", "", true));
                    body.Children.Add(Field(L.S("ed_wg_pub"), "pub"));
                    body.Children.Add(Two(Field(L.S("ed_wg_addr"), "wgaddr", "10.0.0.2/32"), Field("MTU", "mtu", "1280")));
                    body.Children.Add(Two(Field("Reserved", "reserved", "0,0,0"), Field("Pre-shared key", "psk")));
                    break;
                case "socks":
                case "http":
                    body.Children.Add(Two(Field(L.S("ed_user"), "user"), Field(L.S("ed_password"), "pass", "", true)));
                    break;
            }
            if (p is "vless" or "vmess" or "trojan")
            {
                var adv = new StackPanel { Spacing = 10, IsVisible = advOpen };
                var toggle = Ui.Btn((advOpen ? "▾ " : "▸ ") + L.S("ed_transport_tls"), Ui.Kind.Ghost, null, size: 13);
                toggle.Click += (_, _) => { advOpen = !advOpen; adv.IsVisible = advOpen; toggle.Content = (advOpen ? "▾ " : "▸ ") + L.S("ed_transport_tls"); };
                body.Children.Add(toggle);
                var net = v["net"];
                adv.Children.Add(Two(Pick(L.S("ed_transport"), "net", Nets, true), Pick(L.S("ed_security"), "sec", Secs, true)));
                if (net is "ws" or "httpupgrade" or "xhttp" || (net == "tcp" && v.GetValueOrDefault("htype") == "http"))
                    adv.Children.Add(Two(Field("Host", "host", "cdn.example.com"), Field("Path", "path", "/")));
                if (net == "tcp") adv.Children.Add(Pick(L.S("ed_header"), "htype", ["none", "http"], true));
                if (net == "grpc") adv.Children.Add(Two(Field("serviceName", "path"), Pick("Mode", "mode", ["gun", "multi"])));
                if (net == "xhttp") adv.Children.Add(Pick("Mode", "mode", ["auto", "packet-up", "stream-up", "stream-one"]));
                if (net == "kcp") adv.Children.Add(Two(Pick(L.S("ed_header"), "htype", ["none", "srtp", "utp", "wechat-video", "dtls", "wireguard"]), Field("Seed", "path")));
                var sec = v["sec"];
                if (sec != "none")
                {
                    adv.Children.Add(Two(Field("SNI", "sni", "example.com"), Pick("Fingerprint", "fp", Fps)));
                    if (sec == "tls") { adv.Children.Add(Field("ALPN", "alpn", "h2,http/1.1")); adv.Children.Add(Check(L.S("ed_insecure"), "insecure")); }
                    if (sec == "reality")
                    {
                        adv.Children.Add(Field("Public key (pbk)", "pbk"));
                        adv.Children.Add(Two(Field("Short ID (sid)", "sid"), Field("SpiderX", "spx", "/")));
                    }
                }
                body.Children.Add(adv);
            }
            raw.Text = Build(v) ?? "";
            rawEdited = false;
            body.Children.Add(Ui.V(4, Ui.Caption(L.S("ed_raw"), P.Text3), raw));
            body.Children.Add(err);
            var save = Ui.Btn(L.S("save"), Ui.Kind.Primary, () => _ = SaveAsync());
            var cancel = Ui.Btn(L.S("cancel"), Ui.Kind.Ghost, () => close());
            body.Children.Add(Ui.H(10, save, cancel));
        }

        async Task SaveAsync()
        {
            err.Text = "";
            try
            {
                ProfileItem? item;
                if (v["proto"] == "http")
                {
                    if (!int.TryParse(v.GetValueOrDefault("port"), out var hp) || string.IsNullOrWhiteSpace(v.GetValueOrDefault("addr"))) { err.Text = L.S("ed_invalid"); return; }
                    item = new ProfileItem { ConfigType = EConfigType.HTTP, Remarks = v.GetValueOrDefault("name") ?? "", Address = v["addr"].Trim(), Port = hp, Username = v.GetValueOrDefault("user") ?? "", Password = v.GetValueOrDefault("pass") ?? "" };
                }
                else
                {
                    var link = (rawEdited ? raw.Text : Build(v))?.Trim() ?? "";
                    item = FmtHandler.ResolveConfig(link, out var msg);
                    if (item == null || string.IsNullOrWhiteSpace(item.Address) || item.Port <= 0) { err.Text = L.S("ed_invalid") + (string.IsNullOrEmpty(msg) ? "" : " · " + msg); return; }
                    if (!rawEdited && v.GetValueOrDefault("insecure") == "1") item.AllowInsecure = Global.StringTrue;
                }
                var cfg = AppManager.Instance.Config;
                if (orig != null)
                {
                    item.Subid = orig.Subid;
                    item.IsSub = orig.IsSub;
                    if (orig.ConfigType == item.ConfigType) item.IndexId = orig.IndexId;
                    else await ConfigHandler.RemoveServers(cfg, [orig]);
                }
                else item.Subid = SatraCore.I.Profiles.SelectedSub?.Id is { Length: > 0 } sid ? sid : "";
                if (string.IsNullOrWhiteSpace(item.Remarks)) item.Remarks = $"{item.Address}:{item.Port}";
                if (await ConfigHandler.AddServer(cfg, item) != 0) { err.Text = L.S("ed_invalid"); return; }
                await SatraCore.I.Profiles.RefreshServers();
                SatraCore.I.RaiseChanged();
                SatraShell.I.Toast(L.S(orig != null ? "ed_saved" : "added"));
                close();
            }
            catch (Exception ex) { err.Text = ex.Message; }
        }

        DevSet = (k, val) => { v[k] = val; Render(); };
        DevSave = () => _ = SaveAsync();
        Render();
        close = SatraShell.I.Sheet(L.S(existing != null ? "ed_edit" : "ed_add"), body, 600);
    }

    // ---------- share-link <-> fields ----------

    private static string E(string? s) => Uri.EscapeDataString(s ?? "");

    public static string? Build(Dictionary<string, string> v)
    {
        string G(string k) => v.GetValueOrDefault(k)?.Trim() ?? "";
        var p = G("proto");
        var host = G("addr");
        if (host.Contains(':') && !host.StartsWith('[')) host = $"[{host}]";
        var hp = $"{host}:{G("port")}";
        var frag = G("name").Length > 0 ? "#" + E(G("name")) : "";
        var q = new List<string>();
        void Add(string k, string val) { if (val.Length > 0) q.Add($"{k}={E(val)}"); }
        void Stream()
        {
            var net = G("net"); if (net == "") net = "tcp";
            Add("type", net);
            var sec = G("sec"); if (sec == "") sec = "none";
            Add("security", sec);
            if (net == "tcp") { if (G("htype") == "http") { Add("headerType", "http"); Add("host", G("host")); Add("path", G("path")); } }
            else if (net == "kcp") { Add("headerType", G("htype").Length > 0 ? G("htype") : "none"); Add("seed", G("path")); }
            else if (net == "grpc") { Add("serviceName", G("path")); Add("mode", G("mode")); }
            else { Add("host", G("host")); Add("path", G("path")); if (net == "xhttp") Add("mode", G("mode")); }
            if (sec != "none") { Add("sni", G("sni")); Add("fp", G("fp")); }
            if (sec == "tls") { Add("alpn", G("alpn")); if (G("insecure") == "1") { Add("allowInsecure", "1"); Add("insecure", "1"); } }
            if (sec == "reality") { Add("pbk", G("pbk")); Add("sid", G("sid")); Add("spx", G("spx")); }
        }
        string Q() => q.Count > 0 ? "?" + string.Join("&", q) : "";
        switch (p)
        {
            case "vless":
                Add("encryption", "none"); Add("flow", G("flow")); Stream();
                return $"vless://{E(G("id"))}@{hp}{Q()}{frag}";
            case "trojan":
                Add("flow", G("flow")); Stream();
                return $"trojan://{E(G("pass"))}@{hp}{Q()}{frag}";
            case "vmess":
            {
                var net = G("net"); if (net == "") net = "tcp";
                var sec = G("sec"); if (sec == "none") sec = "";
                var o = new JsonObject
                {
                    ["v"] = "2", ["ps"] = G("name"), ["add"] = G("addr"), ["port"] = G("port"), ["id"] = G("id"), ["aid"] = G("aid").Length > 0 ? G("aid") : "0",
                    ["scy"] = G("scy").Length > 0 ? G("scy") : "auto", ["net"] = net,
                    ["type"] = net is "tcp" or "kcp" ? (G("htype").Length > 0 ? G("htype") : "none") : net is "grpc" or "xhttp" ? G("mode") : "none",
                    ["host"] = G("host"), ["path"] = G("path"), ["tls"] = sec, ["sni"] = G("sni"), ["alpn"] = G("alpn"), ["fp"] = G("fp"),
                };
                if (sec == "reality") { o["pbk"] = G("pbk"); o["sid"] = G("sid"); o["spx"] = G("spx"); }
                return "vmess://" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(o.ToJsonString()));
            }
            case "ss":
                return $"ss://{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{G("method")}:{G("pass")}")).TrimEnd('=').Replace('+', '-').Replace('/', '_')}@{hp}{frag}";
            case "hysteria2":
                Add("sni", G("sni")); if (G("insecure") == "1") Add("insecure", "1");
                if (G("obfs").Length > 0) { Add("obfs", "salamander"); Add("obfs-password", G("obfs")); }
                Add("mport", G("mport"));
                return $"hysteria2://{E(G("pass"))}@{hp}{Q()}{frag}";
            case "tuic":
                Add("sni", G("sni")); Add("alpn", G("alpn")); Add("congestion_control", G("cc")); if (G("insecure") == "1") Add("allow_insecure", "1");
                return $"tuic://{E(G("id"))}:{E(G("pass"))}@{hp}{Q()}{frag}";
            case "wireguard":
                Add("publickey", G("pub")); Add("address", G("wgaddr")); Add("reserved", G("reserved")); Add("mtu", G("mtu")); Add("presharedkey", G("psk"));
                return $"wireguard://{E(G("id"))}@{hp}{Q()}{frag}";
            case "socks":
            {
                var auth = G("user").Length > 0 ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{G("user")}:{G("pass")}")) + "@" : "";
                return $"socks://{auth}{hp}{frag}";
            }
        }
        return null;
    }

    public static Dictionary<string, string> Parse(string link)
    {
        var v = new Dictionary<string, string>();
        try
        {
            if (link.StartsWith("vmess://"))
            {
                var b = link[8..].Trim();
                b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
                var o = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b)))!.AsObject();
                string J(string k) => o[k]?.ToString() ?? "";
                v["proto"] = "vmess"; v["name"] = J("ps"); v["addr"] = J("add"); v["port"] = J("port"); v["id"] = J("id"); v["aid"] = J("aid"); v["scy"] = J("scy");
                v["net"] = J("net") is { Length: > 0 } n ? (n == "raw" ? "tcp" : n) : "tcp";
                var t = J("type"); if (v["net"] is "tcp" or "kcp") v["htype"] = t; else if (v["net"] is "grpc" or "xhttp") v["mode"] = t;
                v["host"] = J("host"); v["path"] = J("path"); v["sec"] = J("tls") is { Length: > 0 } s ? s : "none"; v["sni"] = J("sni"); v["alpn"] = J("alpn"); v["fp"] = J("fp");
                v["pbk"] = J("pbk"); v["sid"] = J("sid"); v["spx"] = J("spx");
                return v;
            }
            var u = new Uri(link);
            var scheme = u.Scheme.ToLowerInvariant();
            v["proto"] = scheme switch { "hy2" => "hysteria2", "socks5" => "socks", _ => scheme };
            v["addr"] = u.Host.Trim('[', ']'); v["port"] = u.Port > 0 ? u.Port.ToString() : "443";
            v["name"] = Uri.UnescapeDataString(u.Fragment.TrimStart('#'));
            var q = System.Web.HttpUtility.ParseQueryString(u.Query);
            string Qv(string k) => q[k] ?? "";
            var ui = Uri.UnescapeDataString(u.UserInfo);
            switch (v["proto"])
            {
                case "vless": v["id"] = ui; v["flow"] = Qv("flow"); break;
                case "trojan": v["pass"] = ui; break;
                case "ss":
                {
                    var dec = ui.Contains(':') ? ui : Base64Any(ui);
                    var i = dec.IndexOf(':'); if (i > 0) { v["method"] = dec[..i]; v["pass"] = dec[(i + 1)..]; }
                    break;
                }
                case "hysteria2": v["pass"] = ui; v["obfs"] = Qv("obfs-password"); v["mport"] = Qv("mport"); break;
                case "tuic": { var i = ui.IndexOf(':'); v["id"] = i >= 0 ? ui[..i] : ui; v["pass"] = i >= 0 ? ui[(i + 1)..] : ""; v["cc"] = Qv("congestion_control"); if (Qv("allow_insecure") == "1") v["insecure"] = "1"; break; }
                case "wireguard": v["id"] = ui; v["pub"] = Qv("publickey"); v["wgaddr"] = Qv("address"); v["reserved"] = Qv("reserved"); v["mtu"] = Qv("mtu"); v["psk"] = Qv("presharedkey"); break;
                case "socks":
                {
                    var dec = ui.Contains(':') ? ui : Base64Any(ui);
                    var i = dec.IndexOf(':'); if (i > 0) { v["user"] = dec[..i]; v["pass"] = dec[(i + 1)..]; }
                    break;
                }
            }
            var net = Qv("type"); v["net"] = net is "" or "raw" ? "tcp" : net;
            v["sec"] = Qv("security") is { Length: > 0 } sec ? sec : "none";
            v["htype"] = Qv("headerType"); v["host"] = Qv("host");
            v["path"] = v["net"] == "grpc" ? Qv("serviceName") : v["net"] == "kcp" ? Qv("seed") : Qv("path");
            v["mode"] = Qv("mode"); v["sni"] = Qv("sni"); v["fp"] = Qv("fp"); v["alpn"] = Qv("alpn");
            v["pbk"] = Qv("pbk"); v["sid"] = Qv("sid"); v["spx"] = Qv("spx");
            if (Qv("allowInsecure") == "1" || Qv("insecure") == "1") v["insecure"] = "1";
        }
        catch { }
        return v;
    }

    private static string Base64Any(string s)
    {
        try
        {
            s = s.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        catch { return s; }
    }

    public static async void ShowQr(ProfileItemModel pm)
    {
        var pid = pm.IndexId;
        var prof = await Task.Run(() => AppManager.Instance.GetProfileItem(pid));
        var link = prof != null ? FmtHandler.GetShareUri(prof) : null;
        if (string.IsNullOrEmpty(link)) { SatraShell.I.Toast(L.S("ed_no_link")); return; }
        var png = QRCodeUtils.GenQRCode(link);
        var body = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        if (png != null) body.Children.Add(new Border { Background = Brushes.White, CornerRadius = Ui.RCtl, Padding = new Thickness(10), Child = new Image { Source = new Bitmap(new MemoryStream(png)), Width = 300, Height = 300 } });
        body.Children.Add(Ui.Btn(L.S("ed_copy_link"), Ui.Kind.Tinted, () => _ = SatraShell.I.Copy(link), "⧉", 13));
        SatraShell.I.Sheet(Ui.CleanName(pm.Remarks), body, 380);
    }
}

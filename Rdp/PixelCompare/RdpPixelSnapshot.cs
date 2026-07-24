using System.Text.Json;

namespace Netvium.Rdp.PixelCompare
{
    public sealed class RdpPixelSnapshot
    {
        public string Client { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public int FormClientWidth { get; set; }
        public int FormClientHeight { get; set; }
        public int HostClientWidth { get; set; }
        public int HostClientHeight { get; set; }
        public int ActiveXWidth { get; set; }
        public int ActiveXHeight { get; set; }
        public int DesktopWidth { get; set; }
        public int DesktopHeight { get; set; }
        public int ColorDepth { get; set; }
        public bool SmartSizing { get; set; }
        public int DesktopScaleFactor { get; set; }
        public int DeviceScaleFactor { get; set; }
        public int PerformanceFlags { get; set; }
        public int BitmapPersistence { get; set; }
        public int Compress { get; set; }
        public int AllowFontSmoothing { get; set; }
        public int EnableDesktopComposition { get; set; }
        public int DisableWallpaper { get; set; }
        public int DisableThemes { get; set; }
        public string ResizeMethod { get; set; } = string.Empty;

        public static RdpPixelSnapshot CaptureFromActiveX(
            string client,
            string phase,
            int formClientW,
            int formClientH,
            System.Windows.Forms.Control host,
            dynamic ax,
            dynamic? adv,
            string resizeMethod)
        {
            var snap = new RdpPixelSnapshot
            {
                Client = client,
                Phase = phase,
                FormClientWidth = formClientW,
                FormClientHeight = formClientH,
                HostClientWidth = host.ClientSize.Width,
                HostClientHeight = host.ClientSize.Height,
                ActiveXWidth = (int)ax.Width,
                ActiveXHeight = (int)ax.Height,
                DesktopWidth = (int)ax.DesktopWidth,
                DesktopHeight = (int)ax.DesktopHeight,
                ColorDepth = (int)ax.ColorDepth,
                ResizeMethod = resizeMethod,
            };

            if (adv == null)
                return snap;

            try { snap.SmartSizing = (bool)((dynamic)adv).SmartSizing; } catch { /* optional */ }
            TryReadAdv((object)adv, "DesktopScaleFactor", v => snap.DesktopScaleFactor = v);
            TryReadAdv((object)adv, "DeviceScaleFactor", v => snap.DeviceScaleFactor = v);
            TryReadAdv((object)adv, "PerformanceFlags", v => snap.PerformanceFlags = v);
            TryReadAdv((object)adv, "BitmapPersistence", v => snap.BitmapPersistence = v);
            TryReadAdv((object)adv, "BitmapPeristence", v => snap.BitmapPersistence = v);
            TryReadAdv((object)adv, "Compress", v => snap.Compress = v);
            TryReadAdv((object)adv, "AllowFontSmoothing", v => snap.AllowFontSmoothing = v);
            TryReadAdv((object)adv, "EnableDesktopComposition", v => snap.EnableDesktopComposition = v);
            TryReadAdv((object)adv, "DisableWallpaper", v => snap.DisableWallpaper = v);
            TryReadAdv((object)adv, "DisableThemes", v => snap.DisableThemes = v);
            return snap;
        }

        private static void TryReadAdv(object adv, string name, Action<int> apply)
        {
            try
            {
                apply((int)adv.GetType().InvokeMember(
                    name,
                    System.Reflection.BindingFlags.GetProperty,
                    null,
                    adv,
                    null)!);
            }
            catch { /* optional */ }
        }

        public string ToJson() =>
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    public sealed class RdpPixelDiff
    {
        public string Field { get; init; } = string.Empty;
        public string Netvium { get; init; } = string.Empty;
        public string MRemoteNg { get; init; } = string.Empty;
        public bool Match { get; init; }
    }

    public static class RdpPixelSnapshotComparer
    {
        public static IReadOnlyList<RdpPixelDiff> Compare(RdpPixelSnapshot netvium, RdpPixelSnapshot mremote)
        {
            var diffs = new List<RdpPixelDiff>();
            CompareField(diffs, "FormClientWidth", netvium.FormClientWidth, mremote.FormClientWidth);
            CompareField(diffs, "FormClientHeight", netvium.FormClientHeight, mremote.FormClientHeight);
            CompareField(diffs, "HostClientWidth", netvium.HostClientWidth, mremote.HostClientWidth);
            CompareField(diffs, "HostClientHeight", netvium.HostClientHeight, mremote.HostClientHeight);
            CompareField(diffs, "ActiveXWidth", netvium.ActiveXWidth, mremote.ActiveXWidth);
            CompareField(diffs, "ActiveXHeight", netvium.ActiveXHeight, mremote.ActiveXHeight);
            CompareField(diffs, "DesktopWidth", netvium.DesktopWidth, mremote.DesktopWidth);
            CompareField(diffs, "DesktopHeight", netvium.DesktopHeight, mremote.DesktopHeight);
            CompareField(diffs, "ColorDepth", netvium.ColorDepth, mremote.ColorDepth);
            CompareField(diffs, "SmartSizing", netvium.SmartSizing, mremote.SmartSizing);
            CompareField(diffs, "DesktopScaleFactor", netvium.DesktopScaleFactor, mremote.DesktopScaleFactor);
            CompareField(diffs, "DeviceScaleFactor", netvium.DeviceScaleFactor, mremote.DeviceScaleFactor);
            CompareField(diffs, "PerformanceFlags", netvium.PerformanceFlags, mremote.PerformanceFlags);
            CompareField(diffs, "BitmapPersistence", netvium.BitmapPersistence, mremote.BitmapPersistence);
            CompareField(diffs, "Compress", netvium.Compress, mremote.Compress);
            CompareField(diffs, "AllowFontSmoothing", netvium.AllowFontSmoothing, mremote.AllowFontSmoothing);
            CompareField(diffs, "EnableDesktopComposition", netvium.EnableDesktopComposition, mremote.EnableDesktopComposition);
            CompareField(diffs, "DisableWallpaper", netvium.DisableWallpaper, mremote.DisableWallpaper);
            CompareField(diffs, "DisableThemes", netvium.DisableThemes, mremote.DisableThemes);
            return diffs;
        }

        private static void CompareField<T>(List<RdpPixelDiff> diffs, string field, T a, T b)
        {
            diffs.Add(new RdpPixelDiff
            {
                Field = field,
                Netvium = a?.ToString() ?? "null",
                MRemoteNg = b?.ToString() ?? "null",
                Match = EqualityComparer<T>.Default.Equals(a, b),
            });
        }

        public static string FormatReport(
            RdpPixelSnapshot netvium,
            RdpPixelSnapshot mremote,
            IReadOnlyList<RdpPixelDiff> diffs,
            RdpSystemDpiSnapshot dpi)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== RDP PIXEL-LEVEL COMPARISON (Netvium vs mRemoteNG profile) ===");
            sb.AppendLine($"Server phase: {netvium.Phase}");
            sb.AppendLine($"DPI: WPF={dpi.WpfDpiScaleX:F3}x{dpi.WpfDpiScaleY:F3} Effective={dpi.EffectiveDpiX} WindowsScale={dpi.WindowsScalingPercent}%");
            sb.AppendLine();
            sb.AppendLine($"{"Field",-28} {"Netvium",-14} {"mRemoteNG",-14} Match");
            sb.AppendLine(new string('-', 70));
            foreach (var d in diffs)
                sb.AppendLine($"{d.Field,-28} {d.Netvium,-14} {d.MRemoteNg,-14} {(d.Match ? "OK" : "DIFF")}");

            var mismatch = diffs.Count(d => !d.Match);
            sb.AppendLine();
            sb.AppendLine($"Total diffs: {mismatch}/{diffs.Count}");
            sb.AppendLine(mismatch == 0
                ? "VERDICT: Pixel-level parity — no measured differences."
                : "VERDICT: NOT PARITY — fix not proven. See DIFF rows above.");

            sb.AppendLine();
            sb.AppendLine("Netvium JSON:");
            sb.AppendLine(netvium.ToJson());
            sb.AppendLine("mRemoteNG-profile JSON:");
            sb.AppendLine(mremote.ToJson());
            return sb.ToString();
        }
    }
}

using System.Windows;

namespace Netvium;

public class ScanItem
{
    public string Icon      { get; set; } = "";
    public string Label     { get; set; } = "";
    public string Detail    { get; set; } = "";
    public bool   IsRisk    { get; set; }

    private static System.Windows.Media.SolidColorBrush Brush(byte r, byte g, byte b)
        => new(System.Windows.Media.Color.FromRgb(r, g, b));

    public System.Windows.Media.Brush RowBackground
        => IsRisk ? Brush(0x2D, 0x15, 0x15) : Brush(0x1E, 0x29, 0x3B);

    public System.Windows.Media.Brush DetailColor
        => IsRisk ? Brush(0xFC, 0xA5, 0xA5) : Brush(0xCB, 0xD5, 0xE1);

    public string BadgeText => IsRisk ? "RISK" : "OK";

    public System.Windows.Media.Brush BadgeBg
        => IsRisk ? Brush(0x7F, 0x1D, 0x1D) : Brush(0x14, 0x53, 0x2D);

    public System.Windows.Media.Brush BadgeFg
        => IsRisk ? Brush(0xFC, 0xA5, 0xA5) : Brush(0x4A, 0xDE, 0x80);
}

public partial class QuickScanDialog : Window
{
    private static System.Windows.Media.SolidColorBrush WBrush(byte r, byte g, byte b)
        => new(System.Windows.Media.Color.FromRgb(r, g, b));

    public QuickScanDialog(string serverName, string serverIp, List<ScanItem> items, int checks, int risks)
    {
        InitializeComponent();

        TitleLabel.Text        = $"Quick Scan — {serverName}";
        ServerNameLabel.Text   = serverName;
        ServerIpLabel.Text     = serverIp;
        CheckCountText.Text    = checks.ToString();
        RiskCountText.Text     = risks.ToString();
        ChecksList.ItemsSource = items;

        if (risks > 0)
        {
            OverallBadge.Background     = WBrush(0x7F, 0x1D, 0x1D);
            OverallBadgeText.Text       = $"{risks} RİSK";
            OverallBadgeText.Foreground = WBrush(0xFC, 0xA5, 0xA5);
            RiskCountText.Foreground    = WBrush(0xF8, 0x71, 0x71);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();
}

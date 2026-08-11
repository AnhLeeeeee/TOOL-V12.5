namespace ToolTikTokV11.Models;

public sealed class AppSettings
{
    public string XPathPoint1 { get; set; } = "";
    public string XPathPoint2 { get; set; } = "";
    public string XPathPeriodicAction { get; set; } = "";
    public string XPathHoverArea { get; set; } = "";
    public bool SwitchNeedsHover { get; set; }
    public bool UseArrowDownForLiveSwitch { get; set; } = true;
    public int HoverDelayMs { get; set; } = 350;
    public int DelayMinMs { get; set; } = 700;
    public int DelayMaxMs { get; set; } = 1200;
    public int LoopMinMs { get; set; } = 700;
    public int LoopMaxMs { get; set; } = 1200;
    // Các giá trị này đã có trong cấu hình V11.5.  Giữ chúng theo từng profile
    // để không dùng chung nhịp scan giữa các Worker.
    public bool AfterClickScanEnabled { get; set; } = true;
    public int AfterClickScanMs { get; set; } = 1000;
    public bool AfterEnterScanEnabled { get; set; } = true;
    public int AfterEnterScanMs { get; set; } = 1000;
    public int PeriodicF5Minutes { get; set; }
    public int TimerStopMinutes { get; set; }
    public int ChromePort { get; set; } = 9222;
    public string ChromeProfileDir { get; set; } = "";
    public bool StrictXPathOnly { get; set; } = true;
    public string ChromeMode { get; set; } = "visible"; // visible | background
    public List<ScanRegion> ScanRegions { get; set; } = [];
    public ViewerSettings Viewer { get; set; } = new();
    public OldLiveSettings OldLive { get; set; } = new();
}

public sealed class ViewerSettings
{
    public bool Enabled { get; set; }
    public string XPath { get; set; } = "";
    public int Threshold { get; set; } = 100;
    public int ConfirmLow { get; set; } = 2;
    public int IntervalSec { get; set; } = 120;
    public int WaitAfterF5Sec { get; set; } = 2;
    public int MaxF5 { get; set; } = 100;
    public int OcrRetries { get; set; } = 3;
    public double RX1 { get; set; }
    public double RY1 { get; set; }
    public double RX2 { get; set; }
    public double RY2 { get; set; }
}

public sealed class OldLiveSettings
{
    public bool Enabled { get; set; }
    public string XPath { get; set; } = "";
    public string ActionXPath { get; set; } = "";
    public int KeepMinutes { get; set; } = 10;
    public int Variation { get; set; } = 55;
    public double RX1 { get; set; }
    public double RY1 { get; set; }
    public double RX2 { get; set; }
    public double RY2 { get; set; }
}

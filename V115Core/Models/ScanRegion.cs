namespace ToolTikTokV11.Models;

public sealed class ScanRegion
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool AfterClick { get; set; } = true;
    public bool PeriodicEnabled { get; set; }
    public int PeriodicMinutes { get; set; } = 10;
    public int ConsecutiveMax { get; set; } = 1;
    public string Action { get; set; } = "F5"; // F5 = Down + F5, CLICK_F5, STOP
    public int Variation { get; set; } = 55;
    public List<string> Images { get; set; } = [];
    public string ScanXPath { get; set; } = "";
    public string ActionXPath { get; set; } = "";
    public double RX1 { get; set; }
    public double RY1 { get; set; }
    public double RX2 { get; set; }
    public double RY2 { get; set; }
    public DateTime NextPeriodicAt { get; set; } = DateTime.MaxValue;

    public override string ToString() => Name;
}

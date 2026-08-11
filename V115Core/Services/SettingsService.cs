using System.IO.Compression;
using ToolTikTokV11.Models;
using ToolTikTokV11.Utils;
using System.Text;

namespace ToolTikTokV11.Services;

public sealed class SettingsService
{
    readonly string _baseDir;
    public string BaseDir => _baseDir;
    public string IniPath => Path.Combine(_baseDir, "auto_chrome.ini");
    public string ContentPath => Path.Combine(_baseDir, "auto_chrome_noidung.txt");

    public SettingsService(string baseDir) => _baseDir = baseDir;

    public AppSettings Load()
    {
        var ini = new IniFile(IniPath);
        var fixedProfileDir = RuntimeDataPath.ResolveChromeProfilePath(_baseDir);
        var s = new AppSettings
        {
            XPathPoint1 = ini.Get("XPath", "Point1"),
            XPathPoint2 = ini.Get("XPath", "Point2"),
            XPathPeriodicAction = ini.Get("XPath", "PeriodicAction"),
            XPathHoverArea = ini.Get("XPath", "HoverArea"),
            SwitchNeedsHover = ini.GetBool("XPath", "SwitchNeedsHover", false),
            UseArrowDownForLiveSwitch = ini.GetBool("V11", "UseArrowDownForLiveSwitch", true),
            HoverDelayMs = Math.Clamp(ini.GetInt("XPath", "HoverDelayMs", 350), 0, 3000),
            DelayMinMs = ini.GetInt("ThoiGian", "DelayMin", 700),
            DelayMaxMs = ini.GetInt("ThoiGian", "DelayMax", 1200),
            LoopMinMs = ini.GetInt("ThoiGian", "LoopMin", 700),
            LoopMaxMs = ini.GetInt("ThoiGian", "LoopMax", 1200),
            AfterClickScanEnabled = ini.GetBool("ThoiGian", "AfterClickScanEnabled", true),
            AfterClickScanMs = Math.Clamp(ini.GetInt("ThoiGian", "AfterClickScanMs", 1000), 700, 2000),
            AfterEnterScanEnabled = ini.GetBool("ThoiGian", "AfterEnterScanEnabled", true),
            AfterEnterScanMs = Math.Clamp(ini.GetInt("ThoiGian", "AfterEnterScanMs", 1000), 700, 2000),
            PeriodicF5Minutes = ini.GetInt("F5DinhKy", "Phut", 0),
            TimerStopMinutes = ini.GetInt("HenGio", "Phut", 0),
            ChromePort = ini.GetInt("V11", "ChromePort", 9222),
            ChromeProfileDir = fixedProfileDir,
            StrictXPathOnly = ini.GetBool("V11", "StrictXPathOnly", true),
            ChromeMode = ini.Get("V11", "ChromeMode", "visible")
        };

        // Runtime luôn dùng một profile cố định đã có từ trước; không phụ thuộc AppContext/BaseDirectory
        // và không đọc profile runtime từ auto_chrome.ini để tránh dotnet run tạo profile riêng.
        s.ChromeProfileDir = fixedProfileDir;

        s.Viewer = new ViewerSettings
        {
            Enabled = ini.GetBool("NguoiXem", "Enabled"),
            XPath = ini.Get("NguoiXem", "XPath"),
            Threshold = ini.GetInt("NguoiXem", "Threshold", 100),
            ConfirmLow = ini.GetInt("NguoiXem", "ConfirmLow", 2),
            IntervalSec = ini.GetInt("NguoiXem", "IntervalSec", 120),
            WaitAfterF5Sec = ini.GetInt("NguoiXem", "WaitAfterF5Sec", 2),
            MaxF5 = ini.GetInt("NguoiXem", "MaxF5", 100),
            OcrRetries = ini.GetInt("NguoiXem", "OcrRetries", 3),
            RX1 = ini.GetDouble("NguoiXem", "RX1"), RY1 = ini.GetDouble("NguoiXem", "RY1"),
            RX2 = ini.GetDouble("NguoiXem", "RX2"), RY2 = ini.GetDouble("NguoiXem", "RY2")
        };

        s.OldLive = new OldLiveSettings
        {
            Enabled = ini.GetBool("LiveCu", "Enabled"),
            XPath = ini.Get("LiveCu", "XPath"),
            ActionXPath = ini.Get("LiveCu", "ActionXPath", s.XPathPeriodicAction),
            KeepMinutes = ini.GetInt("LiveCu", "KeepMin", 10),
            Variation = ini.GetInt("LiveCu", "Variation", 55),
            RX1 = ini.GetDouble("LiveCu", "RX1"), RY1 = ini.GetDouble("LiveCu", "RY1"),
            RX2 = ini.GetDouble("LiveCu", "RX2"), RY2 = ini.GetDouble("LiveCu", "RY2")
        };

        var count = ini.GetInt("VungQuet", "Count", 0);
        for (int i = 1; i <= count; i++)
        {
            var sec = $"VungQuet_{i}";
            var imgs = ini.Get(sec, "Images", ini.Get(sec, "Image"))
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            s.ScanRegions.Add(new ScanRegion
            {
                Index = i, Name = ini.Get(sec, "Name", $"Vùng {i}"), Enabled = ini.GetBool(sec, "Enabled", true),
                AfterClick = ini.GetBool(sec, "AfterClick", true), PeriodicEnabled = ini.GetBool(sec, "PeriodicEnabled"),
                PeriodicMinutes = ini.GetInt(sec, "PeriodicMinutes", 10), ConsecutiveMax = Math.Clamp(ini.GetInt(sec, "ConsecutiveMax", 1), 1, 4),
                Action = ini.Get(sec, "Action", "F5"), Variation = Math.Clamp(ini.GetInt(sec, "Variation", 55), 0, 255), Images = imgs,
                ScanXPath = ini.Get(sec, "ScanXPath"), ActionXPath = ini.Get(sec, "ActionXPath"),
                RX1 = ini.GetDouble(sec, "RX1"), RY1 = ini.GetDouble(sec, "RY1"), RX2 = ini.GetDouble(sec, "RX2"), RY2 = ini.GetDouble(sec, "RY2")
            });
        }
        return s;
    }

    public void Save(AppSettings s)
    {
        var ini = new IniFile(IniPath);
        ini.Set("XPath", "Point1", s.XPathPoint1); ini.Set("XPath", "Point2", s.XPathPoint2); ini.Set("XPath", "PeriodicAction", s.XPathPeriodicAction);
        ini.Set("XPath", "HoverArea", s.XPathHoverArea); ini.Set("XPath", "SwitchNeedsHover", s.SwitchNeedsHover ? 1 : 0); ini.Set("XPath", "HoverDelayMs", s.HoverDelayMs);
        ini.Set("ThoiGian", "DelayMin", s.DelayMinMs); ini.Set("ThoiGian", "DelayMax", s.DelayMaxMs);
        ini.Set("ThoiGian", "LoopMin", s.LoopMinMs); ini.Set("ThoiGian", "LoopMax", s.LoopMaxMs);
        ini.Set("ThoiGian", "AfterClickScanEnabled", s.AfterClickScanEnabled ? 1 : 0);
        ini.Set("ThoiGian", "AfterClickScanMs", s.AfterClickScanMs);
        ini.Set("ThoiGian", "AfterEnterScanEnabled", s.AfterEnterScanEnabled ? 1 : 0);
        ini.Set("ThoiGian", "AfterEnterScanMs", s.AfterEnterScanMs);
        ini.Set("F5DinhKy", "Phut", s.PeriodicF5Minutes); ini.Set("HenGio", "Phut", s.TimerStopMinutes);
        ini.Set("V11", "ChromePort", s.ChromePort);
        ini.Set("V11", "StrictXPathOnly", s.StrictXPathOnly ? 1 : 0); ini.Set("V11", "ChromeMode", s.ChromeMode); ini.Set("V11", "UseArrowDownForLiveSwitch", s.UseArrowDownForLiveSwitch ? 1 : 0);
        ini.Set("NguoiXem", "Enabled", s.Viewer.Enabled ? 1 : 0); ini.Set("NguoiXem", "XPath", s.Viewer.XPath);
        ini.Set("NguoiXem", "Threshold", s.Viewer.Threshold); ini.Set("NguoiXem", "ConfirmLow", s.Viewer.ConfirmLow);
        ini.Set("NguoiXem", "IntervalSec", s.Viewer.IntervalSec); ini.Set("NguoiXem", "WaitAfterF5Sec", s.Viewer.WaitAfterF5Sec);
        ini.Set("NguoiXem", "MaxF5", s.Viewer.MaxF5); ini.Set("NguoiXem", "OcrRetries", s.Viewer.OcrRetries);
        // Giữ nguyên vùng tỷ lệ V10 làm fallback khi XPath chưa được cấu hình.
        ini.Set("NguoiXem", "RX1", s.Viewer.RX1); ini.Set("NguoiXem", "RY1", s.Viewer.RY1);
        ini.Set("NguoiXem", "RX2", s.Viewer.RX2); ini.Set("NguoiXem", "RY2", s.Viewer.RY2);
        ini.Set("LiveCu", "Enabled", s.OldLive.Enabled ? 1 : 0); ini.Set("LiveCu", "XPath", s.OldLive.XPath); ini.Set("LiveCu", "ActionXPath", s.OldLive.ActionXPath);
        ini.Set("LiveCu", "KeepMin", s.OldLive.KeepMinutes); ini.Set("LiveCu", "Variation", s.OldLive.Variation);
        ini.Set("LiveCu", "RX1", s.OldLive.RX1); ini.Set("LiveCu", "RY1", s.OldLive.RY1);
        ini.Set("LiveCu", "RX2", s.OldLive.RX2); ini.Set("LiveCu", "RY2", s.OldLive.RY2);
        ini.Set("VungQuet", "Count", s.ScanRegions.Count);
        for (int n = 0; n < s.ScanRegions.Count; n++)
        {
            var r = s.ScanRegions[n]; var sec = $"VungQuet_{n + 1}";
            ini.Set(sec, "Name", r.Name); ini.Set(sec, "Enabled", r.Enabled ? 1 : 0); ini.Set(sec, "AfterClick", r.AfterClick ? 1 : 0);
            ini.Set(sec, "PeriodicEnabled", r.PeriodicEnabled ? 1 : 0); ini.Set(sec, "PeriodicMinutes", r.PeriodicMinutes); ini.Set(sec, "ConsecutiveMax", r.ConsecutiveMax);
            ini.Set(sec, "Action", r.Action); ini.Set(sec, "Variation", r.Variation); ini.Set(sec, "Images", string.Join('|', r.Images));
            ini.Set(sec, "Image", r.Images.FirstOrDefault() ?? ""); ini.Set(sec, "ScanXPath", r.ScanXPath); ini.Set(sec, "ActionXPath", r.ActionXPath);
            ini.Set(sec, "RX1", r.RX1); ini.Set(sec, "RY1", r.RY1); ini.Set(sec, "RX2", r.RX2); ini.Set(sec, "RY2", r.RY2);
        }
        ini.Save();
    }

    public List<string> LoadContents()
    {
        if (!File.Exists(ContentPath)) return [];
        var lines = File.ReadAllLines(ContentPath, Encoding.UTF8);
        return ContentLineHelper.GetDisplayLinesFromRawLines(lines);
    }

    public void SaveContents(string text)
    {
        var lines = ContentLineHelper.GetValidLinesForSave(text);
        File.WriteAllLines(ContentPath, lines, new UTF8Encoding(false));
    }

    public void ExportPackage(string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddFile(zip, IniPath, "auto_chrome.ini");
        AddFile(zip, ContentPath, "auto_chrome_noidung.txt");

        // Ảnh chuẩn nằm cạnh tool.
        foreach (var pattern in new[] { "*.png", "*.bmp", "*.jpg", "*.jpeg" })
            foreach (var file in Directory.EnumerateFiles(_baseDir, pattern, SearchOption.TopDirectoryOnly))
                AddFile(zip, file, Path.GetFileName(file));

        // Ảnh người dùng cắt/chụp từ tool.
        var captured = Path.Combine(_baseDir, "anh_mau_chup");
        if (Directory.Exists(captured))
            foreach (var file in Directory.EnumerateFiles(captured, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(_baseDir, file).Replace('\\', '/');
                AddFile(zip, file, rel);
            }

        var manifest = zip.CreateEntry("CONFIG_V11_INFO.txt", CompressionLevel.Fastest);
        using var w = new StreamWriter(manifest.Open(), new System.Text.UTF8Encoding(true));
        w.WriteLine("Tool TikTok V11 configuration package");
        w.WriteLine("Created=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        w.WriteLine("Includes=auto_chrome.ini, content text, image templates");
        w.WriteLine("Chrome profile/cookies are intentionally NOT exported.");
    }

    public string ImportPackage(string zipPath)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException("Không tìm thấy file cấu hình ZIP.", zipPath);
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.GetEntry("auto_chrome.ini") is null) throw new InvalidDataException("ZIP không có auto_chrome.ini nên không phải gói cấu hình V11 hợp lệ.");

        var backupDir = Path.Combine(_baseDir, "config_backups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(backupDir);
        if (File.Exists(IniPath)) File.Copy(IniPath, Path.Combine(backupDir, "auto_chrome.ini"), true);
        if (File.Exists(ContentPath)) File.Copy(ContentPath, Path.Combine(backupDir, "auto_chrome_noidung.txt"), true);

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var name = entry.FullName.Replace('\\', '/');
            bool allowed = name.Equals("auto_chrome.ini", StringComparison.OrdinalIgnoreCase)
                || name.Equals("auto_chrome_noidung.txt", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("anh_mau_chup/", StringComparison.OrdinalIgnoreCase)
                || (!name.Contains('/') && IsImage(name));
            if (!allowed) continue;

            var dest = Path.GetFullPath(Path.Combine(_baseDir, name.Replace('/', Path.DirectorySeparatorChar)));
            var baseFull = Path.GetFullPath(_baseDir) + Path.DirectorySeparatorChar;
            if (!dest.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase) && !dest.Equals(Path.GetFullPath(_baseDir), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ZIP chứa đường dẫn không an toàn: " + name);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
        return backupDir;
    }

    static bool IsImage(string file)
    {
        var ext = Path.GetExtension(file);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    static void AddFile(ZipArchive zip, string file, string entryName)
    {
        if (!File.Exists(file)) return;
        zip.CreateEntryFromFile(file, entryName.Replace('\\', '/'), CompressionLevel.Optimal);
    }
}

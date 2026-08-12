using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ToolTikTokV11.Utils;

namespace ToolTikTokV11.Services;

public sealed partial class TesseractOcr
{
    readonly Logger _log;
    string? _cachedExe;
    public TesseractOcr(Logger log) => _log = log;

    public string? FindExe()
    {
        // Cache only a successful lookup.  If Tesseract was not installed yet,
        // subsequent calls still re-check so installing it while the tool is
        // running keeps the previous behavior.
        if (!string.IsNullOrWhiteSpace(_cachedExe) && File.Exists(_cachedExe)) return _cachedExe;
        string[] c =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tesseract-OCR", "tesseract.exe")
        ];
        _cachedExe = c.FirstOrDefault(File.Exists);
        return _cachedExe;
    }

    public async Task<string> ReadAsync(string imagePath, CancellationToken ct = default)
    {
        var exe = FindExe() ?? throw new FileNotFoundException("Không tìm thấy tesseract.exe. Hãy cài Tesseract OCR.");
        var psi = new ProcessStartInfo(exe, $"\"{imagePath}\" stdout -l eng --psm 7")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được Tesseract.");
        var output = await p.StandardOutput.ReadToEndAsync(ct); var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) _log.Warn("Tesseract exit=" + p.ExitCode + "; " + err.Trim());
        return output.Trim();
    }

    // Chỉ nhận token có ÍT NHẤT một chữ số thật. Nhờ vậy chữ "NGƯỜI" không còn bị
    // biến I -> 1 rồi bị hiểu thành 1 người như parser cũ.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<num>[0-9OIL]*[0-9][0-9OIL]*(?:[\.,][0-9OIL]+)?)[ \t]*(?<unit>[KMB]?)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex ViewerTokenRegex();

    public int ParseViewerCount(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return -1;

        var text = raw.Trim().ToUpperInvariant().Replace("\r", " ").Replace("\n", " ");
        var matches = ViewerTokenRegex().Matches(text);
        if (matches.Count == 0) return -1;

        (int value, int score, string token, string unit, bool inferredK)? best = null;
        foreach (Match m in matches)
        {
            var token = m.Groups["num"].Value;
            var normalized = token.Replace('O', '0').Replace('I', '1').Replace('L', '1').Replace(',', '.');
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;

            var unit = m.Groups["unit"].Value.ToUpperInvariant();
            bool inferredK = false;
            // Giữ tương thích V10: dạng thập phân không có hậu tố thường là OCR mất chữ K.
            if (unit.Length == 0 && normalized.Contains('.'))
            {
                unit = "K";
                inferredK = true;
            }

            double mul = unit switch
            {
                "K" => 1_000d,
                "M" => 1_000_000d,
                "B" => 1_000_000_000d,
                _ => 1d
            };
            var scaled = v * mul;
            if (scaled < 0 || double.IsNaN(scaled) || double.IsInfinity(scaled)) continue;
            var value = scaled >= int.MaxValue ? int.MaxValue : (int)Math.Round(scaled);

            // Token có K/M/B được ưu tiên cao nhất, rồi tới số thập phân mất K, cuối cùng là số thường.
            int score = m.Groups["unit"].Value.Length > 0 ? 300 : inferredK ? 200 : 100;
            // Nếu cùng loại, ưu tiên token xuất hiện sau trong chuỗi vì text DOM thường có nhãn trước, số sau.
            score += Math.Min(99, m.Index / 8);

            if (best is null || score >= best.Value.score)
                best = (value, score, token, unit, inferredK);
        }

        if (best is null) return -1;
        var result = best.Value.value;

        if (best.Value.inferredK)
            _log.Warn($"OCR mất hậu tố K; tự hiểu “{raw}” là đơn vị nghìn.");

        return result;
    }
}

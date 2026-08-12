using System.Drawing;
using System.Text.Json;
using System.Xml.XPath;
using ToolTikTokV11.Models;
using ToolTikTokV11.Utils;

namespace ToolTikTokV11.Services;

public enum AutomationRunState { Stopped, Running, Paused }

/// <summary>
/// V11 chỉ thay lớp tương tác Chrome bằng XPath/CDP. State machine và thứ tự ưu tiên
/// dưới đây bám theo V10.4.4, với một thay đổi chủ đích ở V12.5: quét ảnh thường
/// ngay trước mỗi bước Click (1 và 5) thay vì full-scan sau Enter. Các luồng OCR người xem,
/// F5 định kỳ, Live cũ T-10s, quét định kỳ ưu tiên và transition lock giữ nguyên.
/// </summary>
public sealed class AutomationEngine
{
    public readonly record struct PeriodicF5Snapshot(bool Running, bool Enabled, bool Executing, DateTime DueAt);
    public sealed record OldLiveEntry(string Id, string ImagePath, DateTime CreatedAt, DateTime ExpiresAt, string Source);
    public sealed record OldLiveEntrySnapshot(string Id, string FileName, TimeSpan Age, TimeSpan Remaining);
    public sealed record OldLiveDiagnosticsSnapshot(
        int ActiveCount,
        DateTime? LastCapturedAt,
        DateTime? LastMatchAt,
        bool? LastMatchFound,
        string LastMatchImage,
        double? LastMatchScore,
        IReadOnlyList<OldLiveEntrySnapshot> Entries);

    const int ScanIntervalMs = 350; // Giảm một lượt screenshot thừa trong cửa sổ quan sát 1 giây.
    const int EnterReactionScanMs = 2000;
    const int VerifyAfterF5Ms = 1500;
    // ArrowDown vẫn có 2 giây settle riêng. Sau Reload chỉ cần nhịp CDP ngắn
    // rồi xác nhận DOM ready, không cộng thêm một sleep cố định 2 giây.
    const int F5WaitMs = 1000;
    const int MultiActionGapMs = 600;
    const int LiveVerifyPollMs = 250;
    const int LiveVerifyTimeoutMs = 5000;
    const int ArrowDownSettleBeforeReloadMs = 2000;
    const int ArrowDownRetryAttempts = 2;
    const int ArrowDownRetryDelayMs = 750;
    const int PriorityPauseMs = 5000;
    const int StopConfirmDelayMs = 350;
    const int ImageScanSlowMs = 1500;
    const int ImageScanTimeoutMs = 2000;
    const int XPathScanMissCooldownMs = 10000;
    const int OldLiveScanIntervalMs = 1500;
    const int OldLiveScanRetryMs = 2500;
    const string OldLiveDirectoryName = "live_cu_tam";
    const string OldLiveManifestFileName = "old_live_manifest.json";
    const int RequiredXPathRecoveryMaxAttempts = 3;
    static readonly TimeSpan RequiredXPathRecoveryWait = TimeSpan.FromSeconds(10);
    const int ConsecutiveRecoveryFailurePauseThreshold = 10;
    static readonly TimeSpan[] CdpReconnectBackoff =
    [
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    readonly string _baseDir;
    readonly ChromeController _chrome;
    readonly Logger _log;
    readonly TesseractOcr _ocr;
    readonly Random _rng = new();
    readonly object _periodicSnapshotLock = new();
    readonly Dictionary<string, CachedTemplate> _templateCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, DateTime> _xpathScanUnavailableUntil = new(StringComparer.Ordinal);
    static readonly JsonSerializerOptions OldLiveStoreJson = new() { WriteIndented = true };

    CancellationTokenSource? _cts;
    Task? _task;
    volatile bool _paused;
    volatile bool _running;
    bool _transitioning;

    AppSettings _s = new();
    List<string> _contents = [];
    int _contentIndex;
    int _step = 1; // V10: buocHienTai 1..8
    long _rounds;
    System.Diagnostics.Stopwatch? _loopPerf;
    long _loopPerfTotalMs;
    long _loopPerfCount;

    DateTime _periodicDue = DateTime.MaxValue;
    DateTime _candidateCaptureAt = DateTime.MaxValue;
    readonly List<OldLiveEntry> _activeOldLives = [];
    bool _oldLiveManifestLoaded;
    bool _legacyOldLiveFilesChecked;
    DateTime _nextOldLiveScan = DateTime.MaxValue;
    DateTime _nextViewer = DateTime.MaxValue;
    DateTime _stopAt = DateTime.MaxValue;
    bool _periodicExecuting;
    PeriodicF5Snapshot _periodicSnapshot = new(false, false, false, DateTime.MaxValue);
    DateTime? _lastOldLiveCapturedAt;
    DateTime? _lastOldLiveMatchAt;
    bool? _lastOldLiveMatchFound;
    string _lastOldLiveMatchImage = "";
    double? _lastOldLiveMatchScore;

    string _consecutiveRegion = "";
    int _consecutiveCount;
    int _consecutiveRecoveryFailures;
    readonly Dictionary<string, DateTime> _problemLast = new(StringComparer.Ordinal);

    enum RecoveryDecision { RetryStep, SkipLive, SkipStep }
    sealed class CachedTemplate : IDisposable
    {
        public required DateTime LastWriteUtc { get; init; }
        public required ImageMatcher.MultiScaleTemplate Template { get; init; }

        public void Dispose() => Template.Dispose();
    }

    sealed class ScanCaptureCache : IDisposable
    {
        public byte[]? ViewportBytes { get; set; }
        public Bitmap? ViewportBitmap { get; set; }
        public (int width, int height)? ViewportSize { get; set; }

        public void Dispose()
        {
            ViewportBitmap?.Dispose();
            ViewportBitmap = null;
            ViewportBytes = null;
            ViewportSize = null;
        }
    }

    sealed record PersistedOldLiveEntry(string Id, string FileName, DateTime CreatedAt, DateTime ExpiresAt, string Source);

    sealed class RecoverableAutomationException : Exception
    {
        public string Code { get; }
        public string Context { get; }
        public RecoveryDecision Decision { get; }

        public RecoverableAutomationException(string code, string context, string message, RecoveryDecision decision, Exception? inner = null)
            : base(message, inner)
        {
            Code = code;
            Context = context;
            Decision = decision;
        }
    }

    public bool Running => _running;
    public bool Paused => _paused;
    public AutomationRunState RunState => !_running ? AutomationRunState.Stopped : _paused ? AutomationRunState.Paused : AutomationRunState.Running;
    public long Rounds => _rounds;
    public Task CompletionTask => _task ?? Task.CompletedTask;
    public event Action<string>? Status;
    public event Action<string>? Problem;
    public event Action? StateChanged;
    public event Action<AutomationRunState>? RunStateChanged;

    public AutomationEngine(string baseDir, ChromeController chrome, Logger log)
    {
        _baseDir = baseDir;
        _chrome = chrome;
        _log = log;
        _ocr = new TesseractOcr(log);
        LoadPersistedOldLives();
    }

    public PeriodicF5Snapshot GetPeriodicF5Snapshot()
    {
        lock (_periodicSnapshotLock) return _periodicSnapshot;
    }

    public OldLiveDiagnosticsSnapshot GetOldLiveDiagnosticsSnapshot()
    {
        if (!_running) CleanupExpiredOldLives();
        var now = DateTime.Now;
        var entries = _activeOldLives
            .OrderBy(e => e.ExpiresAt)
            .Select(e => new OldLiveEntrySnapshot(
                e.Id,
                Path.GetFileName(e.ImagePath),
                now - e.CreatedAt,
                e.ExpiresAt - now))
            .ToList();
        return new OldLiveDiagnosticsSnapshot(
            entries.Count,
            _lastOldLiveCapturedAt,
            _lastOldLiveMatchAt,
            _lastOldLiveMatchFound,
            _lastOldLiveMatchImage,
            _lastOldLiveMatchScore,
            entries);
    }

    void SyncPeriodicSnapshot()
    {
        lock (_periodicSnapshotLock)
            _periodicSnapshot = new PeriodicF5Snapshot(_running, _s.PeriodicF5Minutes > 0, _periodicExecuting, _periodicDue);
    }

    public void Start(AppSettings settings, List<string> contents)
    {
        if (_running) return;
        if (!_chrome.Connected) throw new InvalidOperationException("Hãy kết nối Chrome V11 trước khi bắt đầu.");
        if (contents.Count == 0) throw new InvalidOperationException("Danh sách nội dung đang trống.");
        if (string.IsNullOrWhiteSpace(settings.XPathPoint1) || string.IsNullOrWhiteSpace(settings.XPathPoint2))
            throw new InvalidOperationException("V11 cần XPath Điểm 1 và XPath Điểm 2 trước khi chạy.");

        _s = settings;
        _contents = contents;
        _contentIndex = 0;
        _step = 1;
        _rounds = 0;
        _loopPerf = System.Diagnostics.Stopwatch.StartNew();
        _loopPerfTotalMs = 0;
        _loopPerfCount = 0;
        _paused = false;
        _running = true;
        _transitioning = false;
        ResetConsecutive("khởi động");

        var now = DateTime.Now;
        // Khi vừa bắt đầu tool, nếu bật kiểm tra người xem thì OCR ngay ở vòng đầu tiên
        // để có thể đi thẳng vào chuỗi ↓ + F5 hiện có trước khi gửi nội dung.
        _nextViewer = _s.Viewer.Enabled ? now : DateTime.MaxValue;
        _stopAt = _s.TimerStopMinutes > 0 ? now.AddMinutes(_s.TimerStopMinutes) : DateTime.MaxValue;
        EnsureOldLivesReadyForRun();
        foreach (var r in _s.ScanRegions)
            r.NextPeriodicAt = r.PeriodicEnabled ? now.AddMinutes(Math.Max(1, r.PeriodicMinutes)) : DateTime.MaxValue;

        ResetPeriodicDue("khởi động", cancelCandidate: true);
        SyncPeriodicSnapshot();
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => LoopAsync(_cts.Token));
        _log.Info("V11 bắt đầu. Logic xử lý theo V10.4.4; lớp click/phím/F5 dùng XPath + Chrome DevTools.");
        SetStatus("ĐANG CHẠY", "V11 XPath/CDP đã bắt đầu.");
        NotifyStateChanged();
    }

    public void TogglePause()
    {
        if (!_running) return;
        _paused = !_paused;
        _log.Info(_paused ? "Tool đã tạm dừng." : "Tool tiếp tục.");
        SetStatus(_paused ? "TẠM DỪNG" : "ĐANG CHẠY", _paused ? "F9 để tiếp tục." : "Đã tiếp tục.");
        SyncPeriodicSnapshot();
        NotifyStateChanged();
    }

    void AutoPause(string reason)
    {
        if (!_running) return;
        _paused = true;
        _periodicExecuting = false;
        _log.Error("[AUTO_PAUSE] " + reason);
        SetStatus("TẠM DỪNG DO LỖI LIÊN TIẾP", reason);
        SyncPeriodicSnapshot();
        NotifyStateChanged();
    }

    public void Stop(string reason = "Người dùng dừng tool")
    {
        if (!_running) return;
        _running = false;
        _paused = false;
        _periodicExecuting = false;
        _log.Warn("DỪNG TOOL: " + reason);
        SetStatus("ĐÃ DỪNG", reason);
        try { _cts?.Cancel(); } catch { }
        SyncPeriodicSnapshot();
        NotifyStateChanged();
    }

    public async Task<bool> WaitForStopAsync(TimeSpan timeout)
    {
        var task = CompletionTask;
        if (task.IsCompleted) return true;
        var done = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        return done == task;
    }

    async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (_running && !ct.IsCancellationRequested)
            {
                try
                {
                    await WaitIfPausedAsync(ct);
                    if (!_running) break;
                    if (DateTime.Now >= _stopAt)
                    {
                        Stop("Đã hết thời gian hẹn giờ chạy.");
                        break;
                    }

                    // V10.4: quét ưu tiên đi trước mọi luồng khác ở ranh giới an toàn.
                    if (await HandlePriorityDueAsync(ct)) continue;

                    // Các timer logic chỉ được xử lý ở ranh giới giữa các bước, không chen giữa click/dán/Enter.
                    if (await HandleOldLiveExpiryAndScanAsync(ct)) continue;
                    if (await HandlePeriodicCaptureAndF5Async(ct)) continue;
                    if (await HandleViewerDueAsync(ct)) continue;

                    // V12.5: full-scan ảnh thường chỉ chạy ngay trước hai bước Click
                    // (bước 1 và 5) trong ExecuteOneStepAsync. Không quét chen giữa
                    // Click -> dán -> Enter, và không full-scan sau Enter.
                    await ExecuteOneStepAsync(ct);
                    ResetRecoveryFailures("workflow chính đã chạy thành công");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (RecoverableAutomationException ex)
                {
                    await RecoverAndContinueAsync(ex, ct);
                }
                catch (Exception ex)
                {
                    await HandleUnexpectedAutomationExceptionAsync(ex, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _running = false;
            _periodicExecuting = false;
            SyncPeriodicSnapshot();
            NotifyStateChanged();
        }
    }

    void NotifyStateChanged()
    {
        RunStateChanged?.Invoke(RunState);
        StateChanged?.Invoke();
    }

    async Task WaitIfPausedAsync(CancellationToken ct)
    {
        if (!_paused) return;
        var start = DateTime.Now;
        while (_running && _paused) await Task.Delay(200, ct);
        var pausedFor = DateTime.Now - start;
        // V10 không trừ thời gian người dùng Pause vào bộ đếm F5 định kỳ.
        ShiftPeriodicClock(pausedFor);
        foreach (var r in _s.ScanRegions)
            if (r.PeriodicEnabled && r.NextPeriodicAt != DateTime.MaxValue) r.NextPeriodicAt += pausedFor;
    }

    // Hai khoảng delay này là cấu hình nghiệp vụ theo từng profile.  Tuyệt đối
    // không cap runtime: người dùng nhập 1500-2800 thì vẫn random đúng khoảng đó.
    int ActionDelay() => ConfiguredRandomDelay(_s.DelayMinMs, _s.DelayMaxMs);
    int NormalCdpDelay() => ConfiguredRandomDelay(_s.DelayMinMs, _s.DelayMaxMs);
    int NormalLoopDelay() => ConfiguredRandomDelay(_s.LoopMinMs, _s.LoopMaxMs);

    int ConfiguredRandomDelay(int configuredMin, int configuredMax)
    {
        var min = Math.Max(0, Math.Min(configuredMin, configuredMax));
        var max = Math.Max(min, Math.Max(configuredMin, configuredMax));
        return min == max ? min : (int)_rng.NextInt64(min, (long)max + 1);
    }

    void SetStatus(string title, string text) => Status?.Invoke(title + "\n" + text);

    void ReportProblem(string code, string context, string detail, bool error = false, int throttleSeconds = 15)
    {
        var key = code + "|" + context + "|" + detail;
        var now = DateTime.Now;
        if (_problemLast.TryGetValue(key, out var last) && (now - last).TotalSeconds < throttleSeconds) return;
        _problemLast[key] = now;
        var msg = $"[{code}] {context} — {detail}";
        if (error) _log.Error(msg); else _log.Warn(msg);
        Problem?.Invoke(msg);
        SetStatus(error ? "LỖI" : "CẢNH BÁO", msg);
    }

    void ResetRecoveryFailures(string reason)
    {
        if (_consecutiveRecoveryFailures <= 0) return;
        _log.Info($"Reset bộ đếm recovery liên tiếp sau {_consecutiveRecoveryFailures} LIVE/bước lỗi: {reason}.");
        _consecutiveRecoveryFailures = 0;
    }

    void IncreaseRecoveryFailures(string reason)
    {
        _consecutiveRecoveryFailures++;
        _log.Warn($"[RECOVERY_FAILED] count={_consecutiveRecoveryFailures}/{ConsecutiveRecoveryFailurePauseThreshold} reason={reason}");
        if (_consecutiveRecoveryFailures >= ConsecutiveRecoveryFailurePauseThreshold)
            AutoPause($"{_consecutiveRecoveryFailures} LIVE liên tiếp không thể phục hồi. Tool đã tạm dừng để tránh vòng lỗi vô hạn.");
    }

    async Task RecoverAndContinueAsync(RecoverableAutomationException ex, CancellationToken ct)
    {
        _log.Warn($"[RECOVERY_START] code={ex.Code} context={ex.Context} reason={ex.Message}");
        SetStatus("ĐANG TỰ PHỤC HỒI", $"{ex.Context}: {ex.Message}");

        // LIVE/XPath failures are part of normal TikTok churn.  Reconnect and mark
        // Chrome unavailable only when the CDP session/target has actually gone away.
        var cdpSessionLost = IsLikelyCdpIssue(ex);
        if (cdpSessionLost && !await EnsureCdpRecoveredAsync($"{ex.Code}/{ex.Context}", ct))
        {
            IncreaseRecoveryFailures($"CDP session lost: {ex.Code}/{ex.Context}");
            return;
        }

        switch (ex.Decision)
        {
            case RecoveryDecision.RetryStep:
                _log.Warn($"[RECOVERY_OK] code={ex.Code} context={ex.Context} action=retry-step");
                return;
            case RecoveryDecision.SkipStep:
                if (cdpSessionLost) IncreaseRecoveryFailures($"{ex.Code}/{ex.Context} -> skip-step");
                await SkipCurrentStepAsync(ex.Code, ex.Context, ex.Message, ct);
                return;
            default:
                if (cdpSessionLost) IncreaseRecoveryFailures($"{ex.Code}/{ex.Context} -> skip-live");
                await SkipCurrentLiveAsync(ex.Code, ex.Context, ex.Message, ct);
                return;
        }
    }

    async Task HandleUnexpectedAutomationExceptionAsync(Exception ex, CancellationToken ct)
    {
        _log.Error("Lỗi engine V11 đã được chặn để tránh chết LoopAsync: " + ex);
        if (IsLikelyRecoverableAutomationError(ex))
        {
            var wrapped = new RecoverableAutomationException("UNEXPECTED_RECOVERABLE", "LoopAsync", ex.Message, RecoveryDecision.SkipLive, ex);
            await RecoverAndContinueAsync(wrapped, ct);
            return;
        }

        AutoPause("Lỗi nội bộ nghiêm trọng: " + ex.Message);
    }

    bool IsLikelyRecoverableAutomationError(Exception ex)
        => ex is InvalidOperationException
        || ex is IOException
        || ex is TimeoutException
        || IsLikelyCdpIssue(ex);

    bool IsLikelyCdpIssue(Exception ex) => _chrome.IsCdpSessionLost(ex);

    async Task<bool> EnsureCdpRecoveredAsync(string context, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= CdpReconnectBackoff.Length; attempt++)
        {
            var delay = CdpReconnectBackoff[attempt - 1];
            _log.Warn($"[CDP_RECONNECT_START] context={context} attempt={attempt}/{CdpReconnectBackoff.Length} delayMs={(int)delay.TotalMilliseconds}");
            SetStatus("ĐANG RECONNECT CDP", $"{context}: attempt {attempt}/{CdpReconnectBackoff.Length}");
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);

            try
            {
                await _chrome.ReconnectAsync(ct);
                _log.Warn($"[CDP_RECONNECTED] context={context} attempt={attempt}/{CdpReconnectBackoff.Length}");
                SetStatus("ĐANG CHẠY", $"CDP đã phục hồi: {context}");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn($"[CDP_RECONNECT_FAILED] context={context} attempt={attempt}/{CdpReconnectBackoff.Length} reason={ex.Message}");
            }
        }
        return false;
    }

    async Task SkipCurrentLiveAsync(string code, string context, string reason, CancellationToken ct)
    {
        _log.Warn($"[LIVE_SKIP] code={code} context={context} reason={reason}");
        SetStatus("ĐANG BỎ QUA LIVE LỖI", $"{context}: {reason}");
        ResetConsecutive($"bỏ qua live lỗi {context}");
        _step = CurrentRestartStep;

        if (!_chrome.Connected && !await EnsureCdpRecoveredAsync($"LIVE_SKIP/{context}", ct))
        {
            IncreaseRecoveryFailures($"CDP session lost while skipping LIVE: {context}");
            return;
        }

        try
        {
            var action = _s.UseArrowDownForLiveSwitch ? TransitionAction.ArrowDown : TransitionAction.ClickXPath;
            var ok = await TransitionAsync($"bỏ qua LIVE lỗi {context}", action, _s.XPathPeriodicAction, 1, scheduledPeriodic: false, ct, F5WaitMs);
            if (ok)
            {
                _log.Warn($"[RECOVERY_OK] code={code} context={context} action=skip-live");
                return;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Warn($"[LIVE_SKIP] context={context} transition failed: {ex.Message}");
        }

        // A TikTok LIVE can reject a navigation or keep a stale DOM even though CDP
        // remains healthy.  Do not turn that normal, recoverable case into PAUSED.
        ReportProblem("LIVE_SKIP_UNCONFIRMED", context,
            "Đã retry bỏ qua LIVE nhưng chưa xác nhận được LIVE mới. Sẽ tiếp tục vòng chính và thử lại ở ranh giới an toàn.",
            throttleSeconds: 15);
        await Task.Delay(ArrowDownRetryDelayMs, ct);
    }

    Task SkipCurrentStepAsync(string code, string context, string reason, CancellationToken ct)
    {
        _log.Warn($"[STEP_SKIP] code={code} context={context} reason={reason}");
        SetStatus("ĐANG TỰ PHỤC HỒI", $"{context}: bỏ qua bước hiện tại.");
        _step = _step switch
        {
            >= 1 and < 8 => _step + 1,
            _ => 1
        };
        return Task.CompletedTask;
    }

    async Task ClickRequiredXPathAsync(string context, string xpath, CancellationToken ct)
    {
        await EnsureRequiredXPathWithRecoveryAsync(context, xpath, ct);
        try
        {
            _log.Info($"{context}: bắt đầu click XPath.");
            await _chrome.ClickXPathAsync(xpath, ct: ct);
            _log.Info($"{context}: click XPath xong.");
        }
        catch (Exception ex) { throw new RecoverableAutomationException("CLICK_REQUIRED_XPATH_FAILED", context, $"Click XPath thất bại: {ex.Message}", RecoveryDecision.SkipLive, ex); }
    }

    async Task InsertRequiredXPathAsync(string context, string xpath, string text, CancellationToken ct)
    {
        await EnsureRequiredXPathWithRecoveryAsync(context, xpath, ct);
        try
        {
            _log.Info($"{context}: bắt đầu nhập nội dung dài {text.Length} ký tự.");
            await _chrome.InsertTextAsync(xpath, text, ct);
            _log.Info($"{context}: nhập nội dung xong.");
        }
        catch (Exception ex) { throw new RecoverableAutomationException("INSERT_REQUIRED_XPATH_FAILED", context, $"Nhập chữ qua XPath thất bại: {ex.Message}", RecoveryDecision.SkipLive, ex); }
    }

    async Task EnsureRequiredXPathWithRecoveryAsync(string context, string xpath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateRequiredXPathOrThrow(context, xpath);

        _log.Info($"{context}: kiểm tra XPath bắt buộc trước khi tiếp tục workflow.");
        if (await RequiredXPathExistsAsync(context, xpath, ct)) return;

        for (int attempt = 1; attempt <= RequiredXPathRecoveryMaxAttempts; attempt++)
        {
            _log.Warn($"[XPATH_RECOVERY_START] context={context} xpath={xpath} attempt={attempt}/{RequiredXPathRecoveryMaxAttempts}");
            SetStatus("RECOVERY XPATH", $"{context} thiếu XPath, đang recovery {attempt}/{RequiredXPathRecoveryMaxAttempts}.");

            var action = _s.UseArrowDownForLiveSwitch ? TransitionAction.ArrowDown : TransitionAction.ClickXPath;
            _log.Warn($"[XPATH_RECOVERY_TRANSITION] {(_s.UseArrowDownForLiveSwitch ? "ArrowDown CDP -> F5" : "Live switch hien tai -> F5")}");

            var transitioned = await TransitionAsync(
                $"XPATH recovery {context} {attempt}/{RequiredXPathRecoveryMaxAttempts}",
                action,
                _s.XPathPeriodicAction,
                1,
                scheduledPeriodic: false,
                ct,
                (int)RequiredXPathRecoveryWait.TotalMilliseconds);

            if (!transitioned)
            {
                _log.Warn($"[XPATH_RECOVERY_RECHECK] context={context} result=False attempt={attempt}/{RequiredXPathRecoveryMaxAttempts}");
                continue;
            }

            _log.Warn($"[XPATH_RECOVERY_WAIT] Chờ {RequiredXPathRecoveryWait.TotalSeconds:0} giây cho LIVE ổn định");
            var recovered = await RequiredXPathExistsAsync(context, xpath, ct);
            _log.Warn($"[XPATH_RECOVERY_RECHECK] context={context} result={recovered} attempt={attempt}/{RequiredXPathRecoveryMaxAttempts}");
            if (recovered)
            {
                _log.Warn($"[XPATH_RECOVERY_OK] context={context} attempt={attempt}/{RequiredXPathRecoveryMaxAttempts}");
                _log.Info($"[XPATH_RECOVERY_OK] {context} đã xuất hiện lại sau recovery {attempt}/{RequiredXPathRecoveryMaxAttempts}.");
                SetStatus("RECOVERY THÀNH CÔNG", $"{context} đã xuất hiện lại sau recovery {attempt}/{RequiredXPathRecoveryMaxAttempts}.");
                return;
            }
        }

        _log.Error($"[XPATH_RECOVERY_FAILED] context={context} xpath={xpath} attempts={RequiredXPathRecoveryMaxAttempts}");
        ReportProblem("LIVE_UNUSABLE", context, $"Không tìm thấy XPath quan trọng sau {RequiredXPathRecoveryMaxAttempts} lần tự phục hồi. XPath={xpath}", error: true, throttleSeconds: 15);
        throw new RecoverableAutomationException("LIVE_UNUSABLE", context, $"XPath bắt buộc không phục hồi được sau {RequiredXPathRecoveryMaxAttempts} lần.", RecoveryDecision.SkipLive);
    }

    void ValidateRequiredXPathOrThrow(string context, string xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            ReportProblem("XPATH_CONFIG_MISSING", context, "XPath bắt buộc đang trống.", error: true, throttleSeconds: 30);
            throw new InvalidOperationException($"[{context}] chưa cấu hình XPath.");
        }

        try
        {
            XPathExpression.Compile(xpath);
        }
        catch (XPathException ex)
        {
            ReportProblem("XPATH_INVALID", context, $"XPath không hợp lệ: {xpath}. Chi tiết: {ex.Message}", error: true, throttleSeconds: 30);
            throw new InvalidOperationException($"[{context}] XPath không hợp lệ: {xpath}", ex);
        }
    }

    async Task<bool> RequiredXPathExistsAsync(string context, string xpath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return await _chrome.XPathExistsAsync(xpath, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (_chrome.IsCdpSessionLost(ex))
        {
            ReportProblem("CDP_SESSION_LOST", context, "CDP/session/target thực sự mất khi kiểm tra XPath bắt buộc.", error: true, throttleSeconds: 15);
            throw new RecoverableAutomationException("CDP_SESSION_LOST", context, "CDP/session/target thực sự mất khi kiểm tra XPath bắt buộc.", RecoveryDecision.SkipLive, ex);
        }
    }

    string CurrentInputXPath => _step <= 4 ? _s.XPathPoint1 : _s.XPathPoint2;
    int CurrentRestartStep => _step <= 4 ? 1 : 5;
    string CurrentPointName => _step <= 4 ? "điểm 1" : "điểm 2";

    async Task ExecuteOneStepAsync(CancellationToken ct)
    {
        var stepAtStart = _step;
        var stepPerf = System.Diagnostics.Stopwatch.StartNew();
        var content = _contents[_contentIndex];
        try
        {
            switch (_step)
            {
                case 1:
                {
                    SetStatus("BƯỚC 1/8", $"Quét ảnh lỗi → Click ô nhập 1 • nội dung {_contentIndex + 1}/{_contents.Count}");
                    if (await ScanAndProcessBeforeClickAsync(ct)) return;
                    _log.Info($"Nội dung {_contentIndex + 1}/{_contents.Count}: quét trước click sạch, click XPath điểm 1.");
                    await ClickRequiredXPathAsync("Điểm/ô nhập 1", _s.XPathPoint1, ct);
                    _step = 2;
                    await Task.Delay(NormalCdpDelay(), ct);
                    break;
                }
                case 2:
                    SetStatus("BƯỚC 2/8", $"Nhập nội dung {_contentIndex + 1}/{_contents.Count} vào ô 1");
                    await InsertRequiredXPathAsync("Điểm/ô nhập 1", _s.XPathPoint1, content, ct);
                    _step = 3;
                    await Task.Delay(NormalCdpDelay(), ct);
                    break;

                case 3:
                {
                    SetStatus("BƯỚC 3/8", "Enter ô 1 • chờ TikTok phản hồi");
                    await _chrome.PressKeyAsync("Enter", ct: ct);
                    _step = 4;
                    // Vẫn giữ đúng khoảng chờ phản hồi cũ; chỉ dời full-scan sang ngay
                    // trước Click kế tiếp để không thay đổi nhịp nghiệp vụ.
                    await Task.Delay(EnterReactionScanMs, ct);
                    break;
                }
                case 4:
                    SetStatus("BƯỚC 4/8", "Hoàn tất điểm 1 • chuyển sang điểm 2");
                    _step = 5;
                    break;

                case 5:
                {
                    SetStatus("BƯỚC 5/8", $"Quét ảnh lỗi → Click ô nhập 2 • nội dung {_contentIndex + 1}/{_contents.Count}");
                    if (await ScanAndProcessBeforeClickAsync(ct)) return;
                    _log.Info($"Nội dung {_contentIndex + 1}/{_contents.Count}: quét trước click sạch, click XPath điểm 2.");
                    await ClickRequiredXPathAsync("Điểm/ô nhập 2", _s.XPathPoint2, ct);
                    _step = 6;
                    await Task.Delay(NormalCdpDelay(), ct);
                    break;
                }
                case 6:
                    SetStatus("BƯỚC 6/8", $"Nhập nội dung {_contentIndex + 1}/{_contents.Count} vào ô 2");
                    await InsertRequiredXPathAsync("Điểm/ô nhập 2", _s.XPathPoint2, content, ct);
                    _step = 7;
                    await Task.Delay(NormalCdpDelay(), ct);
                    break;

                case 7:
                {
                    SetStatus("BƯỚC 7/8", "Enter ô 2 • chờ TikTok phản hồi");
                    await _chrome.PressKeyAsync("Enter", ct: ct);
                    _step = 8;
                    await Task.Delay(EnterReactionScanMs, ct);
                    break;
                }
                case 8:
                {
                    SetStatus("BƯỚC 8/8", "Hoàn tất vòng • chuẩn bị nội dung tiếp theo");
                    _rounds++;
                    var used = _contentIndex + 1;
                    _contentIndex = (_contentIndex + 1) % _contents.Count;
                    _step = 1;
                    SetStatus("ĐANG CHẠY", $"Hoàn tất vòng {_rounds} với nội dung {used}/{_contents.Count}. Tiếp theo {_contentIndex + 1}/{_contents.Count}.");
                    NotifyStateChanged();
                    await Task.Delay(NormalLoopDelay(), ct);
                    if (_loopPerf is not null)
                    {
                        var totalMs = _loopPerf.ElapsedMilliseconds;
                        _loopPerfTotalMs += totalMs;
                        _loopPerfCount++;
                        _log.Info($"[LOOP_PERF] totalMs={totalMs} avgMs={_loopPerfTotalMs / _loopPerfCount} round={_rounds}");
                        _loopPerf.Restart();
                    }
                    break;
                }

                default:
                    _step = 1;
                    break;
            }
        }
        finally
        {
            stepPerf.Stop();
            _log.Info($"[STEP_PERF] step={stepAtStart} elapsedMs={stepPerf.ElapsedMilliseconds}");
        }
    }

    enum ScanMode { AllNonPeriodic, Priority }
    sealed record RegionHit(ScanRegion Region, string Template, ImageMatchResult Result);

    IEnumerable<ScanRegion> RegionsFor(ScanMode mode)
    {
        var now = DateTime.Now;
        var q = _s.ScanRegions.Where(r => r.Enabled);
        q = mode switch
        {
            ScanMode.Priority => q.Where(r => r.PeriodicEnabled && now >= r.NextPeriodicAt),
            _ => q.Where(r => !r.PeriodicEnabled)
        };
        // V10.4.2: STOP luôn được kiểm tra trước cả trong quét thường lẫn ưu tiên.
        return q.OrderByDescending(r => r.Action.Equals("STOP", StringComparison.OrdinalIgnoreCase));
    }

    static string ScanXPathCooldownKey(ScanRegion region) => region.Name + "\n" + region.ScanXPath;

    bool IsScanXPathCoolingDown(ScanRegion region)
    {
        if (string.IsNullOrWhiteSpace(region.ScanXPath)) return false;
        var key = ScanXPathCooldownKey(region);
        if (!_xpathScanUnavailableUntil.TryGetValue(key, out var retryAt)) return false;
        if (DateTime.Now >= retryAt)
        {
            _xpathScanUnavailableUntil.Remove(key);
            return false;
        }
        return true;
    }

    void ClearScanXPathCooldown(string reason)
    {
        if (_xpathScanUnavailableUntil.Count == 0) return;
        _log.Info($"[XPATH_SCAN_COOLDOWN_CLEAR] count={_xpathScanUnavailableUntil.Count} reason={reason}");
        _xpathScanUnavailableUntil.Clear();
    }

    async Task<RegionHit?> FindFirstMatchAsync(ScanMode mode, CancellationToken ct)
    {
        // Một lượt quét chỉ chụp mỗi XPath/vùng viewport một lần. Nhiều vùng dùng chung
        // room-chat-input-field sẽ tái sử dụng cùng screenshot thay vì bắt Chrome chụp lặp.
        using var captureCache = new ScanCaptureCache();
        var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + ImageScanTimeoutMs * System.Diagnostics.Stopwatch.Frequency / 1000;
        foreach (var region in RegionsFor(mode))
        {
            if (System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
            {
                _log.Warn($"[IMAGE_SCAN_TIMEOUT] scope=pass mode={mode} timeoutMs={ImageScanTimeoutMs}");
                return null;
            }
            var raw = await FindRegionMatchAsync(region, captureCache, deadline, ct);
            if (raw is null) continue;
            if (region.Action.Equals("STOP", StringComparison.OrdinalIgnoreCase))
            {
                if (!await ConfirmStopAsync(region, raw.Value.template, ct))
                {
                    _log.Warn($"Bỏ qua STOP nhận nhầm ở vùng “{region.Name}”: ảnh {raw.Value.template} không vượt qua xác nhận 3 lần.");
                    continue;
                }
            }
            return new RegionHit(region, raw.Value.template, raw.Value.result);
        }
        return null;
    }

    async Task<(string template, ImageMatchResult result)?> FindRegionMatchAsync(ScanRegion region, ScanCaptureCache captureCache, long deadline, CancellationToken ct)
    {
        if (_s.StrictXPathOnly && string.IsNullOrWhiteSpace(region.ScanXPath))
        {
            ReportProblem("XPATH_SCAN_MISSING", $"Vùng quét “{region.Name}”", "Chưa cấu hình XPath vùng quét. Đã bỏ qua vùng này; không dùng tọa độ fallback.");
            return null;
        }

        if (region.Images.Count == 0)
        {
            ReportProblem("IMAGE_TEMPLATE_MISSING", $"Vùng quét “{region.Name}”", "Chưa có ảnh mẫu. Đã bỏ qua vùng này.", throttleSeconds: 60);
            return null;
        }

        // A missing element is normal while TikTok replaces the live DOM.  Avoid
        // repeatedly capturing the same known-missing XPath until it has had time
        // to reappear (or until the next confirmed reload clears the cooldown).
        if (IsScanXPathCoolingDown(region)) return null;

        var scanStopwatch = System.Diagnostics.Stopwatch.StartNew();
        ImageMatcher.PreparedBitmap current;
        try
        {
            current = await CaptureScanRegionAsync(region, ct, captureCache);
            _xpathScanUnavailableUntil.Remove(ScanXPathCooldownKey(region));
        }
        catch (Exception ex) when (_chrome.IsCdpSessionLost(ex))
        {
            // Do not relabel a real CDP target/session loss as XPATH_SCAN_NOT_FOUND.
            // It needs the engine's bounded CDP recovery path; normal DOM misses do not.
            throw new RecoverableAutomationException(
                "CDP_SESSION_LOST",
                $"Vùng quét “{region.Name}”",
                "CDP/session/target thực sự mất khi quét XPath.",
                RecoveryDecision.SkipLive,
                ex);
        }
        catch (InvalidOperationException ex) when (!string.IsNullOrWhiteSpace(region.ScanXPath))
        {
            _xpathScanUnavailableUntil[ScanXPathCooldownKey(region)] = DateTime.Now.AddMilliseconds(XPathScanMissCooldownMs);
            ReportProblem("XPATH_SCAN_NOT_FOUND", $"Vùng quét “{region.Name}”", $"Không chụp được XPath vùng quét: {region.ScanXPath}. Chi tiết: {ex.Message}", throttleSeconds: 30);
            return null;
        }
        catch (Exception ex)
        {
            ReportProblem("REGION_CAPTURE_ERROR", $"Vùng quét “{region.Name}”", $"Không chụp được vùng để quét. XPath={region.ScanXPath}. Chi tiết: {ex.Message}", throttleSeconds: 20);
            return null;
        }

        using (current)
        {
            _log.Info("[PERF_SCAN] TemplateLoad START");
            var templateLoad = System.Diagnostics.Stopwatch.StartNew();
            var templates = new List<(string rel, string path, ImageMatcher.MultiScaleTemplate template)>(region.Images.Count);
            int validFiles = 0;
            foreach (var rel in region.Images)
            {
                var path = ResolveImage(rel);
                if (!File.Exists(path))
                {
                    ReportProblem("IMAGE_FILE_NOT_FOUND", $"Vùng quét “{region.Name}”", $"Không tìm thấy file ảnh mẫu: {rel}", throttleSeconds: 60);
                    continue;
                }
                validFiles++;
                try
                {
                    templates.Add((rel, path, GetOrLoadTemplate(path)));
                }
                catch (Exception ex)
                {
                    ReportProblem("IMAGE_SCAN_ERROR", $"Vùng quét “{region.Name}”", $"Ảnh {Path.GetFileName(path)} tải lỗi: {ex.Message}", throttleSeconds: 30);
                }
            }
            templateLoad.Stop();
            _log.Info($"[PERF_SCAN] TemplateLoad DONE count={templates.Count} elapsed={templateLoad.ElapsedMilliseconds}ms");

            if (validFiles == 0)
            {
                ReportProblem("IMAGE_TEMPLATE_INVALID", $"Vùng quét “{region.Name}”", "Không còn file ảnh mẫu hợp lệ để quét.", throttleSeconds: 60);
                return null;
            }

            foreach (var (rel, path, template) in templates)
            {
                var matchWatch = System.Diagnostics.Stopwatch.StartNew();
                _log.Info($"[PERF_SCAN] Match START region=\"{region.Name}\" template={Path.GetFileName(path)}");
                try
                {
                    var m = ImageMatcher.FindMultiScale(current, template, region.Variation, deadline);
                    matchWatch.Stop();
                    _log.Info($"[PERF_SCAN] Match DONE score={m.Score:F4} elapsed={matchWatch.ElapsedMilliseconds}ms");
                    if (m.Found)
                    {
                        _log.Warn($"Phát hiện vùng “{region.Name}”; ảnh={Path.GetFileName(path)}; hành động={region.Action}.");
                        if (scanStopwatch.ElapsedMilliseconds > ImageScanSlowMs)
                            _log.Warn($"[IMAGE_SCAN_SLOW] region=\"{region.Name}\" elapsed={scanStopwatch.ElapsedMilliseconds}ms");
                        return (rel, m);
                    }
                }
                catch (TimeoutException)
                {
                    _log.Warn($"[IMAGE_SCAN_TIMEOUT] region=\"{region.Name}\"");
                    return null;
                }
                catch (Exception ex)
                {
                    ReportProblem("IMAGE_SCAN_ERROR", $"Vùng quét “{region.Name}”", $"Ảnh {Path.GetFileName(path)} quét lỗi: {ex.Message}", throttleSeconds: 30);
                }
            }

            if (scanStopwatch.ElapsedMilliseconds > ImageScanSlowMs)
                _log.Warn($"[IMAGE_SCAN_SLOW] region=\"{region.Name}\" elapsed={scanStopwatch.ElapsedMilliseconds}ms");
        }
        return null;
    }

    async Task<bool> ConfirmStopAsync(ScanRegion region, string sameTemplate, CancellationToken ct)
    {
        var path = ResolveImage(sameTemplate);
        if (!File.Exists(path)) return false;
        var template = GetOrLoadTemplate(path);
        for (int i = 2; i <= 3; i++)
        {
            await Task.Delay(StopConfirmDelayMs, ct);
            try
            {
                using var current = await CaptureScanRegionAsync(region, ct);
                var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + ImageScanTimeoutMs * System.Diagnostics.Stopwatch.Frequency / 1000;
                if (!ImageMatcher.FindMultiScale(current, template, region.Variation, deadline).Found) return false;
            }
            catch (TimeoutException)
            {
                _log.Warn($"[IMAGE_SCAN_TIMEOUT] region=\"{region.Name}\"");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                if (_chrome.IsCdpSessionLost(ex)) throw;
                ReportProblem("STOP_VERIFY_XPATH", $"Vùng STOP “{region.Name}”", $"Không thể xác nhận XPath/ảnh ở lần {i}/3: {ex.Message}", throttleSeconds: 30);
                return false;
            }
        }
        _log.Warn($"STOP đã được xác nhận 3 lần liên tiếp ở vùng “{region.Name}”; ảnh={sameTemplate}.");
        return true;
    }

    async Task<bool> ScanAndProcessBeforeClickAsync(CancellationToken ct)
    {
        var perf = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var hit = await FindFirstMatchAsync(ScanMode.AllNonPeriodic, ct);
            if (hit is null)
            {
                if (_consecutiveCount > 0) ResetConsecutive("không còn ảnh lỗi ở lần quét kế tiếp");
                return false;
            }
            if (hit.Region.Action.Equals("STOP", StringComparison.OrdinalIgnoreCase))
            {
                ResetConsecutive("chuyển sang vùng STOP");
                Stop($"Vùng ảnh STOP “{hit.Region.Name}” đã khớp ổn định 3 lần; ảnh={hit.Template}");
                return true;
            }
            await ProcessImageRegionAsync(hit, CurrentRestartStep, CurrentInputXPath, CurrentPointName, ct);
            return true;
        }
        finally
        {
            perf.Stop();
            _log.Info($"[STEP_PERF] step=preClickScan:AllNonPeriodic elapsedMs={perf.ElapsedMilliseconds}");
        }
    }

    void SetConsecutiveNew(ScanRegion region)
    {
        _consecutiveRegion = region.Name;
        _consecutiveCount = 1;
    }

    void IncreaseConsecutive(ScanRegion region)
    {
        if (_consecutiveRegion == region.Name && _consecutiveCount > 0) _consecutiveCount++;
        else { _consecutiveRegion = region.Name; _consecutiveCount = 1; }
    }

    int ConsecutiveActionCount(ScanRegion region)
    {
        var max = Math.Clamp(region.ConsecutiveMax, 1, 4);
        if (max <= 1 || region.Action.Equals("STOP", StringComparison.OrdinalIgnoreCase)) return 1;
        return Math.Min(max, Math.Max(1, _consecutiveCount));
    }

    void ResetConsecutive(string reason)
    {
        if (_consecutiveCount > 1)
            _log.Info($"Reset chuỗi ảnh lỗi liên tiếp sau {_consecutiveCount} lần: {reason}; vùng={_consecutiveRegion}.");
        _consecutiveRegion = "";
        _consecutiveCount = 0;
    }

    async Task ProcessImageRegionAsync(RegionHit initial, int restartStep, string inputXPath, string pointName, CancellationToken ct)
    {
        _step = restartStep;
        var current = initial;
        if (_consecutiveRegion == current.Region.Name && _consecutiveCount > 0) IncreaseConsecutive(current.Region);
        else SetConsecutiveNew(current.Region);

        while (_running && !ct.IsCancellationRequested)
        {
            await WaitIfPausedAsync(ct);
            if (HasPriorityDue())
            {
                ResetConsecutive("quét định kỳ ưu tiên tiếp quản");
                await HandlePriorityDueAsync(ct);
                return;
            }

            if (current.Region.Action.Equals("STOP", StringComparison.OrdinalIgnoreCase))
            {
                ResetConsecutive("chuyển sang vùng STOP");
                Stop($"Đã tìm thấy ảnh STOP “{current.Region.Name}” và xác nhận liên tiếp.");
                return;
            }

            int count = ConsecutiveActionCount(current.Region);
            _log.Warn($"Vùng “{current.Region.Name}” tại {pointName}: chuỗi lỗi={_consecutiveCount}; thao tác ×{count} rồi F5.");
            SetStatus("XỬ LÝ ẢNH LỖI", $"{current.Region.Name}: thao tác ×{count} rồi F5.");

            int afterReload = _s.Viewer.Enabled ? Math.Max(0, _s.Viewer.WaitAfterF5Sec * 1000) : F5WaitMs;
            if (current.Region.Action.Equals("CLICK_F5", StringComparison.OrdinalIgnoreCase) && !_s.UseArrowDownForLiveSwitch)
            {
                var xp = string.IsNullOrWhiteSpace(current.Region.ActionXPath) ? _s.XPathPeriodicAction : current.Region.ActionXPath;
                if (string.IsNullOrWhiteSpace(xp))
                {
                    ReportProblem("XPATH_ACTION_MISSING", $"Vùng “{current.Region.Name}”", "Hành động CLICK_F5 nhưng chưa có XPath nút bấm (và XPath nút chuyển live chung cũng trống). Đã bỏ qua hành động; không dùng tọa độ fallback.", error: true, throttleSeconds: 30);
                    ResetConsecutive("thiếu XPath hành động");
                    return;
                }
                if (!await TransitionAsync($"vùng ảnh “{current.Region.Name}”", TransitionAction.ClickXPath, xp, count, scheduledPeriodic: false, ct, afterReload))
                {
                    _log.Warn($"Vòng chuyển live của vùng “{current.Region.Name}” chưa thực hiện được; giữ chuỗi lỗi={_consecutiveCount} và cooldown 1500 ms để tránh quét dồn dập.");
                    await Task.Delay(1500, ct);
                    return;
                }
            }
            else
            {
                if (!await TransitionAsync($"vùng ảnh “{current.Region.Name}”", TransitionAction.ArrowDown, "", count, scheduledPeriodic: false, ct, afterReload))
                {
                    _log.Warn($"Vòng chuyển live của vùng “{current.Region.Name}” chưa hoàn tất; giữ chuỗi lỗi={_consecutiveCount} và cooldown 1500 ms.");
                    await Task.Delay(1500, ct);
                    return;
                }
            }

            if (HasPriorityDue())
            {
                ResetConsecutive("quét định kỳ ưu tiên tiếp quản ngay sau F5 ảnh thường");
                await HandlePriorityDueAsync(ct);
                return;
            }

            // V10.4.4: sau F5 click lại đúng ô nhập để kích hoạt thông báo chỉ hiện khoảng 1 giây.
            try { await _chrome.ClickXPathAsync(inputXPath, ct: ct); }
            catch (Exception ex) { ReportProblem("XPATH_RECHECK_FAILED", $"{pointName}", $"Không click lại được ô nhập sau F5 để xác minh ảnh. XPath={inputXPath}. Chi tiết: {ex.Message}", throttleSeconds: 20); }

            RegionHit? next = null;
            var verifyEnd = Environment.TickCount64 + VerifyAfterF5Ms;
            while (_running && !_paused && Environment.TickCount64 <= verifyEnd)
            {
                if (HasPriorityDue())
                {
                    ResetConsecutive("quét định kỳ ưu tiên tiếp quản trong lúc xác minh ảnh sau F5");
                    await HandlePriorityDueAsync(ct);
                    return;
                }
                next = await FindFirstMatchAsync(ScanMode.AllNonPeriodic, ct);
                if (next is not null) break;
                await Task.Delay(ScanIntervalMs, ct);
            }

            if (next is not null)
            {
                if (next.Region.Action.Equals("STOP", StringComparison.OrdinalIgnoreCase))
                {
                    ResetConsecutive("chuyển sang vùng STOP sau F5");
                    Stop($"Sau F5 đã tìm thấy vùng STOP “{next.Region.Name}”; ảnh đã xác nhận 3 lần.");
                    return;
                }
                IncreaseConsecutive(next.Region);
                current = next;
                _log.Warn($"Sau F5 vẫn thấy vùng “{current.Region.Name}”: chuỗi liên tiếp={_consecutiveCount}; lượt kế tiếp thao tác ×{ConsecutiveActionCount(current.Region)}.");
                continue;
            }

            ResetConsecutive("không còn ảnh lỗi sau F5");
            _log.Info("Quét điểm ảnh sau hành động + F5: không còn hạn chế.");

            // V9.3+: chỉ sau khi hết ảnh hạn chế mới OCR người xem.
            if (_s.Viewer.Enabled)
                await RunViewerCheckNowAsync(ct, "sau khi hết ảnh hạn chế");

            if (_running && !_paused) await Task.Delay(ActionDelay(), ct);
            return;
        }
    }

    bool HasPriorityDue() => _s.ScanRegions.Any(r => r.Enabled && r.PeriodicEnabled && DateTime.Now >= r.NextPeriodicAt);

    async Task<bool> HandlePriorityDueAsync(CancellationToken ct)
    {
        if (!HasPriorityDue()) return false;
        var due = _s.ScanRegions.Where(r => r.Enabled && r.PeriodicEnabled && DateTime.Now >= r.NextPeriodicAt).ToList();
        if (due.Count == 0) return false;

        var started = DateTime.Now;
        var total = System.Diagnostics.Stopwatch.StartNew();
        _log.Warn($"Quét định kỳ ưu tiên đến hạn ({string.Join(", ", due.Select(r => r.Name))}). Khóa hành động khác 5 giây.");
        SetStatus("QUÉT ƯU TIÊN", "Đã khóa hành động khác; chờ 5 giây cho giao diện ổn định.");
        try
        {
            await Task.Delay(PriorityPauseMs, ct);
            // Trong toàn bộ lượt ưu tiên countdown F5 định kỳ đang bị "đóng băng";
            // chờ Pause ở đây không gọi WaitIfPausedAsync để tránh cộng thời gian hai lần.
            while (_running && _paused) await Task.Delay(200, ct);
            if (!_running) return true;

            var hit = await FindFirstMatchAsync(ScanMode.Priority, ct);
            var now = DateTime.Now;
            foreach (var r in due) r.NextPeriodicAt = now.AddMinutes(Math.Max(1, r.PeriodicMinutes));

            // V10 không tính toàn bộ thời gian quét ưu tiên vào countdown F5 định kỳ.
            ShiftPeriodicClock(DateTime.Now - started);

            if (hit is null)
            {
                _log.Info($"Quét định kỳ ưu tiên hoàn tất: không thấy ảnh trong {due.Count} vùng đến hạn.");
                // Sau ưu tiên: quét thường lại ở vòng kế tiếp, rồi OCR ngay nếu bật.
                if (_s.Viewer.Enabled) _nextViewer = DateTime.Now;
                await Task.Delay(50, ct);
                return true;
            }

            _log.Warn($"Quét định kỳ ưu tiên trúng vùng “{hit.Region.Name}”; ảnh={hit.Template}; hành động={hit.Region.Action}.");
            if (hit.Region.Action.Equals("STOP", StringComparison.OrdinalIgnoreCase))
            {
                ResetConsecutive("quét ưu tiên gặp STOP");
                Stop($"QUÉT ĐỊNH KỲ ƯU TIÊN đã tìm thấy vùng STOP “{hit.Region.Name}”; ảnh đã xác nhận 3 lần.");
                return true;
            }

            await ProcessImageRegionAsync(hit, CurrentRestartStep, CurrentInputXPath, CurrentPointName + " - quét định kỳ ưu tiên", ct);
            return true;
        }
        finally
        {
            total.Stop();
            _log.Info($"[PERF_SCAN] PriorityScan DONE total={total.ElapsedMilliseconds}ms");
        }
    }

    void ShiftPeriodicClock(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero || _s.PeriodicF5Minutes <= 0) return;
        if (_periodicDue != DateTime.MaxValue) _periodicDue += delta;
        if (_candidateCaptureAt != DateTime.MaxValue) _candidateCaptureAt += delta;
        SyncPeriodicSnapshot();
    }

    async Task<bool> HandlePeriodicCaptureAndF5Async(CancellationToken ct)
    {
        if (_s.PeriodicF5Minutes <= 0) return false;
        var now = DateTime.Now;

        if (_s.StrictXPathOnly && _s.OldLive.Enabled && string.IsNullOrWhiteSpace(_s.OldLive.XPath) && now >= _candidateCaptureAt)
        {
            ReportProblem("XPATH_OLDLIVE_MISSING", "Live cũ", "Đã bật Live cũ nhưng XPath vùng nhận dạng đang trống. Bỏ qua chụp T-10s; không dùng vùng tọa độ fallback.", error: true, throttleSeconds: 30);
            _candidateCaptureAt = DateTime.MaxValue;
            return true;
        }

        if (_s.OldLive.Enabled && now >= _candidateCaptureAt)
        {
            try
            {
                _log.Info("[OLD_LIVE_CAPTURE_START]");
                var captured = await CaptureOldLiveBytesAsync(ct);
                _candidateCaptureAt = DateTime.MaxValue;
                AddOldLiveEntry(captured, "PERIODIC_T_MINUS_10");
                _nextOldLiveScan = DateTime.Now.AddMilliseconds(OldLiveScanRetryMs);
                _log.Info("[OLD_LIVE_CAPTURE_OK]");
                _log.Info("F5 định kỳ còn <=10 giây: đã chụp và thêm ngay LIVE CŨ vào danh sách active.");
                SetStatus("LIVE CŨ T-10s", "Đã chụp và thêm ngay vào danh sách Live cũ active.");
                return true;
            }
            catch (Exception ex)
            {
                if (now < _periodicDue)
                {
                    // V10.4.4 thử lại ở tick sau nếu chưa chụp được, nhưng không được chặn mốc F5 đã đến hạn.
                    _candidateCaptureAt = DateTime.Now.AddSeconds(1);
                    _log.Warn("F5 định kỳ còn <=10 giây nhưng chưa chụp được Live cũ; sẽ thử lại: " + ex.Message);
                    return true;
                }

                _candidateCaptureAt = DateTime.MaxValue;
                _log.Warn("Đã đến hạn F5 định kỳ nhưng chưa chụp được Live cũ; tiếp tục F5: " + ex.Message);
            }
        }

        if (now < _periodicDue) return false;

        // V10 cho bước 4/8 hoàn tất trước để tránh lặp lại bình luận vừa gửi xong.
        if (_step is 4 or 8) return false;

        if (!_s.UseArrowDownForLiveSwitch && string.IsNullOrWhiteSpace(_s.XPathPeriodicAction))
        {
            ReportProblem("XPATH_PERIODIC_MISSING", "F5 định kỳ", "XPath nút chuyển live đang trống. Đã lùi 30 giây và bỏ qua lần này; không dùng tọa độ fallback.", error: true, throttleSeconds: 30);
            _periodicDue = now.AddSeconds(30);
            _candidateCaptureAt = _periodicDue.AddSeconds(-10);
            SyncPeriodicSnapshot();
            return true;
        }

        _step = CurrentRestartStep;
        var periodicAction = _s.UseArrowDownForLiveSwitch ? TransitionAction.ArrowDown : TransitionAction.ClickXPath;
        _periodicExecuting = true;
        SyncPeriodicSnapshot();
        bool periodicOk;
        try
        {
            periodicOk = await TransitionAsync("F5 định kỳ", periodicAction, _s.XPathPeriodicAction, 1, scheduledPeriodic: true, ct, F5WaitMs);
        }
        finally
        {
            _periodicExecuting = false;
            SyncPeriodicSnapshot();
        }
        if (!periodicOk)
        {
            _log.Warn("F5 định kỳ chưa thực hiện được; các ảnh Live cũ đã lưu vẫn được giữ nguyên tới khi từng ảnh hết TTL. Sẽ thử lại sau 30 giây.");
            _periodicDue = DateTime.Now.AddSeconds(30);
            _candidateCaptureAt = _periodicDue.AddSeconds(-10);
            SyncPeriodicSnapshot();
            return true;
        }

        if (_s.OldLive.Enabled && _activeOldLives.Count > 0)
            _log.Info("F5 định kỳ hoàn tất; giữ nguyên mọi ảnh Live cũ active cho tới khi từng ảnh hết TTL.");

        if (_s.Viewer.Enabled) _nextViewer = DateTime.Now;
        else await Task.Delay(ActionDelay(), ct);
        return true;
    }

    void ResetPeriodicDue(string reason, bool cancelCandidate)
    {
        if (_s.PeriodicF5Minutes <= 0)
        {
            _periodicDue = DateTime.MaxValue;
            _candidateCaptureAt = DateTime.MaxValue;
            SyncPeriodicSnapshot();
            return;
        }
        _periodicDue = DateTime.Now.AddMinutes(_s.PeriodicF5Minutes);
        _candidateCaptureAt = _periodicDue.AddSeconds(-10);
        _log.Info($"Đã reset bộ đếm F5 định kỳ về {_s.PeriodicF5Minutes} phút: {reason}");
        SyncPeriodicSnapshot();
    }

    async Task<bool> HandleOldLiveExpiryAndScanAsync(CancellationToken ct)
    {
        CleanupExpiredOldLives();
        if (_activeOldLives.Count == 0) return false;
        var now = DateTime.Now;
        if (now < _nextOldLiveScan || _transitioning) return false;
        if (_s.PeriodicF5Minutes <= 0) return false;
        // V10: nếu F5 định kỳ còn <=3 giây/đã đến hạn thì ưu tiên đúng vòng định kỳ;
        // đồng thời không chen Live cũ vào bước 4/8 vừa hoàn tất bình luận.
        if (_periodicDue != DateTime.MaxValue && _periodicDue - now <= TimeSpan.FromSeconds(3)) return false;
        if (_step is 4 or 8) return false;
        _nextOldLiveScan = now.AddMilliseconds(OldLiveScanIntervalMs);

        try
        {
            _log.Info($"[OLD_LIVE_SCAN_START] activeCount={_activeOldLives.Count}");
            var currentBytes = await CaptureOldLiveBytesAsync(ct);
            using var current = ImageMatcher.FromBytes(currentBytes);
            using var preparedCurrent = new ImageMatcher.PreparedBitmap(current);
            var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + ImageScanTimeoutMs * System.Diagnostics.Stopwatch.Frequency / 1000;
            OldLiveEntry? matchedEntry = null;
            ImageMatchResult match = new(false);
            foreach (var entry in _activeOldLives.OrderBy(e => e.ExpiresAt))
            {
                if (!File.Exists(entry.ImagePath))
                {
                    _log.Warn($"[OLD_LIVE_COMPARE] id={entry.Id} warning=file-missing");
                    continue;
                }
                _log.Info($"[OLD_LIVE_COMPARE] id={entry.Id} file={Path.GetFileName(entry.ImagePath)}");
                var template = GetOrLoadTemplate(entry.ImagePath);
                match = ImageMatcher.FindMultiScale(preparedCurrent, template, _s.OldLive.Variation, deadline);
                if (!match.Found) continue;
                matchedEntry = entry;
                break;
            }
            if (matchedEntry is null)
            {
                _lastOldLiveMatchAt = DateTime.Now;
                _lastOldLiveMatchFound = false;
                _lastOldLiveMatchImage = "";
                _lastOldLiveMatchScore = null;
                _log.Info("[OLD_LIVE_NO_MATCH]");
                return false;
            }

            var remaining = matchedEntry.ExpiresAt - DateTime.Now;
            var age = DateTime.Now - matchedEntry.CreatedAt;
            _lastOldLiveMatchAt = DateTime.Now;
            _lastOldLiveMatchFound = true;
            _lastOldLiveMatchImage = Path.GetFileName(matchedEntry.ImagePath);
            _lastOldLiveMatchScore = match.Score;
            _log.Warn($"[OLD_LIVE_MATCH] id={matchedEntry.Id} age={age:c} remaining={remaining:c} score={match.Score:F4}");

            _log.Warn("LIVE CŨ: ảnh tạm đã KHỚP. Thực hiện lại vòng chuyển live + F5; không ghi đè/gia hạn ảnh đang dùng.");
            var xp = string.IsNullOrWhiteSpace(_s.OldLive.ActionXPath) ? _s.XPathPeriodicAction : _s.OldLive.ActionXPath;
            if (!_s.UseArrowDownForLiveSwitch && string.IsNullOrWhiteSpace(xp))
            {
                ReportProblem("XPATH_OLDLIVE_ACTION_MISSING", "Live cũ", "Ảnh Live cũ đã khớp nhưng XPath nút chuyển live đang trống. Đã bỏ qua hành động; không dùng tọa độ fallback.", error: true, throttleSeconds: 30);
                return false;
            }
            _step = CurrentRestartStep;
            var oldLiveAction = _s.UseArrowDownForLiveSwitch ? TransitionAction.ArrowDown : TransitionAction.ClickXPath;
            var oldLiveTransitioned = await TransitionAsync("phát hiện LIVE CŨ", oldLiveAction, xp, 1, scheduledPeriodic: false, ct, F5WaitMs);
            if (!oldLiveTransitioned)
            {
                _nextOldLiveScan = DateTime.Now.AddMilliseconds(OldLiveScanRetryMs);
                return false;
            }
            _nextOldLiveScan = DateTime.Now.AddMilliseconds(OldLiveScanRetryMs);
            if (_s.Viewer.Enabled) _nextViewer = DateTime.Now;
            return true;
        }
        catch (TimeoutException)
        {
            _log.Warn("[IMAGE_SCAN_TIMEOUT] region=\"old-live\"");
            return false;
        }
        catch (Exception ex)
        {
            ReportProblem("OLDLIVE_SCAN_ERROR", "Live cũ", ex.Message, throttleSeconds: 20);
            return false;
        }
    }

    async Task<byte[]> CaptureOldLiveBytesAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_s.OldLive.XPath))
            return await _chrome.CaptureXPathAsync(_s.OldLive.XPath, ct);

        if (_s.StrictXPathOnly)
            throw new InvalidOperationException("Live cũ: XPath vùng nhận dạng đang trống và chế độ Chỉ XPath đang bật.");

        var view = await _chrome.CaptureViewportAsync(ct);
        using var bmp = ImageMatcher.FromBytes(view);
        using var crop = ImageMatcher.CropNormalized(bmp, _s.OldLive.RX1, _s.OldLive.RY1, _s.OldLive.RX2, _s.OldLive.RY2);
        using var ms = new MemoryStream();
        crop.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    string OldLiveDirectoryPath => Path.Combine(_baseDir, OldLiveDirectoryName);
    string OldLiveManifestPath => Path.Combine(OldLiveDirectoryPath, OldLiveManifestFileName);

    void EnsureOldLivesReadyForRun()
    {
        LoadPersistedOldLives();
        MigrateUntrackedOldLiveFiles();
        CleanupExpiredOldLives();
        _nextOldLiveScan = _s.OldLive.Enabled && _activeOldLives.Count > 0
            ? DateTime.Now.AddMilliseconds(OldLiveScanRetryMs)
            : DateTime.MaxValue;
    }

    void LoadPersistedOldLives()
    {
        if (_oldLiveManifestLoaded) return;
        _oldLiveManifestLoaded = true;
        if (!File.Exists(OldLiveManifestPath)) return;

        try
        {
            var entries = JsonSerializer.Deserialize<List<PersistedOldLiveEntry>>(File.ReadAllText(OldLiveManifestPath), OldLiveStoreJson) ?? [];
            foreach (var persisted in entries)
            {
                if (!TryGetManagedOldLivePath(persisted.FileName, out var imagePath))
                {
                    _log.Warn($"[OLD_LIVE_RESTORE] ignored unsafe file name: {persisted.FileName}");
                    continue;
                }
                if (!File.Exists(imagePath))
                {
                    _log.Warn($"[OLD_LIVE_RESTORE] id={persisted.Id} warning=file-missing");
                    continue;
                }
                if (_activeOldLives.Any(e => e.Id.Equals(persisted.Id, StringComparison.OrdinalIgnoreCase)
                    || e.ImagePath.Equals(imagePath, StringComparison.OrdinalIgnoreCase))) continue;

                _activeOldLives.Add(new OldLiveEntry(persisted.Id, imagePath, persisted.CreatedAt, persisted.ExpiresAt, persisted.Source));
            }
            if (_activeOldLives.Count > 0)
                _log.Info($"[OLD_LIVE_RESTORE] restored={_activeOldLives.Count} manifest={OldLiveManifestPath}");
            CleanupExpiredOldLives();
        }
        catch (Exception ex)
        {
            _log.Warn($"[OLD_LIVE_RESTORE] cannot read manifest: {ex.Message}");
        }
    }

    void MigrateUntrackedOldLiveFiles()
    {
        if (_legacyOldLiveFilesChecked) return;
        _legacyOldLiveFilesChecked = true;
        if (!Directory.Exists(OldLiveDirectoryPath)) return;

        var migrated = 0;
        foreach (var file in Directory.EnumerateFiles(OldLiveDirectoryPath, "old_live_*.png", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (!TryGetManagedOldLivePath(fileName, out var imagePath)
                || _activeOldLives.Any(e => e.ImagePath.Equals(imagePath, StringComparison.OrdinalIgnoreCase))) continue;

            var createdAt = File.GetLastWriteTime(imagePath);
            var expiresAt = createdAt.AddMinutes(Math.Max(1, _s.OldLive.KeepMinutes));
            _activeOldLives.Add(new OldLiveEntry(GenerateOldLiveId(createdAt), imagePath, createdAt, expiresAt, "LEGACY_FILE_MIGRATION"));
            migrated++;
        }
        if (migrated == 0) return;

        _log.Info($"[OLD_LIVE_MIGRATE] imported={migrated} fallbackKeepMinutes={Math.Max(1, _s.OldLive.KeepMinutes)}");
        CleanupExpiredOldLives();
        PersistOldLiveManifest();
    }

    bool TryGetManagedOldLivePath(string fileName, out string imagePath)
    {
        imagePath = "";
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.Equals(Path.GetFileName(fileName), StringComparison.Ordinal)
            || !fileName.StartsWith("old_live_", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;

        var directory = Path.GetFullPath(OldLiveDirectoryPath);
        var candidate = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!candidate.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        imagePath = candidate;
        return true;
    }

    void PersistOldLiveManifest()
    {
        try
        {
            Directory.CreateDirectory(OldLiveDirectoryPath);
            var entries = _activeOldLives
                .OrderBy(e => e.ExpiresAt)
                .Select(e => new PersistedOldLiveEntry(e.Id, Path.GetFileName(e.ImagePath), e.CreatedAt, e.ExpiresAt, e.Source))
                .ToList();
            var temporaryPath = OldLiveManifestPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, OldLiveStoreJson));
            File.Move(temporaryPath, OldLiveManifestPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.Warn($"[OLD_LIVE_STORE] cannot persist manifest: {ex.Message}");
        }
    }

    void AddOldLiveEntry(byte[] imageBytes, string source)
    {
        var now = DateTime.Now;
        var entry = new OldLiveEntry(
            GenerateOldLiveId(now),
            TempImagePath($"old_live_{now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.png"),
            now,
            now.AddMinutes(Math.Max(1, _s.OldLive.KeepMinutes)),
            source);
        File.WriteAllBytes(entry.ImagePath, imageBytes);
        _activeOldLives.Add(entry);
        _lastOldLiveCapturedAt = now;
        _log.Info($"[OLD_LIVE_ADDED] id={entry.Id} activeCount={_activeOldLives.Count} expiresAt={entry.ExpiresAt:O}");
        CleanupExpiredOldLives();
        PersistOldLiveManifest();
    }

    void CleanupExpiredOldLives()
    {
        var now = DateTime.Now;
        var removed = false;
        foreach (var entry in _activeOldLives.Where(e => now >= e.ExpiresAt).ToList())
        {
            _activeOldLives.Remove(entry);
            _log.Info($"[OLD_LIVE_EXPIRED] id={entry.Id} expiresAt={entry.ExpiresAt:O}");
            DeleteOldLiveEntryFile(entry, "ttl-expired");
            removed = true;
        }
        foreach (var entry in _activeOldLives.Where(e => !File.Exists(e.ImagePath)).ToList())
        {
            _activeOldLives.Remove(entry);
            _templateCache.Remove(entry.ImagePath, out var cached);
            cached?.Dispose();
            _log.Warn($"[OLD_LIVE_MISSING] id={entry.Id} file={Path.GetFileName(entry.ImagePath)}");
            removed = true;
        }
        if (_activeOldLives.Count == 0)
            _nextOldLiveScan = DateTime.MaxValue;
        if (removed) PersistOldLiveManifest();
    }

    public int ClearOldLivesManually()
    {
        if (_running) throw new InvalidOperationException("Hãy dừng tool trước khi xóa ảnh Live cũ active.");
        EnsureOldLivesReadyForRun();
        var entries = _activeOldLives.ToList();
        foreach (var entry in entries)
        {
            _activeOldLives.Remove(entry);
            DeleteOldLiveEntryFile(entry, "manual");
        }
        _nextOldLiveScan = DateTime.MaxValue;
        PersistOldLiveManifest();
        _log.Warn($"[OLD_LIVE_MANUAL_CLEAR] removed={entries.Count}");
        return entries.Count;
    }

    void DeleteOldLiveEntryFile(OldLiveEntry entry, string reason)
    {
        try
        {
            if (File.Exists(entry.ImagePath)) File.Delete(entry.ImagePath);
            _templateCache.Remove(entry.ImagePath, out var cached);
            cached?.Dispose();
            _log.Info($"[OLD_LIVE_DELETED] reason={reason} id={entry.Id} file={Path.GetFileName(entry.ImagePath)}");
        }
        catch (Exception ex)
        {
            _log.Warn($"[OLD_LIVE_DELETED] reason={reason} id={entry.Id} warning={ex.Message}");
        }
    }

    static string GenerateOldLiveId(DateTime now) => $"old_live_{now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";

    async Task<bool> HandleViewerDueAsync(CancellationToken ct)
    {
        if (!_s.Viewer.Enabled || DateTime.Now < _nextViewer) return false;
        await RunViewerCheckNowAsync(ct, _rounds == 0 && _step == 1 ? "kiểm tra lúc khởi động" : "kiểm tra định kỳ");
        return true;
    }

    async Task RunViewerCheckNowAsync(CancellationToken ct, string source)
    {
        if (!_s.Viewer.Enabled) return;
        SetStatus("ĐỌC NGƯỜI XEM", $"{source} • đang đọc XPath người xem");
        var (value, raw) = await ReadViewerAsync(ct);
        if (value < 0)
        {
            _log.Warn($"OCR/người xem không đọc được ({source}); bỏ qua lần kiểm tra này.");
            ScheduleNextViewer();
            return;
        }

        _log.Info($"Kiểm tra người xem ({source}): {value}; ngưỡng={_s.Viewer.Threshold}; raw={raw}");
        if (value > _s.Viewer.Threshold)
        {
            ScheduleNextViewer();
            return;
        }

        // V10: kết quả thấp đầu tiên được xác nhận ngay trong cùng một lượt, cách nhau 2 giây.
        int lowCount = 1;
        while (lowCount < Math.Max(1, _s.Viewer.ConfirmLow))
        {
            await Task.Delay(2000, ct);
            await WaitIfPausedAsync(ct);
            if (HasPriorityDue())
            {
                await HandlePriorityDueAsync(ct);
                _nextViewer = DateTime.Now; // sau ưu tiên sẽ OCR lại ngay
                return;
            }

            var (confirm, rawConfirm) = await ReadViewerAsync(ct);
            if (confirm < 0)
            {
                _log.Warn("Không đọc được khi xác nhận số người xem thấp; bỏ qua lần này.");
                ScheduleNextViewer();
                return;
            }
            lowCount++;
            _log.Info($"Xác nhận thấp {lowCount}/{_s.Viewer.ConfirmLow}: {confirm}; raw={rawConfirm}");
            if (confirm > _s.Viewer.Threshold)
            {
                ScheduleNextViewer();
                return;
            }
            value = confirm;
        }

        await HandleLowViewerLoopAsync(value, ct);
        if (_running) ScheduleNextViewer();
    }

    void ScheduleNextViewer() => _nextViewer = _s.Viewer.Enabled
        ? DateTime.Now.AddSeconds(Math.Max(1, _s.Viewer.IntervalSec))
        : DateTime.MaxValue;

    async Task HandleLowViewerLoopAsync(int initial, CancellationToken ct)
    {
        int max = Math.Max(1, _s.Viewer.MaxF5);
        _log.Warn($"Bắt đầu xử lý người xem thấp {initial} ≤ {_s.Viewer.Threshold}; tối đa {max} vòng ↓ + F5.");

        for (int i = 1; i <= max && _running; i++)
        {
            await WaitIfPausedAsync(ct);
            if (HasPriorityDue())
            {
                await HandlePriorityDueAsync(ct);
                _nextViewer = DateTime.Now;
                return;
            }

            if (!await TransitionAsync($"người xem thấp vòng {i}/{max}", TransitionAction.ArrowDown, "", 1, scheduledPeriodic: false, ct,
                Math.Max(0, _s.Viewer.WaitAfterF5Sec * 1000)))
            {
                ReportProblem("VIEWER_TRANSITION_FAILED", "Người xem thấp", $"Vòng {i}/{max} không thực hiện được thao tác chuyển live; đã dừng riêng chuỗi xử lý người xem.", error: true, throttleSeconds: 15);
                return;
            }

            if (HasPriorityDue())
            {
                await HandlePriorityDueAsync(ct);
                _nextViewer = DateTime.Now;
                return;
            }

            var (value, raw) = await ReadViewerAsync(ct);
            if (value < 0)
            {
                _log.Warn("Sau F5 vẫn không đọc được người xem; kết thúc riêng phần OCR và tiếp tục vòng chính.");
                return;
            }
            _log.Info($"Sau F5 vòng {i}/{max}: {value} người xem; raw={raw}");
            if (value > _s.Viewer.Threshold) return;
        }

        await SkipCurrentLiveAsync("VIEWER_LOW_PERSISTENT", "Người xem thấp", $"Số người xem vẫn không vượt {_s.Viewer.Threshold} sau {max} vòng ↓ + F5.", ct);
    }

    async Task<(int value, string raw)> ReadViewerAsync(CancellationToken ct)
    {
        for (int i = 0; i < Math.Max(1, _s.Viewer.OcrRetries); i++)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_s.Viewer.XPath))
                {
                    if (!await _chrome.XPathExistsAsync(_s.Viewer.XPath, ct))
                    {
                        ReportProblem("VIEWER_XPATH_NOT_FOUND", "Người xem", $"Không tìm thấy XPath người xem trên trang hiện tại: {_s.Viewer.XPath}", throttleSeconds: 30);
                        if (_s.StrictXPathOnly) return (-1, "");
                    }
                    else
                    {
                        var text = await _chrome.GetTextAsync(_s.Viewer.XPath, ct);
                        var v = _ocr.ParseViewerCount(text);
                        if (v >= 0) return (v, text);
                        ReportProblem("VIEWER_PARSE_FAILED", "Người xem", $"raw=\"{text}\"", throttleSeconds: 30);
                        if (_s.StrictXPathOnly) return (-1, text);
                    }
                }
                else if (_s.StrictXPathOnly)
                {
                    ReportProblem("VIEWER_XPATH_MISSING", "Người xem", "Chưa cấu hình XPath người xem", error: true, throttleSeconds: 30);
                    return (-1, "");
                }

                var bytes = await _chrome.CaptureViewportAsync(ct);
                using var bmp = ImageMatcher.FromBytes(bytes);
                using var crop = ImageMatcher.CropNormalized(bmp, _s.Viewer.RX1, _s.Viewer.RY1, _s.Viewer.RX2, _s.Viewer.RY2);
                var logDir = Path.Combine(_baseDir, "logs");
                Directory.CreateDirectory(logDir);
                var path = Path.Combine(logDir, "tesseract_debug.png");
                crop.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                var raw = await _ocr.ReadAsync(path, ct);
                var n = _ocr.ParseViewerCount(raw);
                if (n >= 0) return (n, raw);
            }
            catch (Exception ex)
            {
                ReportProblem("VIEWER_READ_ERROR", "Người xem", $"Lần đọc {i + 1}/{Math.Max(1, _s.Viewer.OcrRetries)} lỗi: {ex.Message}", throttleSeconds: 20);
            }
            await Task.Delay(250, ct);
        }
        return (-1, "");
    }

    enum TransitionAction { ArrowDown, ClickXPath }
    sealed record LiveSwitchVerification(bool Changed, string BeforeIdentity, string AfterIdentity, int Attempt, long ElapsedMs);

    static string TrimIdentityForLog(string identity, int max = 220)
    {
        if (string.IsNullOrWhiteSpace(identity)) return "(empty)";
        identity = identity.Replace("\r", " ").Replace("\n", " ").Trim();
        return identity.Length <= max ? identity : identity[..max] + "...";
    }

    async Task<string> GetCurrentLiveIdentityAsync(CancellationToken ct)
    {
        var identity = await _chrome.GetCurrentLiveIdentityAsync(ct);
        return string.IsNullOrWhiteSpace(identity) ? "(unknown)" : identity;
    }

    static bool HasReliableLiveIdentity(string identity)
        => !string.IsNullOrWhiteSpace(identity)
        && !identity.Equals("(unknown)", StringComparison.Ordinal)
        && (identity.Contains("roomId=", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("broadcaster=", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("/live/", StringComparison.OrdinalIgnoreCase));

    async Task<LiveSwitchVerification> WaitForLiveChangedAsync(string source, string beforeIdentity, int attempt, int maxAttempts, CancellationToken ct)
    {
        _log.Info($"[LIVE_VERIFY_WAIT] source={source} attempt={attempt}/{maxAttempts} timeoutMs={LiveVerifyTimeoutMs} intervalMs={LiveVerifyPollMs}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string latestIdentity = beforeIdentity;
        while (sw.ElapsedMilliseconds < LiveVerifyTimeoutMs)
        {
            await Task.Delay(LiveVerifyPollMs, ct);
            latestIdentity = await GetCurrentLiveIdentityAsync(ct);
            if (!string.Equals(latestIdentity, beforeIdentity, StringComparison.Ordinal))
                return new LiveSwitchVerification(true, beforeIdentity, latestIdentity, attempt, sw.ElapsedMilliseconds);
        }
        return new LiveSwitchVerification(false, beforeIdentity, latestIdentity, attempt, sw.ElapsedMilliseconds);
    }

    async Task<LiveSwitchVerification> TryArrowDownSwitchAsync(string source, int count, int waitAfterReloadMs, CancellationToken ct)
    {
        string beforeIdentity = await GetCurrentLiveIdentityAsync(ct);
        _log.Info($"[LIVE_VERIFY_BEFORE] source={source} identity={TrimIdentityForLog(beforeIdentity)}");
        var canVerifyIdentity = HasReliableLiveIdentity(beforeIdentity);

        for (int attempt = 1; attempt <= ArrowDownRetryAttempts; attempt++)
        {
            await _chrome.PressKeyAsync("ArrowDown", Math.Clamp(count, 1, 4), MultiActionGapMs, ct);
            _log.Info($"[LIVE_KEY_SENT] source={source} attempt={attempt}/{ArrowDownRetryAttempts} key=ArrowDown count={Math.Clamp(count, 1, 4)}");

            // Keep the stable V11.5 sequence.  Checking XPath/identity before this
            // reload races TikTok's virtualized live player and was the source of
            // false LIVE_SWITCH_FAILED loops in V12.5.
            await Task.Delay(ArrowDownSettleBeforeReloadMs, ct);
            _log.Info($"[LIVE_SWITCH_SETTLED] source={source} attempt={attempt}/{ArrowDownRetryAttempts} waitMs={ArrowDownSettleBeforeReloadMs}");
            await _chrome.ReloadAndWaitAsync(Math.Max(0, waitAfterReloadMs), 15000, ct);
            ClearScanXPathCooldown($"ArrowDown -> Reload DOM ready: {source}");
            _log.Info($"[LIVE_SWITCH_DOM_READY] source={source} attempt={attempt}/{ArrowDownRetryAttempts}");

            var afterIdentity = await GetCurrentLiveIdentityAsync(ct);
            if (!canVerifyIdentity || !HasReliableLiveIdentity(afterIdentity))
            {
                // Some TikTok layouts expose no stable room id.  A successful
                // ArrowDown + reload + DOM-ready sequence is usable; the following
                // XPath scan is the authoritative readiness check in that layout.
                _log.Warn($"[LIVE_SWITCH_UNVERIFIED] source={source} attempt={attempt}/{ArrowDownRetryAttempts} before={TrimIdentityForLog(beforeIdentity)} after={TrimIdentityForLog(afterIdentity)}");
                return new LiveSwitchVerification(true, beforeIdentity, afterIdentity, attempt, ArrowDownSettleBeforeReloadMs);
            }

            if (!string.Equals(afterIdentity, beforeIdentity, StringComparison.Ordinal))
            {
                _log.Info($"[LIVE_SWITCH_CONFIRMED] source={source} attempt={attempt}/{ArrowDownRetryAttempts} before={TrimIdentityForLog(beforeIdentity)} after={TrimIdentityForLog(afterIdentity)}");
                return new LiveSwitchVerification(true, beforeIdentity, afterIdentity, attempt, ArrowDownSettleBeforeReloadMs);
            }

            _log.Warn($"[LIVE_SWITCH_NOT_CHANGED] source={source} attempt={attempt}/{ArrowDownRetryAttempts} before={TrimIdentityForLog(beforeIdentity)} after={TrimIdentityForLog(afterIdentity)}");
            if (attempt < ArrowDownRetryAttempts)
            {
                _log.Warn($"[LIVE_SWITCH_RETRY] source={source} nextAttempt={attempt + 1}/{ArrowDownRetryAttempts} waitMs={ArrowDownRetryDelayMs}");
                await Task.Delay(ArrowDownRetryDelayMs, ct);
            }
        }

        return new LiveSwitchVerification(false, beforeIdentity, beforeIdentity, ArrowDownRetryAttempts, LiveVerifyTimeoutMs);
    }

    async Task<LiveSwitchVerification> TryClickSwitchAsync(string source, string xpath, int count, CancellationToken ct)
    {
        string beforeIdentity = await GetCurrentLiveIdentityAsync(ct);
        _log.Info($"[LIVE_VERIFY_BEFORE] source={source} identity={TrimIdentityForLog(beforeIdentity)}");

        if (!await ClickLiveSwitchAsync(source, xpath, Math.Clamp(count, 1, 4), ct))
            return new LiveSwitchVerification(false, beforeIdentity, beforeIdentity, 1, 0);

        _log.Info($"[LIVE_KEY_SENT] source={source} attempt=1/1 action=ClickXPath count={Math.Clamp(count, 1, 4)}");
        var verify = await WaitForLiveChangedAsync(source, beforeIdentity, 1, 1, ct);
        if (verify.Changed)
        {
            _log.Info($"[LIVE_SWITCH_CONFIRMED] source={source} attempt=1/1 before={TrimIdentityForLog(verify.BeforeIdentity)} after={TrimIdentityForLog(verify.AfterIdentity)} elapsed={verify.ElapsedMs}ms");
            return verify;
        }

        _log.Warn($"[LIVE_SWITCH_NOT_CHANGED] source={source} attempt=1/1 before={TrimIdentityForLog(beforeIdentity)} after={TrimIdentityForLog(verify.AfterIdentity)} elapsed={verify.ElapsedMs}ms");
        return verify;
    }

    async Task<bool> ClickLiveSwitchAsync(string source, string xpath, int count, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            ReportProblem("XPATH_ACTION_MISSING", source, "XPath nút chuyển live đang trống. Bỏ qua vòng chuyển live; không dùng tọa độ fallback.", error: true, throttleSeconds: 30);
            return false;
        }

        if (await _chrome.XPathExistsAsync(xpath, ct))
        {
            try
            {
                await _chrome.ClickXPathDomSmartAsync(xpath, count, MultiActionGapMs, ct);
                return true;
            }
            catch (Exception ex)
            {
                ReportProblem("LIVE_SWITCH_CLICK_FAILED", source, $"Đã tìm thấy XPath nút chuyển live nhưng click clickable ancestor thất bại. XPath={xpath}; chi tiết: {ex.Message}", error: true, throttleSeconds: 15);
                return false;
            }
        }

        if (!_s.SwitchNeedsHover)
        {
            ReportProblem("LIVE_SWITCH_NOT_FOUND", source, $"Không tìm thấy XPath nút chuyển live: {xpath}. Chế độ hover đang tắt nên bỏ qua vòng chuyển live.", error: true, throttleSeconds: 15);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_s.XPathHoverArea))
        {
            ReportProblem("HOVER_TARGET_MISSING", source, "Đã bật nút cần hover nhưng XPath vùng hover đang trống. Bỏ qua vòng chuyển live.", error: true, throttleSeconds: 30);
            return false;
        }
        if (!await _chrome.XPathExistsAsync(_s.XPathHoverArea, ct))
        {
            ReportProblem("HOVER_TARGET_NOT_FOUND", source, $"Không tìm thấy XPath vùng hover LIVE: {_s.XPathHoverArea}. Bỏ qua vòng chuyển live.", error: true, throttleSeconds: 15);
            return false;
        }

        int beforeControls = 0;
        try { beforeControls = await _chrome.CountVisibleInteractiveOverXPathAsync(_s.XPathHoverArea, ct); } catch { }

        try
        {
            SetStatus("ĐANG HIỆN NÚT LIVE", "Hover ảo ngoài → giữa LIVE → dịch nhẹ, chờ control TikTok xuất hiện.");
            await _chrome.HoverXPathAsync(_s.XPathHoverArea, ct);
            await Task.Delay(Math.Clamp(_s.HoverDelayMs, 0, 3000), ct);
        }
        catch (Exception ex)
        {
            ReportProblem("HOVER_TARGET_FAILED", source, $"Tìm thấy vùng hover nhưng không kích hoạt được hover ảo. XPath={_s.XPathHoverArea}; chi tiết: {ex.Message}", error: true, throttleSeconds: 15);
            return false;
        }

        var deadline = Environment.TickCount64 + 2500;
        bool found = false;
        do
        {
            if (await _chrome.XPathExistsAsync(xpath, ct)) { found = true; break; }
            await Task.Delay(100, ct);
        } while (Environment.TickCount64 < deadline);

        if (!found)
        {
            int afterControls = beforeControls;
            try { afterControls = await _chrome.CountVisibleInteractiveOverXPathAsync(_s.XPathHoverArea, ct); } catch { }
            if (afterControls <= beforeControls)
            {
                ReportProblem("HOVER_CONTROL_NOT_SHOWN", source, $"Đã hover ảo vào vùng LIVE nhưng không thấy control tương tác mới xuất hiện sau 2.5 giây. XPath hover={_s.XPathHoverArea}. Hãy dùng nút ‘Thử hover’ để kiểm tra vùng hover.", error: true, throttleSeconds: 15);
            }
            else
            {
                ReportProblem("LIVE_SWITCH_NOT_FOUND", source, $"Control LIVE đã thay đổi sau hover nhưng vẫn không tìm thấy XPath nút chuyển live: {xpath}. Hãy lấy lại XPath nút; picker V11.5 sẽ tự chọn clickable ancestor thay vì SVG.", error: true, throttleSeconds: 15);
            }
            return false;
        }

        try
        {
            await _chrome.ClickXPathDomSmartAsync(xpath, count, MultiActionGapMs, ct);
            return true;
        }
        catch (Exception ex)
        {
            ReportProblem("LIVE_SWITCH_CLICK_FAILED", source, $"Nút chuyển live đã xuất hiện nhưng click clickable ancestor thất bại. XPath={xpath}; chi tiết: {ex.Message}", error: true, throttleSeconds: 15);
            return false;
        }
    }

    async Task<bool> TransitionAsync(string source, TransitionAction action, string xpath, int count, bool scheduledPeriodic,
        CancellationToken ct, int waitAfterReloadMs = F5WaitMs)
    {
        if (_transitioning)
        {
            ReportProblem("TRANSITION_LOCKED", source, "Đang có một vòng chuyển live/recovery khác.", throttleSeconds: 5);
            return false;
        }
        _transitioning = true;
        bool completed = false;
        SetStatus("ĐANG CHUYỂN LIVE", source);
        _log.Info($"BẮT ĐẦU KHÓA CHUYỂN LIVE: {source}");
        try
        {
            LiveSwitchVerification verify = await ExecuteTransitionAttemptAsync(source, action, xpath, count, waitAfterReloadMs, ct);
            if (!verify.Changed)
            {
                ReportProblem("LIVE_SWITCH_FAILED", source, "Đã retry chuyển LIVE nhưng chưa xác nhận LIVE mới; đây là lỗi recoverable, sẽ bỏ qua/retry ở vòng kế tiếp.", throttleSeconds: 10);
                return false;
            }

            ResetPeriodicDue(source + " da xac nhan sang LIVE moi va F5 xong", cancelCandidate: !scheduledPeriodic);
            completed = true;
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var cdpSessionLost = IsLikelyCdpIssue(ex);
            ReportProblem("TRANSITION_FAILED", source, ex.Message, error: cdpSessionLost, throttleSeconds: 10);
            if (cdpSessionLost && !await EnsureCdpRecoveredAsync($"transition/{source}", ct)) return false;
            if (!cdpSessionLost) await Task.Delay(ArrowDownRetryDelayMs, ct);

            try
            {
                if (cdpSessionLost) await _chrome.BringToFrontAsync(ct);
                LiveSwitchVerification verify = await ExecuteTransitionAttemptAsync(source + " retry", action, xpath, count, waitAfterReloadMs, ct);
                if (!verify.Changed)
                {
                    ReportProblem("LIVE_SWITCH_FAILED", source, "Đã retry chuyển LIVE nhưng chưa xác nhận LIVE mới; sẽ tiếp tục cơ chế bỏ qua LIVE lỗi.", throttleSeconds: 10);
                    return false;
                }

                _log.Warn($"[RECOVERY_OK] transition={source} action=retry-after-cdp-reconnect");
                ResetPeriodicDue(source + " da xac nhan sang LIVE moi sau retry reconnect", cancelCandidate: !scheduledPeriodic);
                completed = true;
                return true;
            }
            catch (Exception retryEx)
            {
                ReportProblem("TRANSITION_RETRY_FAILED", source, retryEx.Message, error: true, throttleSeconds: 10);
                return false;
            }
        }
        finally
        {
            _transitioning = false;
            _log.Info($"MỞ KHÓA CHUYỂN LIVE: {source}");
            // Nếu vòng chuyển thất bại vì XPath, giữ nguyên trạng thái LỖI/CẢNH BÁO
            // do ReportProblem vừa hiển thị thay vì ghi đè bằng “hoàn tất”.
            if (completed) SetStatus("ĐANG CHẠY", source + " hoàn tất.");
        }
    }

    async Task<LiveSwitchVerification> ExecuteTransitionAttemptAsync(string source, TransitionAction action, string xpath, int count,
        int waitAfterReloadMs, CancellationToken ct)
    {
        if (action == TransitionAction.ArrowDown)
            return await TryArrowDownSwitchAsync(source, count, waitAfterReloadMs, ct);

        var verify = await TryClickSwitchAsync(source, xpath, count, ct);
        if (!verify.Changed) return verify;

        await _chrome.ReloadAndWaitAsync(Math.Max(0, waitAfterReloadMs), 15000, ct);
        ClearScanXPathCooldown($"ClickXPath -> Reload DOM ready: {source}");
        _log.Info($"[LIVE_SWITCH_DOM_READY] source={source} action=ClickXPath");
        return verify;
    }

    async Task<ImageMatcher.PreparedBitmap> CaptureScanRegionAsync(ScanRegion region, CancellationToken ct, ScanCaptureCache? cache = null)
    {
        Bitmap? ownedBitmap = null;
        var bmp = cache?.ViewportBitmap;
        if (bmp is null)
        {
            var bytes = cache?.ViewportBytes;
            if (bytes is null)
            {
                bytes = await _chrome.CaptureViewportAsync(ct);
                if (cache is not null) cache.ViewportBytes = bytes;
            }

            _log.Info("[PERF_SCAN] Decode START");
            var decode = System.Diagnostics.Stopwatch.StartNew();
            var decoded = ImageMatcher.FromBytes(bytes);
            decode.Stop();
            _log.Info($"[PERF_SCAN] Decode DONE elapsed={decode.ElapsedMilliseconds}ms");

            if (cache is not null)
            {
                cache.ViewportBitmap = decoded;
                bmp = decoded;
            }
            else
            {
                ownedBitmap = decoded;
                bmp = decoded;
            }
        }

        try
        {
            _log.Info("[PERF_SCAN] ResolveROI START");
            var resolve = System.Diagnostics.Stopwatch.StartNew();
            var rect = await ResolveScanRectangleAsync(bmp, region.ScanXPath, region.RX1, region.RY1, region.RX2, region.RY2, ct, cache);
            resolve.Stop();
            _log.Info($"[PERF_SCAN] ResolveROI DONE elapsed={resolve.ElapsedMilliseconds}ms");

            _log.Info("[PERF_SCAN] Crop START");
            var crop = System.Diagnostics.Stopwatch.StartNew();
            using var roi = bmp.Clone(rect, bmp.PixelFormat);
            var prepared = new ImageMatcher.PreparedBitmap(roi);
            crop.Stop();
            _log.Info($"[PERF_SCAN] Crop DONE elapsed={crop.ElapsedMilliseconds}ms");
            return prepared;
        }
        finally
        {
            ownedBitmap?.Dispose();
        }
    }

    async Task<Rectangle> ResolveScanRectangleAsync(Bitmap bmp, string xpath, double rx1, double ry1, double rx2, double ry2, CancellationToken ct, ScanCaptureCache? cache = null)
    {
        Rectangle resolved;
        if (!string.IsNullOrWhiteSpace(xpath))
        {
            var box = await _chrome.GetBoxNoScrollAsync(xpath, ct)
                ?? throw new InvalidOperationException("Không tìm thấy XPath: " + xpath);
            var viewport = cache?.ViewportSize ?? await _chrome.GetViewportSizeAsync(ct);
            if (cache is not null) cache.ViewportSize = viewport;
            var (vw, vh) = viewport;
            if (vw <= 0 || vh <= 0) throw new InvalidOperationException("Không đọc được kích thước viewport Chrome.");
            var sx = bmp.Width / (double)vw;
            var sy = bmp.Height / (double)vh;
            var leftCss = Math.Max(0, box.X);
            var topCss = Math.Max(0, box.Y);
            var rightCss = Math.Min(vw, box.X + box.Width);
            var bottomCss = Math.Min(vh, box.Y + box.Height);
            if (rightCss - leftCss < 2 || bottomCss - topCss < 2)
                throw new InvalidOperationException("Element XPath đang ngoài viewport hoặc quá nhỏ; tool không tự cuộn để tránh Chrome bị giật.");
            resolved = Rectangle.FromLTRB(
                Math.Clamp((int)Math.Floor(leftCss * sx), 0, bmp.Width - 1),
                Math.Clamp((int)Math.Floor(topCss * sy), 0, bmp.Height - 1),
                Math.Clamp((int)Math.Ceiling(rightCss * sx), 1, bmp.Width),
                Math.Clamp((int)Math.Ceiling(bottomCss * sy), 1, bmp.Height));
            if (resolved.Width < 2 || resolved.Height < 2) throw new InvalidOperationException("Vùng XPath sau quy đổi screenshot quá nhỏ.");
            return resolved;
        }

        if (_s.StrictXPathOnly)
            throw new InvalidOperationException("Chế độ Chỉ XPath đang bật nên không dùng vùng tọa độ fallback.");

        rx1 = Math.Clamp(rx1, 0, 1);
        ry1 = Math.Clamp(ry1, 0, 1);
        rx2 = Math.Clamp(rx2, 0, 1);
        ry2 = Math.Clamp(ry2, 0, 1);
        var x1 = Math.Clamp((int)Math.Round(Math.Min(rx1, rx2) * bmp.Width), 0, Math.Max(0, bmp.Width - 1));
        var y1 = Math.Clamp((int)Math.Round(Math.Min(ry1, ry2) * bmp.Height), 0, Math.Max(0, bmp.Height - 1));
        var x2 = Math.Clamp((int)Math.Round(Math.Max(rx1, rx2) * bmp.Width), x1 + 1, bmp.Width);
        var y2 = Math.Clamp((int)Math.Round(Math.Max(ry1, ry2) * bmp.Height), y1 + 1, bmp.Height);
        resolved = Rectangle.FromLTRB(x1, y1, x2, y2);
        return resolved;
    }

    ImageMatcher.MultiScaleTemplate GetOrLoadTemplate(string path)
    {
        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        if (_templateCache.TryGetValue(path, out var cached) && cached.LastWriteUtc == lastWriteUtc)
            return cached.Template;

        cached?.Dispose();
        using var bmp = new Bitmap(path);
        var template = ImageMatcher.PrepareMultiScaleTemplate(bmp);
        _templateCache[path] = new CachedTemplate
        {
            LastWriteUtc = lastWriteUtc,
            Template = template
        };
        return template;
    }

    async Task<Bitmap> CaptureRegionAsync(string xpath, double rx1, double ry1, double rx2, double ry2, CancellationToken ct, Dictionary<string, byte[]>? cache = null)
    {
        const string viewportKey = "__VIEWPORT__";
        byte[]? bytes = null;
        if (cache is not null) cache.TryGetValue(viewportKey, out bytes);
        if (bytes is null)
        {
            bytes = await _chrome.CaptureViewportAsync(ct);
            cache?.Add(viewportKey, bytes);
        }

        using var bmp = ImageMatcher.FromBytes(bytes);
        var rect = await ResolveScanRectangleAsync(bmp, xpath, rx1, ry1, rx2, ry2, ct);
        return bmp.Clone(rect, bmp.PixelFormat);
    }

    string ResolveImage(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(_baseDir, path.Replace('\\', Path.DirectorySeparatorChar)));
    }

    string TempImagePath(string name)
    {
        Directory.CreateDirectory(OldLiveDirectoryPath);
        return Path.Combine(OldLiveDirectoryPath, name);
    }
}

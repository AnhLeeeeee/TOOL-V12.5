using System.Text.Json;

namespace ToolTikTokV11;

public sealed partial class MainForm
{
    public Task<string> HandleManagedCommandAsync(string rawCommand)
    {
        var command = (rawCommand ?? "").Trim().ToLowerInvariant();
        if (command == "ping") return Task.FromResult("pong");
        if (IsDisposed || Disposing) return Task.FromResult("disposed");
        return InvokeManagedOnUiAsync(async () =>
        {
            switch (command)
            {
                case "status":
                    return JsonSerializer.Serialize(new
                    {
                        Profile = CurrentProfileName,
                        State = "WORKER_READY",
                        RunState = !_engine.Running ? "STOPPED" : _engine.Paused ? "PAUSED" : "RUNNING",
                        Detail = _runDetail.Text,
                        Chrome = _chrome.Connected ? "CONNECTED" : "DISCONNECTED",
                        CdpPort = _settings.ChromePort,
                        Pid = Environment.ProcessId,
                        WindowHandle = Handle.ToInt64()
                    });
                case "start":
                    await StartAsync();
                    return _engine.Running ? "started" : "not_started";
                case "pause":
                    if (_engine.Running && !_engine.Paused) _engine.TogglePause();
                    return _engine.Paused ? "paused" : "not_paused";
                case "resume":
                    if (_engine.Running && _engine.Paused) _engine.TogglePause();
                    return _engine.Running && !_engine.Paused ? "running" : "not_running";
                case "stop":
                    _engine.Stop();
                    return "stopped";
                case "launch":
                    await LaunchChromeAsync();
                    return _chrome.Connected ? "opened" : "not_opened";
                case "connect":
                    await ConnectChromeAsync();
                    return _chrome.Connected ? "connected" : "disconnected";
                case "close_chrome":
                    return await CloseChromeAsync();
                case "show":
                    if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                    Show();
                    return "shown";
                case "shutdown":
                    BeginInvoke(new Action(Close));
                    return "bye";
                default:
                    return "unknown";
            }
        });
    }

    Task<string> InvokeManagedOnUiAsync(Func<Task<string>> action)
    {
        if (!InvokeRequired) return action();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(new Action(async () =>
        {
            try { tcs.TrySetResult(await action()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }));
        return tcs.Task;
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ToolBox.Engine;

namespace ToolBox.Diagnostics;

/// <summary>
/// Opt-in smoke test: set TOOLBOX_SELFTEST=1 and the app calls every engine route once, writes the timings to
/// %TEMP%\toolbox-selftest.txt and exits. Used by build.ps1 -Test and by hand. Not part of normal use.
/// </summary>
public static class SelfTest
{
    public static bool Requested => Environment.GetEnvironmentVariable("TOOLBOX_SELFTEST") == "1";

    static readonly string[] Routes =
    {
        "/api/hw", "/api/hw/gpus", "/api/hw/cpu", "/api/hw/perf", "/api/hw/temps", "/api/disks", "/api/drives", "/api/proc/list", "/api/proc/ports",
        "/api/net/adapters", "/api/net/local", "/api/net/dns?name=example.com", "/api/net/ping?host=127.0.0.1", "/api/env", "/api/hosts"
    };

    public static async Task RunAndExitAsync()
    {
        var report = new StringBuilder();
        var failed = 0;
        foreach (var path in Routes)
        {
            var sw = Stopwatch.StartNew();
            try { var r = await Backend.GetAsync(path); report.AppendLine($"OK   {sw.ElapsedMilliseconds,5} ms  {path}  {r.GetRawText().Length} chars"); }
            catch (Exception e) { failed++; report.AppendLine($"FAIL {sw.ElapsedMilliseconds,5} ms  {path}  {e.Message}"); }
        }
        // folder scan end to end, on the folder the app runs from
        try
        {
            await Backend.GetAsync("/api/scan?path=" + Uri.EscapeDataString(AppContext.BaseDirectory));
            JsonElement s;
            do { await Task.Delay(100); s = await Backend.GetAsync("/api/status"); } while (s.Get("scanning").ValueKind == JsonValueKind.True);
            var node = await Backend.GetAsync("/api/node?id=0");
            report.AppendLine($"OK   scan   {s.Num("files")} files, {Json.Bytes(node.Get("node").Num("size"))}");
            await Backend.GetAsync("/api/reset");
        }
        catch (Exception e) { failed++; report.AppendLine("FAIL scan  " + e.Message); }
        report.AppendLine(failed == 0 ? "ALL OK" : $"{failed} FAILED");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "toolbox-selftest.txt"), report.ToString());
        Environment.Exit(failed == 0 ? 0 : 1);
    }
}

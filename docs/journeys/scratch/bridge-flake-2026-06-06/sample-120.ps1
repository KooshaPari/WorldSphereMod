# 120-poll sample of the bridge with parallel requests across 5 endpoints (Task.WhenAll via HttpClient).
# Per-poll: status, latency_ms, body_len, error — keyed by endpoint.
# Output: $Out JSON-lines, $Summary CSV with success rate + p50/p95/p99 per endpoint.

param(
    [string]$Out = "E:\Dev\WorldSphereMod\docs\journeys\scratch\bridge-flake-2026-06-06\sample.jsonl",
    [string]$Summary = "E:\Dev\WorldSphereMod\docs\journeys\scratch\bridge-flake-2026-06-06\summary.csv",
    [int]$PollCount = 120,
    [int]$IntervalMs = 500,
    [int]$TimeoutSec = 8
)

$ErrorActionPreference = "Stop"
$endpoints = @(
    "/health",
    "/telemetry",
    "/diag/render_stats",
    "/diag/emit_status",
    "/diag/water_samples"
)

New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
if (Test-Path $Out) { Remove-Item $Out }

$code = @"
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;

public class BridgePoll
{
    public static async Task<List<Dictionary<string, object>>> RunAsync(
        string outPath, int pollCount, int intervalMs, int timeoutSec, string[] endpoints)
    {
        var all = new List<Dictionary<string, object>>();
        var swAll = Stopwatch.StartNew();
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };
        var baseUrl = "http://127.0.0.1:8766";
        var swPoll = new Stopwatch();

        using (var writer = new StreamWriter(outPath, false, Encoding.UTF8))
        {
            for (int i = 0; i < pollCount; i++)
            {
                var tasks = new List<Task<Dictionary<string, object>>>();
                foreach (var ep in endpoints)
                {
                    var endpointCopy = ep;
                    tasks.Add(PollAsync(http, baseUrl, endpointCopy, timeoutSec, i, swPoll));
                }
                var results = await Task.WhenAll(tasks);
                foreach (var r in results)
                {
                    writer.WriteLine(ToJson(r));
                }
                await writer.FlushAsync();
                all.AddRange(results);
                await Task.Delay(intervalMs);
            }
        }
        swAll.Stop();
        Console.Error.WriteLine(string.Format("[sample] done polls={0} elapsed_s={1:0.0}", pollCount, swAll.Elapsed.TotalSeconds));
        return all;
    }

    static string ToJson(Dictionary<string, object> d)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        bool first = true;
        foreach (var kv in d)
        {
            if (!first) sb.Append(",");
            first = false;
            sb.Append("\"").Append(Escape(kv.Key)).Append("\":");
            AppendValue(sb, kv.Value);
        }
        sb.Append("}");
        return sb.ToString();
    }

    static void AppendValue(StringBuilder sb, object v)
    {
        if (v == null) { sb.Append("null"); return; }
        Type t = v.GetType();
        if (t == typeof(bool)) { sb.Append(((bool)v) ? "true" : "false"); return; }
        if (t == typeof(int) || t == typeof(long) || t == typeof(double) || t == typeof(float) || t == typeof(decimal)) { sb.Append(v.ToString()); return; }
        sb.Append("\"").Append(Escape(v.ToString())).Append("\"");
    }

    static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    static async Task<Dictionary<string, object>> PollAsync(HttpClient http, string baseUrl, string ep, int timeoutSec, int pollIdx, Stopwatch sw)
    {
        var dict = new Dictionary<string, object>
        {
            { "poll", pollIdx },
            { "ts", DateTime.UtcNow.ToString("o") },
            { "endpoint", ep },
            { "ok", false },
            { "status", 0 },
            { "latency_ms", 0.0 },
            { "body_len", 0 },
            { "err", "" }
        };
        sw.Restart();
        try
        {
            var resp = await http.GetAsync(baseUrl + ep);
            sw.Stop();
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            dict["ok"] = resp.IsSuccessStatusCode;
            dict["status"] = (int)resp.StatusCode;
            dict["latency_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            dict["body_len"] = bytes.LongLength;
        }
        catch (TaskCanceledException tex)
        {
            sw.Stop();
            dict["latency_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            dict["err"] = "TIMEOUT(" + timeoutSec + "s): " + (tex.InnerException != null ? tex.InnerException.Message : tex.Message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            dict["latency_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            var msg = ex.Message ?? "";
            if (msg.Length > 200) msg = msg.Substring(0, 200);
            dict["err"] = ex.GetType().Name + ": " + msg;
        }
        return dict;
    }
}
"@

Add-Type -TypeDefinition $code -ReferencedAssemblies "System.Net.Http", "System.Net.Http.WebRequest", "System.Diagnostics.Process", "System.IO" -IgnoreWarnings
$tasks = [BridgePoll]::RunAsync($Out, $PollCount, $IntervalMs, $TimeoutSec, $endpoints)
$results = $tasks.GetAwaiter().GetResult()

# Build per-endpoint summary
$perEp = @{}
foreach ($r in $results) {
    $ep = $r["endpoint"]
    if (-not $perEp.ContainsKey($ep)) { $perEp[$ep] = New-Object System.Collections.Generic.List[object] }
    $perEp[$ep].Add($r)
}

$rows = @()
foreach ($ep in $endpoints) {
    $rs = $perEp[$ep]
    $total = $rs.Count
    $ok = ($rs | Where-Object { $_[ "ok" ] }).Count
    $err = $total - $ok
    $lats = $rs | ForEach-Object { [double]$_["latency_ms"] } | Sort-Object
    $p50 = if ($lats.Count -gt 0) { $lats[[int]([Math]::Floor($lats.Count * 0.5))] } else { 0 }
    $p95 = if ($lats.Count -gt 0) { $lats[[int]([Math]::Floor($lats.Count * 0.95))] } else { 0 }
    $p99 = if ($lats.Count -gt 0) { $lats[[int]([Math]::Floor($lats.Count * 0.99))] } else { 0 }
    $max = if ($lats.Count -gt 0) { $lats[-1] } else { 0 }
    $min = if ($lats.Count -gt 0) { $lats[0] } else { 0 }
    $rows += [pscustomobject]@{
        endpoint = $ep
        polls = $total
        ok = $ok
        err = $err
        success_rate = [math]::Round(($ok / [Math]::Max(1, $total)), 4)
        p50_ms = [math]::Round($p50, 1)
        p95_ms = [math]::Round($p95, 1)
        p99_ms = [math]::Round($p99, 1)
        min_ms = [math]::Round($min, 1)
        max_ms = [math]::Round($max, 1)
        avg_ms = [math]::Round((($lats | Measure-Object -Average).Average), 1)
    }
}

$rows | Export-Csv -Path $Summary -NoTypeInformation
$rows | Format-Table -AutoSize | Out-String | Write-Host

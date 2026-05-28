# OmniRoute on kooshas-laptop (macOS)

Desk PlayCUA / `do-all.ps1 -Vision` needs **OpenAI-compatible chat** on the laptop over Tailscale. A working **`/models`** list from the desk does **not** prove vision works — **`/chat/completions`** must succeed too (502/timeout on chat while Cursor works on `localhost:20128` usually means OmniRoute is bound to loopback only).

Tailscale machine: **kooshas-laptop** (typical tailnet IP **`100.112.14.98`**). Desk env: `Tools/omniroute-vision.env` with `OMNROUTE_BASE_URL=http://100.112.14.98:20128/v1`. Do not use stale funnel hostnames (e.g. `omniroute-a6e82363`).

## 1. Confirm OmniRoute on loopback

On the laptop (Terminal):

```bash
# OmniRoute UI / gateway should be listening locally
curl -sS -o /dev/null -w "%{http_code}\n" http://127.0.0.1:20128/
curl -sS http://127.0.0.1:20128/v1/models -H "Authorization: Bearer $OMNROUTE_API_KEY" | head -c 200
```

Expect HTTP **200** and a JSON `data` array. If OmniRoute is not running, start it from the OmniRoute app or your usual launch method (default gateway port **20128**).

Optional: copy repo `Tools/omniroute-vision.env.example` → `Tools/omniroute-vision.env` on the **desk** repo and set `OMNROUTE_API_KEY` from **Dashboard → Endpoints** in the OmniRoute UI.

## 2. Expose the API on the tailnet

Pick **one** approach.

### A. Tailscale Serve (recommended)

OmniRoute stays on **`127.0.0.1:20128`**; Tailscale proxies it to peers:

```bash
tailscale status   # kooshas-laptop should be online
tailscale serve --bg http://127.0.0.1:20128
tailscale serve status
```

Peers can use the laptop’s Tailscale IP on port **20128** (same as direct bind when Serve forwards that port). After Serve is up, re-check from the laptop:

```bash
curl -sS http://127.0.0.1:20128/v1/models -H "Authorization: Bearer $OMNROUTE_API_KEY" | head -c 200
```

To remove later: `tailscale serve reset`.

### B. Bind OmniRoute to the tailnet interface

If OmniRoute supports listening on **`0.0.0.0:20128`** (or the Tailscale interface IP), configure that in OmniRoute settings/docs so **`100.112.14.98:20128`** reaches the process without Serve. Ensure macOS firewall allows inbound **20128** on the Tailscale interface.

## 3. Verify from the desk (PowerShell)

From the WorldSphereMod repo on the **desk** (not the laptop):

```powershell
# One-shot (loads Tools/omniroute-vision.env, exits 0 only if /models and /chat/completions OK)
pwsh Tools/verify-omniroute-remote.ps1
```

Manual probes (same checks as `do-all.ps1`):

```powershell
Get-Content Tools/omniroute-vision.env | ForEach-Object {
  if ($_ -match '^\s*([^#][^=]+)=(.*)$') { Set-Item "env:$($matches[1].Trim())" $matches[2].Trim() }
}
$base = $env:OMNROUTE_BASE_URL.TrimEnd('/')   # e.g. http://100.112.14.98:20128/v1
$h = @{ Authorization = "Bearer $env:OMNROUTE_API_KEY" }

# /models
$models = Invoke-RestMethod -Uri "$base/models" -Headers $h -TimeoutSec 30
"models: $(@($models.data).Count)"

# Text chat (required for vision routing)
$modelId = if ($env:OMNROUTE_VISION_MODEL) { $env:OMNROUTE_VISION_MODEL } else { 'gemini/gemini-2.5-flash' }
$body = @{
  model = $modelId; max_tokens = 24; temperature = 0
  messages = @(@{ role = 'user'; content = 'Reply with exactly: vision-ok' })
} | ConvertTo-Json -Depth 5
$chat = Invoke-RestMethod -Uri "$base/chat/completions" -Method Post -Headers ($h + @{ 'Content-Type' = 'application/json' }) -Body $body -TimeoutSec 25
"chat: $($chat.choices[0].message.content)"
```

Tiny **vision** probe (1×1 PNG, same path PlayCUA uses):

```powershell
$pngB64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
$visionBody = @{
  model = $modelId; max_tokens = 64
  messages = @(@{
    role = 'user'
    content = @(
      @{ type = 'text'; text = 'What color is this image? One word only.' }
      @{ type = 'image_url'; image_url = @{ url = "data:image/png;base64,$pngB64" } }
    )
  })
} | ConvertTo-Json -Depth 8
Invoke-RestMethod -Uri "$base/chat/completions" -Method Post -Headers ($h + @{ 'Content-Type' = 'application/json' }) -Body $visionBody -TimeoutSec 60 |
  Select-Object -ExpandProperty choices | Select-Object -First 1 -ExpandProperty message | Select-Object -ExpandProperty content
```

If **`/models` OK** but **`/chat/completions` 502 or timeout**, fix laptop exposure (step 2) before running `pwsh Tools/do-all.ps1 -Vision`.

## 4. Desk env checklist

| Variable | Desk value |
|---|---|
| `OMNROUTE_BASE_URL` | `http://100.112.14.98:20128/v1` (or Serve URL if different) |
| `OMNROUTE_API_KEY` | From OmniRoute dashboard |
| `OMNROUTE_VISION_MODEL` or `OMNROUTE_VISION_COMBO` | Vision-capable model or combo name |

Then: `pwsh Tools/do-all.ps1 -Vision` — `Tools/.reports/do-all-latest.json` should show `omniroute-probe` **passed**, not `visionDegraded: true`.

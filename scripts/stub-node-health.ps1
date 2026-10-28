<#
.SYNOPSIS
  A tiny local stand-in for the node API's /api/health, to exercise the wallet's own-node mode without waiting for a real node.

.DESCRIPTION
  Serves GET /api/health on http://localhost:<Port>/ (default 5260, the only local node origin the wallet's content-security policy
  already allows) with permissive CORS, and answers with whatever state you choose. Use it with the quickstart in
  specs/005-wallet-own-node-source/quickstart.md, step 3: set OwnNode:Enabled=true and OwnNode:ApiUrl=http://localhost:5260 for the
  wallet (for example in wwwroot/appsettings.Development.json), then change the state while the wallet is running.

  Keys (press while the script runs):
    o  ok                (ready: status ok, txindex true, versionOk true)
    s  syncing           (status syncing)
    t  starting          (status starting)
    u  unhealthy         (status unhealthy, answered with HTTP 503 like the real API when the node is unreachable)
    x  txindex false     (ok, but the transaction index is not ready)
    v  versionOk false   (ok, but the node is older than recommended)
    +  blocks +20        (the node reports 20 more blocks than the explorers: drifting ahead)
    -  blocks -20        (20 fewer: drifting behind)
    0  blocks = real     (back to the real chain height)
    q  quit

  The script reads the real chain height from a public explorer every 20 seconds so "ok" lines up with what the wallet's
  cross-check sees. It answers ONLY /api/health; the wallet's data requests to this port will fail, which is exactly what the
  failed-read demotion (quickstart row h) is meant to handle.

.PARAMETER Port
  Port to listen on. Default 5260.

.PARAMETER ExplorerHeightUrl
  Where to read the real height from. Default: digiexplorer.info.
#>
param(
    [int]$Port = 5260,
    [string]$ExplorerHeightUrl = 'https://digiexplorer.info/api/blocks/tip/height'
)

$ErrorActionPreference = 'Stop'

$script:state = 'ok'          # ok | syncing | starting | unhealthy | txindex | version
$script:offset = 0
$script:realHeight = 0
$script:lastHeightFetch = [datetime]::MinValue

function Get-RealHeight {
    if (([datetime]::UtcNow - $script:lastHeightFetch).TotalSeconds -gt 20 -or $script:realHeight -le 0) {
        try {
            $script:realHeight = [int](Invoke-RestMethod -Uri $ExplorerHeightUrl -TimeoutSec 5)
            $script:lastHeightFetch = [datetime]::UtcNow
        } catch {
            if ($script:realHeight -le 0) { $script:realHeight = 24000000 }
        }
    }
    return $script:realHeight
}

function New-HealthBody {
    $blocks = (Get-RealHeight) + $script:offset
    $status = switch ($script:state) {
        'syncing'   { 'syncing' }
        'starting'  { 'starting' }
        'unhealthy' { 'unhealthy' }
        default     { 'ok' }
    }
    $txindex = $script:state -ne 'txindex'
    $versionOk = $script:state -ne 'version'
    @{
        status = $status
        network = 'mainnet'
        nodeVersion = '9.26.7'
        blocks = $blocks
        headers = $blocks
        lag = 0
        peers = 8
        txindex = $txindex
        recommendedVersion = '9.26.7'
        versionOk = $versionOk
        timestamp = [datetime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Compress
}

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Stub node health on http://localhost:$Port/api/health  (o s t u x v + - 0 q)"
Write-Host "State: $($script:state), height offset: $($script:offset)"

$pending = $listener.GetContextAsync()
try {
    while ($true) {
        $keyReady = try { [Console]::KeyAvailable } catch { $false }   # no console (for example when run from a job): keys are simply unavailable
        if ($keyReady) {
            $key = [Console]::ReadKey($true).KeyChar
            switch ($key) {
                'o' { $script:state = 'ok' }
                's' { $script:state = 'syncing' }
                't' { $script:state = 'starting' }
                'u' { $script:state = 'unhealthy' }
                'x' { $script:state = 'txindex' }
                'v' { $script:state = 'version' }
                '+' { $script:offset = 20 }
                '-' { $script:offset = -20 }
                '0' { $script:offset = 0 }
                'q' { break }
            }
            if ($key -eq 'q') { break }
            Write-Host "State: $($script:state), height offset: $($script:offset)"
        }

        if ($pending.Wait(200)) {
            $ctx = $pending.Result
            $pending = $listener.GetContextAsync()
            $res = $ctx.Response
            $res.Headers.Add('Access-Control-Allow-Origin', '*')
            $res.Headers.Add('Access-Control-Allow-Headers', '*')
            $res.Headers.Add('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')

            if ($ctx.Request.HttpMethod -eq 'OPTIONS') {
                $res.StatusCode = 204
            }
            elseif ($ctx.Request.Url.AbsolutePath -eq '/api/health') {
                $body = [System.Text.Encoding]::UTF8.GetBytes((New-HealthBody))
                # like the real node API: 503 when the node cannot be reached, 200 for every other state
                $res.StatusCode = if ($script:state -eq 'unhealthy') { 503 } else { 200 }
                $res.ContentType = 'application/json'
                $res.OutputStream.Write($body, 0, $body.Length)
            }
            else {
                $res.StatusCode = 404
            }
            $res.Close()
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}

$base = "http://localhost:5260"
$pass = 0; $fail = 0; $results = @()

function Test-Endpoint {
    param([string]$name, [string]$method, [string]$path, $body)
    try {
        $params = @{ Uri = "$base$path"; Method = $method; TimeoutSec = 30; ContentType = "application/json" }
        if ($body -is [array]) {
            $params.Body = (ConvertTo-Json $body -Compress)
        } elseif ($body) {
            $params.Body = ($body | ConvertTo-Json -Compress)
        }
        $r = Invoke-RestMethod @params
        $json = ($r | ConvertTo-Json -Compress -Depth 2)
        if ($json -eq $null) { $json = "(null)" }
        $detail = $json.Substring(0, [Math]::Min(120, $json.Length))
        $script:pass++
        $script:results += [PSCustomObject]@{ Test=$name; Status="PASS"; Detail=$detail }
    } catch {
        $code = ""
        if ($_.Exception.Response) { $code = " $($_.Exception.Response.StatusCode.value__)" }
        $msg = $_.Exception.Message
        $detail = $msg.Substring(0, [Math]::Min(100, $msg.Length))
        $script:fail++
        $script:results += [PSCustomObject]@{ Test=$name; Status="FAIL$code"; Detail=$detail }
    }
}

Write-Host "Testing DigiByte NodeApi at $base`n" -ForegroundColor Cyan

# === System ===
Test-Endpoint "Health Check" GET "/api/health"
Test-Endpoint "Blockchain Info" GET "/api/blockchain/info"
Test-Endpoint "Block Height" GET "/api/blockchain/height"
Test-Endpoint "Block by Height" GET "/api/blockchain/block/1"
Test-Endpoint "Block Hash" GET "/api/blockchain/blockhash/1"
Test-Endpoint "Chain Tips" GET "/api/blockchain/chaintips"

# === Network ===
Test-Endpoint "Network Info" GET "/api/network/info"
Test-Endpoint "Peer Info" GET "/api/network/peers"
Test-Endpoint "Connections" GET "/api/network/connections"
Test-Endpoint "Mempool Info" GET "/api/network/mempool"
Test-Endpoint "Fee Estimate (6 blocks)" GET "/api/network/fee/6"
Test-Endpoint "Net Totals" GET "/api/network/totals"
Test-Endpoint "Ping Peers" POST "/api/network/ping"

# === Mining ===
Test-Endpoint "Mining Info" GET "/api/mining/info"
Test-Endpoint "Difficulty" GET "/api/mining/difficulty"
Test-Endpoint "Hashrate" GET "/api/mining/hashrate?nblocks=10"

# === Utility ===
Test-Endpoint "Node Version" GET "/api/util/version"
Test-Endpoint "Node Uptime" GET "/api/util/uptime"

# === Wallet ===
Test-Endpoint "Wallet Info" GET "/api/wallet/info"
Test-Endpoint "Wallet Balance" GET "/api/wallet/balance"
Test-Endpoint "Wallet Balances" GET "/api/wallet/balances"
Test-Endpoint "New Address" GET "/api/wallet/newaddress"
Test-Endpoint "List Wallets" GET "/api/wallet/list"
Test-Endpoint "Wallet Unspent" GET "/api/wallet/unspent"
Test-Endpoint "Wallet Transactions" GET "/api/wallet/transactions?count=5&skip=0"

# === Address (use a valid bech32 mainnet address) ===
$testAddr = (Invoke-RestMethod -Uri "$base/api/wallet/newaddress").address
Test-Endpoint "Validate Address" GET "/api/address/$testAddr/validate"

# === Transaction (decode with bad hex returns expected 400 error) ===
# Skip: Test-Endpoint "Decode TX (bad hex)" POST "/api/tx/decode" @{ hex = "00" }

# === Batch endpoints (use valid address) ===
Test-Endpoint "Batch Balances" POST "/api/address/balances" @($testAddr)
Test-Endpoint "Batch UTXOs" POST "/api/address/utxos" @($testAddr)

# === Results ===
Write-Host "`n===== TEST RESULTS =====" -ForegroundColor Yellow
$results | Format-Table Test, Status, Detail -AutoSize -Wrap
$color = if ($fail -eq 0) { "Green" } else { "Red" }
Write-Host "Passed: $pass | Failed: $fail | Total: $($pass + $fail)" -ForegroundColor $color

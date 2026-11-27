# Self-host tren Windows (Docker Desktop): kiem tra nhanh $Branch moi 2 phut, co commit moi thi build lai va chay.
# Chay trong thu muc chua docker-compose.yml, selfhost.yml, Dockerfile.ai, .env va src\ (ban clone nhanh do). Ctrl+C de dung.
param([string]$Branch = 'main', [int]$IntervalSec = 120)
Set-Location $PSScriptRoot
$compose = @('-f', 'docker-compose.yml', '-f', 'selfhost.yml')

function Show-TunnelUrl {
    $log = docker compose @compose logs tunnel 2>&1 | Out-String
    $m = [regex]::Matches($log, 'https://[a-z0-9-]+\.trycloudflare\.com')
    if ($m.Count -gt 0) { Write-Host "URL public: $($m[$m.Count - 1].Value)" -ForegroundColor Green }
}

while ($true) {
    git -C src fetch --quiet origin $Branch
    $new = (git -C src rev-parse "origin/$Branch" | Out-String).Trim()
    $old = if (Test-Path .deployed) { (Get-Content .deployed -Raw).Trim() } else { '' }

    if ($new -and $new -ne $old) {
        Write-Host "$(Get-Date -Format s) Deploy $Branch @ $($new.Substring(0, 7))" -ForegroundColor Cyan
        git -C src reset --quiet --hard $new
        # Ghi truoc khi build: build loi thi doi commit sau, khong build lai lien tuc.
        Set-Content .deployed $new
        if (Test-Path src\deploy\staging\selfhost.yml) {
            Copy-Item src\deploy\staging\docker-compose.yml, src\deploy\staging\selfhost.yml, src\deploy\staging\Dockerfile.ai, src\deploy\staging\Caddyfile . -Force
        }

        docker compose @compose build --pull api ai
        if ($LASTEXITCODE -eq 0) {
            docker compose @compose up -d --remove-orphans
            $ok = $false
            for ($i = 0; $i -lt 60 -and -not $ok; $i++) {
                try {
                    Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 http://127.0.0.1:8080/swagger/v1/swagger.json | Out-Null
                    $ok = $true
                } catch { Start-Sleep 5 }
            }
            if ($ok) { Write-Host "OK: API chay commit $($new.Substring(0, 7))" -ForegroundColor Green; Show-TunnelUrl }
            else { Write-Warning 'API khong tra loi sau 5 phut:'; docker compose @compose logs --tail 60 api }
        } else {
            Write-Warning "Build loi o commit $($new.Substring(0, 7)), giu ban dang chay."
        }
    }
    Start-Sleep $IntervalSec
}

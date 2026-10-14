# Xuất docs/api/openapi.json từ Swagger rồi liệt kê thay đổi so với bản đã commit (hoặc -BaseFile).
#   powershell -File scripts/export-openapi.ps1
#   powershell -File scripts/export-openapi.ps1 -BaseFile C:\tmp\openapi.old.json
# Công cụ xuất chạy mã khởi động của ứng dụng (không mở cổng), nên cần cấu hình như khi chạy Backend ở Development.
param([string]$BaseFile)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$spec = Join-Path $root 'docs\api\openapi.json'
$oasdiff = Join-Path $root 'agent-tools\oasdiff\oasdiff.exe'

Push-Location $root
try {
    dotnet tool restore | Out-Null
    dotnet build src/PanelForge.API -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build lỗi, không xuất OpenAPI.' }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Push-Location src/PanelForge.API   # cần đứng ở đây để ứng dụng tìm thấy appsettings
    try { dotnet swagger tofile --output $spec bin/Debug/net9.0/PanelForge.API.dll v1 | Out-Null }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Xuất OpenAPI lỗi.' }
    Write-Host "Đã ghi $spec"

    if (-not (Test-Path $oasdiff)) {
        Write-Host 'Chưa có oasdiff: tải oasdiff_*_windows_amd64.tar.gz từ https://github.com/oasdiff/oasdiff/releases, giải nén vào agent-tools\oasdiff\.'
        return
    }
    if (-not $BaseFile) {
        # cmd giữ nguyên byte (PowerShell 5.1 chuyển sang UTF-16 khi dùng >)
        $BaseFile = Join-Path $env:TEMP 'panelforge-openapi.base.json'
        cmd /c "git show HEAD:docs/api/openapi.json > `"$BaseFile`" 2>nul"
        if ($LASTEXITCODE -ne 0) { Write-Host 'Chưa có bản openapi.json nào được commit để so sánh.'; return }
    }
    Write-Host "`n== Thay đổi API so với $BaseFile =="
    & $oasdiff changelog $BaseFile $spec
}
finally { Pop-Location }

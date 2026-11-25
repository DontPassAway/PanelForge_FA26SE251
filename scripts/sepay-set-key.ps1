# Sinh API key webhook SePay, lưu vào user-secrets của BE và copy vào clipboard (không in ra màn hình).
$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$key = ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
dotnet user-secrets set "SePay:WebhookApiKey" $key --project (Join-Path $PSScriptRoot "..\src\PanelForge.API") | Out-Null
Set-Clipboard -Value $key
Write-Host "Da luu SePay:WebhookApiKey (48 ky tu) va copy vao clipboard. Dan vao o API Key tren SePay."

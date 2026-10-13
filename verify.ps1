<#
.SYNOPSIS
  统一验证脚本：后端、调用端、前端各自编译、格式检查、运行检查程序。
  全量运行时会临时启动后端服务，让调用端 SDK 对真实服务跑端到端检查。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1 -Part backend
#>
param(
    [ValidateSet('all', 'backend', 'client', 'frontend')]
    [string]$Part = 'all'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Invoke-Step([string]$Title, [scriptblock]$Command) {
    Write-Host ''
    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "失败：$Title（退出码 $LASTEXITCODE）"
    }
}

# 临时启动后端服务（开发环境配置、独立端口、临时数据目录），跑完调用端检查后关闭并删除数据目录。
function Invoke-ClientChecksWithServer {
    $port = 5289
    $url = "http://127.0.0.1:$port/"
    $bin = Join-Path $root 'backend/src/ApprovalService.Api/bin/Debug/net10.0'
    $log = Join-Path ([IO.Path]::GetTempPath()) "approvalservice-verify-$port.log"
    $data = Join-Path ([IO.Path]::GetTempPath()) ('approvalservice-verify-data-' + [Guid]::NewGuid().ToString('N'))
    $previous = $env:ASPNETCORE_ENVIRONMENT
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Write-Host ''
    Write-Host "==> 启动后端服务 $url（日志：$log）" -ForegroundColor Cyan
    $server = Start-Process -FilePath 'dotnet' -ArgumentList @('ApprovalService.Api.dll', '--urls', "http://127.0.0.1:$port", "--ApprovalService:Data:Directory=$data") `
        -WorkingDirectory $bin -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $env:ASPNETCORE_ENVIRONMENT = $previous
    try {
        $ready = $false
        for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
            Start-Sleep -Seconds 1
            try {
                $ready = (Invoke-WebRequest -UseBasicParsing -Uri "${url}health/ready" -TimeoutSec 2).StatusCode -eq 200
            }
            catch {
                $ready = $false
            }
        }

        if (-not $ready) {
            throw "后端服务 30 秒内没有就绪，见 $log"
        }

        $env:APPROVAL_SERVICE_URL = $url
        Invoke-Step '调用端：检查程序（含对真实服务的端到端检查）' { dotnet run --project client/tests/ApprovalService.Client.Checks --no-build }
    }
    finally {
        Remove-Item Env:APPROVAL_SERVICE_URL -ErrorAction SilentlyContinue
        if (-not $server.HasExited) {
            Stop-Process -Id $server.Id -Force
            $server.WaitForExit(10000) | Out-Null
        }

        Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Push-Location $root
try {
    if ($Part -in 'all', 'backend') {
        Invoke-Step '后端：编译' { dotnet build backend/ApprovalService.Backend.slnx }
        Invoke-Step '后端：格式检查' { dotnet format backend/ApprovalService.Backend.slnx --verify-no-changes --no-restore }
        Invoke-Step '后端：检查程序' { dotnet run --project backend/tests/ApprovalService.Backend.Checks --no-build }
    }

    if ($Part -in 'all', 'client') {
        Invoke-Step '调用端：编译' { dotnet build client/ApprovalService.Client.slnx }
        Invoke-Step '调用端：格式检查' { dotnet format client/ApprovalService.Client.slnx --verify-no-changes --no-restore }
        if ($Part -eq 'all') {
            Invoke-ClientChecksWithServer
        }
        else {
            Invoke-Step '调用端：检查程序' { dotnet run --project client/tests/ApprovalService.Client.Checks --no-build }
        }
    }

    if ($Part -in 'all', 'frontend') {
        Push-Location (Join-Path $root 'frontend')
        try {
            if (-not (Test-Path 'node_modules')) {
                Invoke-Step '前端：安装依赖' { npm ci }
            }

            Invoke-Step '前端：检查程序' { npm run check }
            Invoke-Step '前端：类型检查和打包' { npm run build }
        }
        finally {
            Pop-Location
        }
    }

    Write-Host ''
    Write-Host "全部通过（$Part）" -ForegroundColor Green
}
finally {
    Pop-Location
}

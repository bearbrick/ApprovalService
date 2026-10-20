<#
.SYNOPSIS
  为 SQLite、SQL Server、PostgreSQL 各生成同名迁移。
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\backend\tools\add-migration.ps1 -Name InitialRuntime
#>
param(
    [Parameter(Mandatory = $true)][string]$Name
)

$ErrorActionPreference = 'Stop'
$backend = Split-Path -Parent $PSScriptRoot
$project = Join-Path $backend 'src/ApprovalService.Infrastructure'

foreach ($provider in 'Sqlite', 'SqlServer', 'PostgreSql') {
    Write-Host "==> $provider：$Name" -ForegroundColor Cyan
    dotnet ef migrations add $Name --project $project --startup-project $project `
        --context "${provider}ApprovalDbContext" `
        --output-dir "Persistence/Migrations/$provider" `
        --namespace "ApprovalService.Infrastructure.Persistence.Migrations.$provider"
    if ($LASTEXITCODE -ne 0) {
        throw "$provider 的迁移生成失败"
    }
}

Write-Host '三种数据库迁移已生成；请运行 dotnet format 和后端检查并核对模型快照。' -ForegroundColor Green

<#
.SYNOPSIS
  根据 ApprovalService:Files 环境配置，将本地附件迁移到 MinIO。
.DESCRIPTION
  先把 ApprovalService__Files__Provider 设置为 Minio，并提供 Endpoint、Bucket、AccessKey、SecretKey。
  命令按数据库元数据逐个校验、复制和切换；中断后重新运行会继续剩余文件。
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
dotnet run --project (Join-Path $root 'backend/src/ApprovalService.Api') -- --migrate-files
if ($LASTEXITCODE -ne 0) {
    throw '附件迁移失败，请检查日志和配置后重试。'
}

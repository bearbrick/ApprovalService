# 审批服务部署与恢复

审批服务可以作为 Linux Docker 容器或 Windows 服务运行。SQLite 初版只允许一个服务进程使用同一数据目录；数据目录应在本地磁盘，不能放网络共享盘。对外访问由反向代理提供 HTTPS，业务系统仍通过 `/api/v1` 调用。以下命令从仓库根目录执行，密码和密钥只放在部署环境中。

## Docker

开发机需要 Docker；本仓库的构建同时编译锁定版本的 Vue 前端和 .NET 10 后端，不需要提交 `dist/`：

```powershell
docker build -f backend/deploy/Dockerfile -t approval-service:local .
docker volume create approval-data
```

在仓库外准备仅部署人员可读的 `approval.env`，至少写入 `ApprovalService__Integration__MasterKey=<32 字节密钥的 Base64>`。根据业务需要添加对象存储、邮件等环境变量；不要提交这个文件。首次开通管理员前先启动一次性命令，然后运行单实例服务：

```powershell
docker run --rm -it --env-file .\approval.env -v approval-data:/data approval-service:local bootstrap-admin
docker run -d --name approval-service --restart unless-stopped --env-file .\approval.env -p 127.0.0.1:5280:8080 -v approval-data:/data approval-service:local
curl.exe http://127.0.0.1:5280/health/ready
```

反向代理只向受信任业务系统转发审批页面和 API。`/metrics` 默认仅允许容器本机访问；外部 Prometheus 采集需在环境中配置至少 32 字符的 `ApprovalService__Metrics__BearerToken`，采集请求带 `Authorization: Bearer <密钥>`，代理须限制来源并提供 TLS。指标仅按 HTTP 方法、服务端路由模板和状态码聚合，不输出业务单号、人员、正文或原始 URL。

## Windows 服务

安装 .NET 10 ASP.NET Core Runtime，以管理员身份把后端和前端发布到例如 `C:\ApprovalService`：

```powershell
dotnet publish backend/src/ApprovalService.Api/ApprovalService.Api.csproj -c Release -r win-x64 --self-contained false -o C:\ApprovalService
Push-Location frontend
npm ci
npm run build
Pop-Location
New-Item C:\ApprovalService\wwwroot -ItemType Directory -Force | Out-Null
Copy-Item frontend\dist\* C:\ApprovalService\wwwroot -Recurse -Force
```

在发布目录放只属于服务账户的 `appsettings.Production.json`，设置本地绝对路径 `ApprovalService:Data:Directory`（例如 `D:\ApprovalData`）；接入应用加密主密钥通过服务账户的受保护环境变量 `ApprovalService__Integration__MasterKey` 注入。先在控制台执行 `dotnet C:\ApprovalService\ApprovalService.Api.dll bootstrap-admin` 创建第一位组织管理员，然后安装服务：

```powershell
New-Service -Name ApprovalService -DisplayName 'ApprovalService' -StartupType Automatic -BinaryPathName '"C:\Program Files\dotnet\dotnet.exe" "C:\ApprovalService\ApprovalService.Api.dll" --urls http://127.0.0.1:5280'
Start-Service ApprovalService
curl.exe http://127.0.0.1:5280/health/ready
```

生产部署应改用专门的低权限服务账户，并赋予发布目录只读、数据目录读写权限。服务更新时先停止，再替换发布文件；SQLite 发现待执行迁移会先生成 `preupgrade` 快照，失败时拒绝迁移和启动。Windows 服务安装需要目标机管理员权限，本机开发环境未实际安装。

## SQLite 备份与恢复

默认按服务器本地时间 02:30 每日在线备份，配置键 `ApprovalService:Backup:Time` 格式为 `HH:mm`，`Enabled` 可关闭，`Keep` 默认保留最近 7 份每日快照。服务重启后同一天不会重复生成；错过当天时间时启动会补做。也可在部署目录手工执行 `dotnet ApprovalService.Api.dll backup-now`（Docker 用 `docker exec approval-service dotnet ApprovalService.Api.dll backup-now`）。数据库使用 `VACUUM INTO`，**不要直接复制运行中的 `approval.db`**。

快照位于数据目录 `backups/daily-日期-时间-随机码/`；待执行迁移前另生成 `preupgrade-*`，不自动清理。完整目录含 `approval.db`、本地 `files/`、`identity-signing.key` 和 `complete.marker`。若使用 MinIO，需另按对象存储策略备份桶；接入应用加密主密钥和部署配置位于数据目录外，也必须另行保管。`backups/` 本身应同步到另一块受控磁盘或备份系统，避免和主数据盘一起损坏。

恢复演练：

1. 停止服务，确认不再有使用数据目录的审批服务进程。
2. 将原 `approval.db`、`approval.db-wal`、`approval.db-shm` 和 `files/` 移到隔离目录留底。勿在运行时覆盖。
3. 从带 `complete.marker` 的同一快照目录复制 `approval.db`、`files/`、`identity-signing.key` 回数据目录；恢复当时匹配的接入应用加密主密钥与部署配置。不要把旧 WAL/SHM 放回。
4. 启动服务，`/health/ready` 应返回 200；用管理员登录并读取已知审批单与附件。若恢复的是旧版库，先用对应版本程序验证，再按正常升级流程迁移。

后端检查程序每次会在 WAL 模式写入一条记录、在线快照、修改原库、把快照恢复到另一库并确认旧记录与附件、签名密钥仍在。该演练不替代生产环境的异地备份与恢复演练。

SQL Server、PostgreSQL 使用数据库官方备份工具及各自维护计划；内置每日备份只对 SQLite 生效。

## 压测

先在隔离测试库准备一个无同步业务钩子、只需一名审批人同意的已发布流程，并为接入应用绑定发起人与审批人。环境变量见 `backend/tests/load/approval-load.js`：`APPROVAL_BASE_URL`、`APPROVAL_API_KEY`、`APPROVAL_FLOW_CODE`、`APPROVAL_INITIATOR`、`APPROVAL_APPROVER`、`APPROVAL_DATA_JSON`；可选 `APPROVAL_VUS`（默认 10）和 `APPROVAL_DURATION`（默认 1m）。禁止对生产库直接运行生成审批单的压测。

```powershell
k6 run backend/tests/load/approval-load.js --summary-export "$env:TEMP\approval-load-summary.json"
```

脚本分别收集发起、审批、待办数量的 P95，阈值为 300、300、50 毫秒。记录硬件、负载、成功率、样本数和外部钩子状态到 `backend/tests/load/report-template.md`。完整 HTTP 本机检查程序也报告三项 P95；其轻负载结果只证明回归，不等于目标环境容量。

## 本机验证边界

本机没有 Docker 命令，镜像尚未实际构建和运行；Windows 服务需管理员权限和目标服务账户，本轮没有安装。代码编译、SQLite 备份恢复、HTTP 指标及 k6 脚本可以在开发机验证。首次部署时仍需在实际 Linux/Windows 目标机按上述步骤复验健康检查、附件恢复及凭据权限。

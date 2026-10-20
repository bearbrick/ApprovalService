# T-013 附件存储：本地文件和 MinIO / S3

状态：进行中
负责人：Codex 2026-10-20
里程碑：M3
依赖：T-005
设计书：§engine

## 目标

附件存在本地文件或 MinIO / S3，改配置切换；上传下载都查权限；没被用到的文件自动清理；有从本地搬到 MinIO 的命令。

## 范围

可以新建、修改的文件：

- `Application`、`Infrastructure` 的 `Files` 模块（扩展点 `IFileStorage`）、上传下载接口
- `Domain/Files/`：附件元数据实体与仓储抽象；`Infrastructure/Persistence/`：统一 EF 模型与三种提供程序的附件迁移
- `backend/src/ApprovalService.Api/Endpoints/Files/`、`backend/src/ApprovalService.Contracts/Protocol/`：上传下载路由和契约；`backend/tests/ApprovalService.Backend.Checks/FileChecks.cs`：场景检查
- `backend/tools/`：从本地存储迁移到 MinIO 的命令
- `docs/design/approval-service-design.html`：只同步 §api 中附件请求和下载行为的已实现描述
- S3 客户端要新增依赖：按 R8 写 ADR、征得同意再下载
- 新增本机已缓存的 `Minio` 7.0.0：它提供 S3 兼容对象上传、读取、删除及短时预签名地址，避免手写签名和 HTTP 协议；选择依据见 ADR 0009
- 图片缩略图可以用标签服务在用的 SkiaSharp（本机缓存里有）

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/Directory.Packages.props`
- 登记文件
- `docs/contracts.md`

## 验收标准

- [ ] 数据库只存文件信息（名称、大小、类型、SHA-256、存储位置、所属审批单）
- [ ] 按文件内容判断真实类型；默认单个 20 MB，可以改
- [ ] 能看这张审批单的人才能下载；MinIO 能被浏览器访问时签发几分钟有效的地址，否则服务转发
- [ ] 24 小时没被用到的上传文件自动清理；迁移命令能把本地文件搬到 MinIO
- [ ] 设置环境变量 `APPROVAL_TEST_MINIO` 时对真实 MinIO 跑一遍
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- Office、OFD 在线预览（二期）

## 交接记录

- 2026-10-20 · Codex · 恢复后完成本地/MinIO 存储、三套附件迁移、ZIP 结构辨识 DOCX/XLSX/PPTX（仅下载、不在线预览）、真实 SQLite 清理与本地转 MinIO 失败恢复/幂等/并发检查、可信身份 HTTP 上传下载检查，并把 §api 附件两行同步为实际协议。已提交 `46e015f`（契约）、`4047f69`（实现与迁移）、`22d2c82`（共享登记）、`d72d823`（README/架构）、`968a464`（身份与清理修正）；全量 `verify.ps1` 在 258 项后端检查时通过，后续新增两项迁移检查及 HTTP 端点复核后 `verify.ps1 -Part backend` 通过 260 项、0 警告。真实 MinIO 测试环境变量未提供，外部对象存储场景尚未执行。**当前未完成的集成决策**：设计要求业务系统发起前上传附件，但本任务公开上传路由只接受已验证用户 JWT；T-006 发起使用 X-Api-Key，需确定接入应用作为上传主体、同一应用关联文件及发起事务/可恢复绑定语义。主代理已向用户请求确认，收到后再实现；本任务继续保持“进行中”，未合并 main、未推送。OFD 与旧版 Office 的下载白名单也尚未由设计明确，当前拒绝并记录待确认。
- 2026-10-20 · Codex · 用户要求暂停以节省额度，本任务保持“进行中”，未合并 main、未推送。工作树 `C:/Users/dukun/.codex/worktrees/file-storage/ApprovalService`，分支 `ai/codex/T-013-file-storage`；已提交认领 `e858c5c`、合入 T-005 基线 `8f8ecd6`、范围登记 `16b461d`、缓存 Minio 7.0.0 与 ADR 0009 `59852b9`。当前有大量已暂存及未暂存的 T-013 源码、三套 AddStoredFiles 迁移、文件场景检查、README/契约/架构文档，**尚未提交实现**；不要丢弃工作区。已实现附件元数据、本地与 MinIO 存储、类型/大小验证、上传下载路由（当前无 T-006/T-007 可信身份接入，匿名默认 401）、实例可见性检查、24 小时孤儿清理、同数据库租约、本地到 MinIO 的迁移命令。上次 `verify.ps1 -Part backend` 通过 250 项检查；其后又将上传和迁移缓冲改成自动删除的临时文件、复用 MinIO 客户端，`dotnet build` 0 警告通过，但**尚未重新运行格式、后端或全量 verify**。恢复后先检查这些未提交改动并补测大文件流、迁移中断/重复运行和三套迁移；确认文件类型范围是否应补 Office 下载（目前仅 PDF/PNG/JPEG/GIF/WebP）；还需 MinIO 真实环境检查（设置 `APPROVAL_TEST_MINIO` 及桶/凭据）、确认下载流临时文件的响应生命周期、完善配置校验、运行完整 `verify.ps1`、提交实现与共享文件、勾选验收项。T-006/T-007 的 API Key/用户身份和实例关联业务入口仍待后续任务接入；公开路由不接受自报 userId。
- 2026-10-20 · Codex · 从 main 建立独立工作树并认领；依赖 T-005 已验收，已合入本任务分支作为数据库基础。原范围未列出完成元数据和三套迁移必需的 Domain/Files、Infrastructure/Persistence，也未列出上传下载的 Contracts、API、检查和迁移命令，已在上方补全。预计共享文件包括 `backend/Directory.Packages.props`（S3 客户端版本）、`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`（模块登记）、`docs/contracts.md`（上传下载协议）、`docs/architecture.md` 与 `docs/adr/README.md`（架构和新依赖 ADR）。本机已缓存 Minio 7.0.0 及所需依赖，官方 NuGet 显示于 2025-11-05 发布，约 611 KB，无需下载；新增依赖的理由和取舍将写 ADR。T-006 接入应用鉴权、T-007 用户登录尚未实现；本任务的公开下载路径必须先拒绝未认证请求，不能从客户端参数信任用户身份。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

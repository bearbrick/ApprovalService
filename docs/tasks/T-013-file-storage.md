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
- `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：收紧 MinIO 下载地址有效期校验
- `docs/architecture.md`：同步嵌入会话附件权限边界

## 验收标准

- [ ] 数据库只存文件信息（名称、大小、类型、SHA-256、存储位置、所属审批单）
- [ ] 按文件内容判断真实类型；默认单个 20 MB，可以改
- [ ] 能看这张审批单的人才能下载；MinIO 能被浏览器访问时签发几分钟有效的地址，否则服务转发
- [ ] 24 小时没被用到的上传文件自动清理；迁移命令能把本地文件搬到 MinIO
- [ ] 设置环境变量 `APPROVAL_TEST_MINIO` 时对真实 MinIO 跑一遍
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- Office、OFD 在线预览（二期）

## 交接记录

- 2026-10-20 · Codex · 用户已确认发起前附件由接入应用 `X-Api-Key` 上传：未关联文件只允许该应用访问，发起审批仅能引用同应用上传的文件并在提交时原子关联。按用户“只收尾现有工作”要求，本轮只记录决定，不继续新增附件/发起代码；下次从上传归属元数据、同应用校验和 T-006 发起事务接线恢复。普通抄送收件人投影、保密范围仍需按已评审设计补齐。最近完整验证为后端 269、客户端 33、前端 54 项。
- 2026-10-20 · Codex · 完成可独立修复的附件安全收尾：嵌入会话必须携带已验证的 `sessionKind=embed` 与 `clientAppCode`，已关联附件只向同应用且有人员权限的嵌入会话开放；缺失会话来源直接拒绝；未关联附件尚无可信应用归属，嵌入会话暂时无法下载，即使是同一上传人。MinIO 预签名地址从允许七天收紧为最多五分钟，并在配置和存储实现双重校验。DOCX/XLSX/PPTX 除 ZIP 条目外，还检查有界的包清单、入口关系及完整主部件 XML，空条目伪装被拒绝。新增本地/同应用/跨应用/缺失声明 HTTP 与应用服务场景、配置及存储有效期、伪装 Office 场景；后端 269 项、调用端 33 项（含真实服务端到端）、前端 54 项，完整 `verify.ps1` 通过。分别提交范围登记 `b8b8578`、实现 `2654163`、共享配置 `ad06d1b`、共享文档 `e070de2`、后端 README `fa7387f`；设计书原本已写明嵌入令牌绑定应用与“几分钟”直链，本轮未改设计。普通抄送在无已阅要求时不生成任务，现有持久化模型也没有可靠的抄送收件人投影，不能仅凭人员 ID 放开，须后续补投影再授权。保密流程可见性也缺明确持久化配置，未擅自猜测。真实 MinIO 测试环境仍未提供。应用级上传主体、同应用文件关联与发起时原子提交仍待用户答复，任务继续阻塞；不要将本分支的未关联附件下载限制误当成该设计决策。
- 2026-10-20 · Codex · 按只读安全复核继续修复与应用级上传主体决策无关的问题：嵌入会话下载按已验证的接入应用编码隔离，未关联附件在嵌入会话中先拒绝；MinIO 直链上限收紧到几分钟；Office 类型检查拒绝仅有空 ZIP 条目的伪装。计划增补文件场景、同步后端 README、`docs/contracts.md` 与 `docs/architecture.md`；共享的基础设施注册文件和两份文档已在上方登记。普通抄送仅在现有持久化数据能可靠识别收件人时修复，否则继续登记缺口。应用级上传与发起时原子关联仍待用户答复，任务保持阻塞。
- 2026-10-20 · Codex · 只读复核新增待修项，恢复 T-013 时与接入应用上传主体决策一并处理：嵌入会话下载须核对 JWT 的应用编码与审批单来源；附件可见性须覆盖普通抄送人，并遵守保密流程的参与人规则；MinIO 预签名地址上限须收紧到几分钟；Office 上传不能只靠 ZIP 条目名称判断真实格式；发起前上传的文件须记录应用归属，并与审批单发起可靠关联。现有实现对同一人员的跨应用附件可能越权，对普通抄送人可能误拒，且配置允许最长 7 天的直链。复核未改产品代码、未重跑验证，任务仍按 R13 阻塞，待用户回答上传主体方案后修复并补场景。
- 2026-10-20 · Codex · 按 AGENTS.md R13 暂停 T-013：设计未定义发起前附件的接入应用主体与绑定授权，而 T-006 用 `X-Api-Key` 发起、T-007 用 JWT 内部人员 ID 授权，当前文件路由只能安全处理 JWT 用户上传。建议确认：接入应用能否用自己的 API Key 先上传、元数据记录应用编码；发起时只允许同一应用引用并在审批单提交事务内（或用可恢复待发机制）绑定文件；未绑定文件仅原应用/上传用户可读取。主代理已把方案发给用户；确认后续接本分支实现并复跑全量验证。当前分支 `ai/codex/T-013-file-storage` HEAD `8c82ab1`，实现与设计书已提交，未合入主分支、未推送。全量 verify 曾通过（后端当时 258 项），最近后端 verify 260 项通过；真实 MinIO 测试环境未提供。OFD、旧版 Office 仅记录白名单待确认，不影响现有安全默认拒绝。
- 2026-10-20 · Codex · 恢复后完成本地/MinIO 存储、三套附件迁移、ZIP 结构辨识 DOCX/XLSX/PPTX（仅下载、不在线预览）、真实 SQLite 清理与本地转 MinIO 失败恢复/幂等/并发检查、可信身份 HTTP 上传下载检查，并把 §api 附件两行同步为实际协议。已提交 `46e015f`（契约）、`4047f69`（实现与迁移）、`22d2c82`（共享登记）、`d72d823`（README/架构）、`968a464`（身份与清理修正）；全量 `verify.ps1` 在 258 项后端检查时通过，后续新增两项迁移检查及 HTTP 端点复核后 `verify.ps1 -Part backend` 通过 260 项、0 警告。真实 MinIO 测试环境变量未提供，外部对象存储场景尚未执行。**当前未完成的集成决策**：设计要求业务系统发起前上传附件，但本任务公开上传路由只接受已验证用户 JWT；T-006 发起使用 X-Api-Key，需确定接入应用作为上传主体、同一应用关联文件及发起事务/可恢复绑定语义。主代理已向用户请求确认，收到后再实现；本任务继续保持“进行中”，未合并 main、未推送。OFD 与旧版 Office 的下载白名单也尚未由设计明确，当前拒绝并记录待确认。
- 2026-10-20 · Codex · 用户要求暂停以节省额度，本任务保持“进行中”，未合并 main、未推送。工作树 `C:/Users/dukun/.codex/worktrees/file-storage/ApprovalService`，分支 `ai/codex/T-013-file-storage`；已提交认领 `e858c5c`、合入 T-005 基线 `8f8ecd6`、范围登记 `16b461d`、缓存 Minio 7.0.0 与 ADR 0009 `59852b9`。当前有大量已暂存及未暂存的 T-013 源码、三套 AddStoredFiles 迁移、文件场景检查、README/契约/架构文档，**尚未提交实现**；不要丢弃工作区。已实现附件元数据、本地与 MinIO 存储、类型/大小验证、上传下载路由（当前无 T-006/T-007 可信身份接入，匿名默认 401）、实例可见性检查、24 小时孤儿清理、同数据库租约、本地到 MinIO 的迁移命令。上次 `verify.ps1 -Part backend` 通过 250 项检查；其后又将上传和迁移缓冲改成自动删除的临时文件、复用 MinIO 客户端，`dotnet build` 0 警告通过，但**尚未重新运行格式、后端或全量 verify**。恢复后先检查这些未提交改动并补测大文件流、迁移中断/重复运行和三套迁移；确认文件类型范围是否应补 Office 下载（目前仅 PDF/PNG/JPEG/GIF/WebP）；还需 MinIO 真实环境检查（设置 `APPROVAL_TEST_MINIO` 及桶/凭据）、确认下载流临时文件的响应生命周期、完善配置校验、运行完整 `verify.ps1`、提交实现与共享文件、勾选验收项。T-006/T-007 的 API Key/用户身份和实例关联业务入口仍待后续任务接入；公开路由不接受自报 userId。
- 2026-10-20 · Codex · 从 main 建立独立工作树并认领；依赖 T-005 已验收，已合入本任务分支作为数据库基础。原范围未列出完成元数据和三套迁移必需的 Domain/Files、Infrastructure/Persistence，也未列出上传下载的 Contracts、API、检查和迁移命令，已在上方补全。预计共享文件包括 `backend/Directory.Packages.props`（S3 客户端版本）、`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`（模块登记）、`docs/contracts.md`（上传下载协议）、`docs/architecture.md` 与 `docs/adr/README.md`（架构和新依赖 ADR）。本机已缓存 Minio 7.0.0 及所需依赖，官方 NuGet 显示于 2025-11-05 发布，约 611 KB，无需下载；新增依赖的理由和取舍将写 ADR。T-006 接入应用鉴权、T-007 用户登录尚未实现；本任务的公开下载路径必须先拒绝未认证请求，不能从客户端参数信任用户身份。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

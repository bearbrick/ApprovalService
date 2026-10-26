# T-015 打印审批单

状态：待验收
负责人：Codex 2026-10-26
里程碑：M3
依赖：T-010、T-011
设计书：§print

## 目标

服务端生成审批单 PDF，样式按设计书第 7 节；每个流程可以调；打印留痕。

## 范围

可以新建、修改的文件：

- `Printing` 模块、`GET /api/v1/instances/{id}/print` 和审批中心的打印
- PDF 生成候选 PDFsharp + MigraDoc（MIT）：按 R8 写 ADR、征得同意；先验证中文字体嵌入和表格跨页
- 二维码用标签服务在用的 ZXing.Net（本机缓存里有）；中文字体沿用标签服务的做法（使用单位上传，发布包不带）

本切片具体范围：
- `backend/src/ApprovalService.Application/Printing/**`、`backend/src/ApprovalService.Infrastructure/Printing/**`
- `backend/src/ApprovalService.Api/Endpoints/PrintingEndpoints.cs`、`backend/src/ApprovalService.Infrastructure/ApprovalService.Infrastructure.csproj`
- `backend/tests/ApprovalService.Backend.Checks/PrintingChecks.cs`
- `frontend/src/api/printing.ts`、`frontend/src/pages/center/ApprovalPrintAction.vue`、`frontend/src/pages/center/CenterDetailView.vue`
- `frontend/src/pages/admin/AdminPrintFont.vue`、`frontend/src/pages/admin/AdminSystemBranding.vue`
- `backend/README.md`、`frontend/README.md`、`docs/adr/0023-approval-pdf-printing.md`、设计书 §print

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `backend/Directory.Packages.props`
- `backend/src/ApprovalService.Contracts/Protocol/PrintingModels.cs`
- `backend/src/ApprovalService.Api/Program.cs`
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`
- `backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`
- `docs/contracts.md`、`docs/architecture.md`、`docs/adr/README.md`

## 验收标准

- [x] 页眉（Logo、公司名称、审批编号、第几次打印、二维码）、状态方框、基本信息两列、明细表（合计、大写、跨页重复表头）、审批记录（签名图）、附件清单和票据页（每页 4 张）、页脚（打印人、时间、页码）、可选水印
- [x] 每个流程能设显示哪些字段、顺序、要不要意见和签名、要不要票据页
- [x] 每次打印记进操作记录
- [x] 生成的 PDF 截图给用户看样式
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 完全自定义的打印模板（二期）

## 交接记录

2026-10-26 · Codex：打印切片已实现并待验收。人员 JWT 与来源应用 API Key 均可生成本权限范围 PDF；应用归档审计使用 `clientApp:{code}`。中文字体上传、单位品牌、锁定版本打印配置、明细合计大写、跨页重复表头、六列审批记录与签名、PNG/JPEG 每页四张票据、二维码、水印、页脚均已接通。生成成功后才以审批单版本 CAS 追加哈希链打印记录，冲突不交付旧文档。前端在详情提供生成、预览打印和保存，在系统信息提供授权字体上传。

全量 `verify.ps1` 已通过：后端 1194、调用端 96、前端 222 项，含真实 SDK 服务端端到端。设置 `APPROVAL_TEST_FONT=C:/Windows/Fonts/simfang.ttf` 仅在临时数据目录测试，覆盖 13 个打印场景：权限、跨应用、字体、字段裁剪、PDF、应用归档、计数审计与并发冲突。无字体或样张入库。额外用伪造报销数据和自制测试图片生成三页凭证，已用 Poppler 检查第一页及四图票据页；长明细六页样张检查重复表头。测试图片仅用于验证签名/票据嵌入，不是真人签名。

样张在仓库外 `C:/Users/dukun/.codex/worktrees/printing/evidence/approval-showcase.pdf`；第一页 `approval-showcase.png`、票据页 `approval-receipts.png`；真实 HTTP 样张 `approval-print.pdf/png`，跨页 `approval-multipage.pdf`、`approval-page2.png`。交由主任务向用户展示和进行外置 Chrome 验收，本子任务未打开浏览器。

部署需上传合法中文独立 TTF，并设置 `ApprovalService:Printing:PublicBaseUrl`。字体不支持 TTC/CFF，票据页仅 PNG/JPEG（其他附件保留清单）；限制 100 张/80 MiB、500 页、宽明细按八列分组。字体与上传人/时间原子保存在数据目录 `printing/font.json`，备份须覆盖它；无数据库迁移。此切片未增加 SDK 便利方法，业务系统可直接调用已公开的双鉴权 HTTP 下载接口。产品代码最后验证基线含 cb7ac7c，后续集成的管理员干预由主任务处理共享登记冲突。

2026-10-26 · Codex：另获用户明确确认同一路径人员 JWT/来源应用 API Key 双鉴权归档协议，API Key 仅本应用，审计主体明确为应用。

2026-10-26 · Codex：用户已明确同意引入本机缓存 PDFsharp 6.2.4（MIT，NuGet 包 4.4 MiB）和 ZXing.Net 0.16.11（Apache-2.0，NuGet 包 10 MiB）；不下载未缓存包。以集成分支 67191ee 为基线，T-011 已集成核心查询、权限及签名。登记共享改动：Contracts 打印接口、Application/Infrastructure 服务登记、Api/Program.cs、检查程序 Program.cs、Directory.Packages.props、docs/contracts.md、docs/architecture.md、docs/adr/README.md 及设计书 §print；各处仅追加打印接线。采用 PDFsharp 手工排版，先验证中文嵌入和跨页，详情打印复用中心权限裁剪。

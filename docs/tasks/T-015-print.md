# T-015 打印审批单

状态：进行中
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

- [ ] 页眉（Logo、公司名称、审批编号、第几次打印、二维码）、状态方框、基本信息两列、明细表（合计、大写、跨页重复表头）、审批记录（签名图）、附件清单和票据页（每页 4 张）、页脚（打印人、时间、页码）、可选水印
- [ ] 每个流程能设显示哪些字段、顺序、要不要意见和签名、要不要票据页
- [ ] 每次打印记进操作记录
- [ ] 生成的 PDF 截图给用户看样式
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 完全自定义的打印模板（二期）

## 交接记录

2026-10-26 · Codex：另获用户明确确认同一路径人员 JWT/来源应用 API Key 双鉴权归档协议，API Key 仅本应用，审计主体明确为应用。

2026-10-26 · Codex：用户已明确同意引入本机缓存 PDFsharp 6.2.4（MIT，NuGet 包 4.4 MiB）和 ZXing.Net 0.16.11（Apache-2.0，NuGet 包 10 MiB）；不下载未缓存包。以集成分支 67191ee 为基线，T-011 已集成核心查询、权限及签名。登记共享改动：Contracts 打印接口、Application/Infrastructure 服务登记、Api/Program.cs、检查程序 Program.cs、Directory.Packages.props、docs/contracts.md、docs/architecture.md、docs/adr/README.md 及设计书 §print；各处仅追加打印接线。采用 PDFsharp 手工排版，先验证中文嵌入和跨页，详情打印复用中心权限裁剪。

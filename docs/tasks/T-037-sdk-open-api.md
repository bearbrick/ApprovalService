# T-037 .NET SDK 首批开放接口

状态：阻塞
负责人：Codex 2026-10-20
里程碑：M4
依赖：T-027、T-006 已提交的稳定开放接口协议
设计书：§integration、§api

## 目标

把已经实现且验证的第一批开放接口包装成兼容 .NET Framework 4.6.1 的 SDK 方法，供业务系统使用，并为 T-016 保留后续接口的实现空间。

## 范围

可以新建、修改的文件：

- `docs/tasks/T-037-sdk-open-api.md`、`docs/tasks/README.md`：任务认领、索引与交接。
- `client/src/ApprovalService.Client/**`：稳定接口的路由副本、公开输入输出模型和方法；维持零 NuGet 包。
- `client/tests/ApprovalService.Client.Checks/**`：假 HTTP 请求、错误和真实服务端到端检查。
- `client/README.md`、必要时 `docs/design/approval-service-design.html`：同步 SDK 用法和已实现接口。

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/tasks/README.md`：追加 T-037 索引。
- `client/src/ApprovalService.Client/Internal/Protocol.cs`：保持后端 HTTP 头副本纯净；路由另放 `Internal/OpenApiPaths.cs`，以符合现有契约一致性检查。
- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`：仅在后端有已提交新错误码时同步。
- `client/tests/ApprovalService.Client.Checks/Program.cs`：登记新检查类。

## 验收标准

- [x] SDK 覆盖稳定的流程列表和结构、发起、实例查询、待办数量与列表及操作、撤回与催办。
- [ ] 每个方法有假 HTTP 检查，错误格式有检查，主要流程有真实服务端到端检查。前两项已通过；真实服务仅验证无 Key 的鉴权边界，成功业务链路未验证。
- [x] net461、netstandard2.0、net8.0 均编译；无 NuGet 依赖，中文完整 XML 注释。
- [x] `verify.ps1` 全量通过；README 与设计书同步。

## 不做

- 不猜尚未确认的 PATCH、重提、终止、批量、预览、钩子等协议；这些留给 T-016。
- 不改 T-006 工作树，不合并主分支或推送。

## 交接记录

- 2026-10-20 · Codex · SDK 首批方法和公开模型已覆盖已确认的十类调用：流程列表/结构、发起、按 ID/业务单号查询、待办数/列表、任务操作、撤回、催办。`fieldValues` 仅同步 T-006 已验证的可选审批人填写字段，不接未确认路由。每个方法通过假 HTTP 检查请求路径、编码、正文和响应；统一错误码、链路 ID 亦覆盖。`verify.ps1` 全量通过（后端 506、调用端 45、前端 65），net461/netstandard2.0/net8.0 零警告，5289 已释放。客户端 README 与设计书 §12 的 SDK 示例同步；设计条款本身未变。最初把路径常量加在 `Protocol.cs` 导致 HTTP 头副本检查失败，已迁至独立 `OpenApiPaths.cs` 并全量复验通过。基线使用已验收 T-027 及 T-006 的 `134f086`、`7e1b481` 两个已提交且全量验证的增量；T-006 整体仍在进行中，不代表其余协议已稳定。当前 `verify.ps1` 启动全新空库，既无接入应用/API Key，也没有创建接入应用的公开管理接口，因此真实 HTTP 仅能验证流程、发起、待办的无 Key 鉴权边界，无法执行成功的发起→查询→审批等链路。按 R13 保留第二条验收未勾并标记阻塞；建议 T-016 在有测试 Key、已发布流程及人员绑定的独立集成环境补成功链路 E2E，或提供可重复的测试夹具后恢复本任务。未合并主分支，未推送。
- 2026-10-20 · Codex · 从 `main` 建立独立工作树并认领任务；先登记任务索引、SDK 路由与错误码副本、检查程序入口等共享文件。仅引入 T-006 已提交并完成全量验证的稳定协议作为开发基线，T-006 整体仍在进行中。

# T-051 SDK 补齐已实现开放接口

状态：进行中
负责人：Codex 2026-10-23
里程碑：M4
依赖：T-027；T-006 已验证的稳定接口快照 `d0a2040`（T-006 尚待验收）
设计书：§integration、§api

## 目标

补齐当前后端已实现而 SDK 尚缺的开放接口方法、请求响应模型和回调验签，使业务系统可通过无第三方依赖的 .NET SDK 接入。T-016 待 T-006 验收后统一收口。

## 范围

可以新建、修改的文件：

- `client/src/ApprovalService.Client/`：流程预览、批量发起、PATCH、重提、终止、嵌入令牌、组织同步、附件上传下载、回调验签与解析；兼容 net461。
- `client/tests/ApprovalService.Client.Checks/`：每个新增方法的假 HTTP 处理器检查、回调篡改和真实服务端到端检查。
- `client/README.md`：新增 SDK 用法。
- `docs/design/approval-service-design.html`：仅同步实际已实现的 SDK 方法和版本，不改变审批业务设计。

## 共享文件

改之前在交接记录说明，改动尽量只追加，单独提交：

- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`、`Internal/Protocol.cs`：需要时同步后端契约副本。
- `client/tests/ApprovalService.Client.Checks/Program.cs`：登记新增检查。

## 验收标准

- [ ] 已实现开放接口的请求、路径及响应模型与后端 Contracts 一致。
- [ ] 新增方法均有假处理器检查；真实服务场景覆盖主要调用。
- [ ] 回调签名错误和正文被修改时拒绝，正确签名可解析事件。
- [ ] SDK 三目标框架编译通过，无第三方包，遵守 R4 与中文完整注释。
- [ ] `verify.ps1` 全量通过。
- [ ] README 与设计书 SDK 用法同步；交接标明 T-016 尚待 T-006 验收。

## 不做

- 不实现后端尚未提供的打印接口；不发包；不提供 UI 控件。

## 交接记录

- 2026-10-23 · Codex · T-006 稳定 HEAD `d0a2040` 的全量验证已通过，但任务状态仍是待验收；按 AGENTS.md §6.1 不认领依赖其完成的 T-016。本任务从 main 单独建分支，计划合入 T-006 稳定快照后补齐可调用的已实现接口。共享 `ApprovalErrorCodes.cs`、`Internal/Protocol.cs` 仅在确有契约变化时同步；检查入口 `Program.cs` 单独提交。无新增 NuGet 包。

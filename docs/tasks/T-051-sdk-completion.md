# T-051 SDK 补齐已实现开放接口

状态：待验收
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
- `client/Directory.Build.props`：新增兼容 API 属于次版本扩展，SDK 版本从 0.1.0 升至 0.2.0。

## 验收标准

- [x] 已实现开放接口的请求、路径及响应模型与后端 Contracts 一致。
- [x] 新增方法均有假处理器检查；真实服务场景覆盖主要调用。
- [x] 回调签名错误和正文被修改时拒绝，正确签名可解析事件。
- [x] SDK 三目标框架编译通过，无第三方包，遵守 R4 与中文完整注释。
- [x] `verify.ps1` 全量通过。
- [x] README 与设计书 SDK 用法同步；交接标明 T-016 尚待 T-006 验收。

## 不做

- 不实现后端尚未提供的打印接口；不发包；不提供 UI 控件。

## 交接记录

- 2026-10-23 · Codex · 完成预览、批量发起、审批数据 PATCH、重新提交、终止、嵌入令牌、组织推送/绑定、附件上传/分身份下载、回调 HMAC 验签及可选自动动作结果解析；发起接口新增稳定请求 ID 重载。SDK 为 0.2.0，不引入 NuGet 包，net461/netstandard2.0/net8.0 全编译通过。全量 `verify.ps1` 通过：后端 737、调用端 68（含真实 HTTP）、前端 119 项检查。临时端到端夹具没有登录账号，因此嵌入令牌只验证明确的未开通错误；假处理器覆盖成功响应。后端尚未提供打印 PDF 开放接口，SDK 没有臆造。T-016 仍待 T-006 从待验收变已完成后再整体收口。
- 2026-10-23 · Codex · 新增的预览、批量和附件等公开方法均是向后兼容扩展；按 R4 预登记共享 `client/Directory.Build.props`，待实现验证后单独提交版本号 0.2.0。检查入口 `Program.cs` 只追加新增检查调用。后端未实现打印 PDF，SDK 不猜测其协议。
- 2026-10-23 · Codex · T-006 稳定 HEAD `d0a2040` 的全量验证已通过，但任务状态仍是待验收；按 AGENTS.md §6.1 不认领依赖其完成的 T-016。本任务从 main 单独建分支，计划合入 T-006 稳定快照后补齐可调用的已实现接口。共享 `ApprovalErrorCodes.cs`、`Internal/Protocol.cs` 仅在确有契约变化时同步；检查入口 `Program.cs` 单独提交。无新增 NuGet 包。

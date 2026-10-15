# T-027 调用端基础连接与错误边界

状态：进行中
负责人：Codex 2026-10-15
里程碑：M4 前置框架
依赖：T-000
设计书：§12、§13、§14

## 目标

让 SDK 在正式业务接口加入前具备可靠的连接配置、超时、取消和错误边界，使接入方可以复用同一个客户端并准确区分调用失败原因。

## 范围

- `client/src/ApprovalService.Client/ApprovalServiceClient.cs`
- `client/src/ApprovalService.Client/ApprovalServiceClientOptions.cs`
- `client/tests/ApprovalService.Client.Checks/ClientChecks.cs`
- `client/README.md`
- 本任务文件

## 共享文件

- `docs/tasks/README.md`：仅追加本任务索引，单独提交；认领时已登记。

## 验收标准

- [ ] 服务地址与超时配置在创建客户端时得到明确验证；创建后修改配置对象不影响已有客户端
- [ ] 调用方主动取消保持取消语义；超时和网络错误保留现有错误码
- [ ] 非 JSON 错误响应按 HTTP 状态给出适当错误码；场景检查覆盖上述边界
- [ ] `verify.ps1 -Part client` 通过
- [ ] 调用端 README 已更新

## 不做

- 审批业务接口、回调验签、WinUI 项目或新依赖；这些由后续任务按契约实现。
- 后端契约和已评审设计书的产品方案变更。

## 交接记录

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

- 2026-10-15 · Codex · 认领并预登记共享任务索引。SDK 骨架已有服务信息、请求 ID、基本网络/超时错误；本任务只加固通用连接边界，不提前实现未定的业务协议。工作树基于 main 的骨架提交，较新的原型/画布文档不在本任务范围内。

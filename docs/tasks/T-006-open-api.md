# T-006 开放接口、接入应用、回调和钩子

状态：待认领
负责人：—
里程碑：M4
依赖：T-004、T-005
设计书：§integration、§api

## 目标

业务系统能发起（传数据或只传单号）、查询、更新数据、撤回、重新提交、催办、终止；拿待办数量和列表、做审批操作；回调和钩子经出站适配层可靠送达。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Contracts/Protocol/`：开放接口的请求和响应、新错误码
- `backend/src/ApprovalService.Api/Endpoints/OpenApi/`；`Application/Integration/`（接入应用、API Key 只存 SHA-256、流程授权）
- `backend/src/ApprovalService.Infrastructure/Integration/`：出站适配层（HMAC 签名、5 秒超时、按 1 分钟 / 5 分钟 / 30 分钟 / 2 小时重试、发送日志）、钩子调用（取数、校验、取人、业务动作）
- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`：新错误码的副本
- 检查程序 `OpenApiChecks*.cs`：用一个假的业务系统后端

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/contracts.md`：写全请求、响应、回调格式
- `backend/src/ApprovalService.Contracts/**`
- `ApprovalErrorCodes.cs`
- `Program.cs`（Api、检查程序）

## 验收标准

- [ ] 设计书第 13 节的审批服务接口全部可用（嵌入令牌、附件、打印除外）
- [ ] “流程编码 + 业务单号”在进行中的审批里唯一，重复发起拿到已有的那张
- [ ] 只传单号发起时调取数接口；校验钩子不通过时挡住并返回原因；钩子超时有明确的错误
- [ ] 回调带签名、失败按规则重试、可以手动重发；同一事件重发时 `eventId` 不变
- [ ] `tasks/count` 能只算本接入应用的流程，也能算全部（按接入应用设置）
- [ ] 性能初定：发起、审批 P95 ≤ 300 ms（不含钩子），待办数量 P95 ≤ 50 ms，写进交接记录
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 嵌入令牌和免登录地址（T-007）
- 附件（T-013）
- 打印（T-015）
- SDK 方法（T-016）

## 交接记录

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

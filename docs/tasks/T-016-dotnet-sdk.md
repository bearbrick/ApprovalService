# T-016 .NET SDK 补齐全部接口

状态：进行中
负责人：Codex 2026-10-24
里程碑：M4
依赖：T-006
设计书：§integration、§api

## 目标

SDK 覆盖设计书第 13 节的全部审批服务接口，加回调验签和解析；兼容 .NET Framework 4.6.1、不依赖任何包。

## 范围

可以新建、修改的文件：

- `client/src/ApprovalService.Client/`：发起、查询、更新数据、撤回、重新提交、催办、终止、待办数量和列表、审批操作、免登录地址、推送组织架构、上传下载附件、回调验签和解析
- `client/tests/ApprovalService.Client.Checks/`：假处理器的检查和端到端检查

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `client/src/ApprovalService.Client/ApprovalErrorCodes.cs`、`Internal/Protocol.cs`

## 验收标准

- [ ] 不用 record、init、required（R4，检查程序会拦）
- [ ] 每个方法有假处理器的检查；设置 `APPROVAL_SERVICE_URL` 时对真实服务跑主要流程
- [ ] 回调验签：签名不对、请求体被改都能识别
- [ ] `client/README.md` 写清用法
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 发布到包源（公司暂时没有私有源）

## 交接记录

- 2026-10-24 · Codex · T-006 已独立验收并合入整合基线 `9cb0b3b`，现认领 T-016；先对照已验证的 T-051 SDK 和开放 Contracts 逐项核查，再补缺口并全量验证。后端尚未提供的打印接口不臆造 SDK 方法。
<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

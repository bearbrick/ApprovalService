# T-020 管理后台

状态：进行中
负责人：Codex 2026-10-23
里程碑：M3
依赖：T-005、T-007
设计书：§center、§org、§integration

## 目标

管理员能管理流程、组织架构、接入应用、日志、系统信息和权限，能做管理员干预。

## 范围

可以新建、修改的文件：

- 登录、用户和权限、系统信息（公司名称和 Logo，打印页眉用）、操作日志：沿用标签服务的做法
- 流程管理：分组、版本、发布、导入导出
- 组织架构：部门树、人员、角色、审批矩阵、工作日历、绑定关系
- 接入应用：API Key、回调和钩子地址、签名密钥、允许嵌入的网站、调用和回调日志、重发
- 管理员干预：修改审批人、跳转、终止、离职交接（要填原因）

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `frontend/src/App.vue`、`frontend/src/model/routes.ts`
- 登记文件

## 验收标准

- [ ] 每个页面的接口都查权限
- [ ] 密钥只在生成时显示一次
- [ ] 管理员干预都记进操作记录
- [ ] 页面截图检查
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 统计报表（T-019）

## 交接记录

- 2026-10-23 · Codex · 认领后从 main 合入已验收的 T-007 基线，先接入管理员登录、权限校验、部门/人员与接入应用安全列表。拟修改共享文件：`backend/src/ApprovalService.Contracts/Protocol/ApiRoutes.cs` 增加管理读取路由；`backend/src/ApprovalService.Api/Program.cs` 登记管理端点；`backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs` 登记管理用例；`backend/tests/ApprovalService.Backend.Checks/Program.cs` 登记场景检查；`docs/contracts.md` 与 `docs/architecture.md` 同步新管理读取接口。共享文件改动单独提交。正式页面的其它写入功能尚需逐项实现。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

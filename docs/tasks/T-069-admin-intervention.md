# T-069 管理员干预前端

状态：阻塞
负责人：Codex 2026-10-26
里程碑：M3
依赖：T-020、管理员运行干预 HTTP 契约与授权查询
设计书：§rules、§center、§org

## 目标

把管理员干预占位页接到受保护的真实审批实例查询、修改审批人、跳转、终止和离职交接接口，所有写入填写原因并留审计记录。

## 范围

- `frontend/src/pages/admin/AdminIntervention.vue`
- 新增独立干预 API、组合函数和纯逻辑模型及对应检查文件
- `frontend/tests/checks.ts`（仅登记新检查）
- `frontend/README.md`
- 本任务文件

## 共享文件

- 暂无。后续确需登记共享文件时先追加交接记录；不改角色和工作日历任务文件。

## 验收标准

- [ ] 仅使用真实管理员授权接口，列表和详情均在服务端限定管理范围
- [ ] 干预原因必填，展示真实结果并处理权限、并发和部分失败
- [ ] 明示固定审批人仅改草稿并发布新版本，在途实例继续旧发布版本
- [ ] 新行为场景检查通过
- [ ] 全量 `verify.ps1` 通过
- [ ] 外置 Google Chrome 验收（由集成任务执行）
- [ ] 相关文档已更新

## 不做

- 不调用业务 API Key 接口冒充管理员操作
- 不把委托规则操作当成改派或离职交接
- 不直接修改历史发布版本，不改变在途实例锁定版本
- 不改角色管理与工作日历文件，不新增依赖

## 交接记录

- 2026-10-26 · Codex · 从集成分支 `ai/codex/T-050-feature-integration` 的 `51d9f72` 建立独立分支 `ai/codex/T-069-admin-intervention`，检查后按 R13 阻塞，未修改产品源码。`Contracts/Protocol` 和 `Api/Endpoints` 没有管理员实例查询、修改审批人、跳转、终止或手工离职交接接口。`IApprovalRuntimeAppService.ListDepartureTasksAsync` / `ReassignDepartedTaskAsync` 是内部应用服务，由 `DepartureJobHandler` 按发布策略自动处理，并非供浏览器调用的协议。已有 `CenterRoutes.AdminDelegations` / `AdminDelegateTasks` 仅管理委托；`OpenApiRoutes.InstanceTerminate` 使用接入应用 API Key，不能代替管理员 JWT 接口。建议先补受保护的管理员运行查询和统一命令用例及契约，再继续页面。用户已明确固定审批人仅修改草稿后重新发布，在途实例保持原发布版，此要求已记录，后续接口及页面应一致体现。未运行验证：当前仅新增阻塞任务记录，没有实现可交付功能。已将缺口报告给集成任务。
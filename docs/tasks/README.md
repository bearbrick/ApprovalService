# 任务看板

每个任务一个文件，**状态以任务文件为准**，这里只做索引，不写状态，避免多人同时改同一个文件。认领、交接流程见 AGENTS.md 的“多 AI 协作”。

查看全部任务状态：

```powershell
Select-String -Path docs/tasks/T-*.md -Pattern '^(状态|负责人)：'
```

状态取值：`待认领` → `进行中` → `待验收` → `已完成`；卡住时写 `阻塞`，并在交接记录里写原因。

## 索引

里程碑对应设计书第 15 节：M1 设计器原型、M2 引擎与审批规则、M3 审批中心、M4 业务对接、M5 统计与上线准备、M6 试点。建议顺序：T-001 → T-002 → T-003 / T-004 / T-005 → T-006 → 其余按依赖。

| 编号 | 标题 | 部分 | 里程碑 | 依赖 | 主要目录 |
| --- | --- | --- | --- | --- | --- |
| [T-000](T-000-scaffold.md) | 搭建仓库骨架 | 全部 | — | — | 全部 |
| [T-001](T-001-flow-definition.md) | 流程定义 JSON 与执行图编译 | 后端 + 前端模型 | M1 | — | Contracts/Flows、Engine/Definitions；frontend：src/model/flow.ts |
| [T-002](T-002-engine-core.md) | 引擎内核：令牌、命令、事件 | 后端 | M2 | T-001 | Engine/Execution |
| [T-003](T-003-assignees-and-policies.md) | 找审批人与多人审批方式 | 后端 | M2 | T-002 | Engine/Assignees、Engine/Policies |
| [T-004](T-004-approval-operations.md) | 审批操作全集与委托 | 后端 | M2 | T-003 | Engine/Commands |
| [T-005](T-005-persistence.md) | 持久化：数据库、仓储、待发队列 | 后端 | M2 | T-002 | Domain、Infrastructure/Persistence、Infrastructure/Jobs |
| [T-006](T-006-open-api.md) | 开放接口、接入应用、回调和钩子 | 后端 + 调用端副本 | M4 | T-004、T-005 | Api/Endpoints/OpenApi、Application/Integration、Infrastructure/Integration |
| [T-007](T-007-organization-and-identity.md) | 组织架构、账号与嵌入令牌 | 后端 | M3 | T-005 | Organization、Identity 模块 |
| [T-008](T-008-tree-designer.md) | 自由画布工作台与审批节点配置 | 前端 | M1 | T-001 | src/designer/workbench、src/model |
| [T-009](T-009-canvas-designer.md) | 自由画布设计器（BPMN 子集） | 前端 | M1 | T-001 | src/designer/canvas |
| [T-010](T-010-approval-data-and-layout.md) | 审批数据定义、表达式与详情排版 | 前端 + 后端 | M1 | T-001 | Engine/Expressions；frontend：src/designer/data |
| [T-011](T-011-approval-center.md) | 审批中心（电脑网页） | 后端 + 前端 | M3 | T-004、T-005、T-007 | Api/Endpoints/Center；frontend：src/pages/center |
| [T-012](T-012-mobile-web.md) | 手机网页 | 前端 | M3 | T-011 | src/pages/mobile |
| [T-013](T-013-file-storage.md) | 附件存储：本地文件和 MinIO / S3 | 后端 | M3 | T-005 | Files 模块 |
| [T-014](T-014-notifications.md) | 消息提醒、时限与工作日历 | 后端 | M3 | T-005、T-007 | Notifications 模块 |
| [T-015](T-015-print.md) | 打印审批单 | 后端 | M3 | T-010、T-011 | Printing 模块 |
| [T-016](T-016-dotnet-sdk.md) | .NET SDK 补齐全部接口 | 调用端 | M4 | T-006 | src/ApprovalService.Client |
| [T-017](T-017-demo-integration.md) | 示例业务系统：对接类库、本机业务后端、演示场景 | 调用端 | M4 | T-016 | samples/ApprovalService.Demo.Integration |
| [T-018](T-018-demo-winui.md) | 示例业务系统：WinUI 3 界面 | 调用端 | M4 | T-017 | samples/ApprovalService.Demo.WinUI |
| [T-019](T-019-reports.md) | 统计报表 | 后端 + 前端 | M5 | T-005、T-011 | Reporting 模块；frontend：src/pages/admin/reports |
| [T-020](T-020-admin-console.md) | 管理后台 | 后端 + 前端 | M3 | T-005、T-007 | Api/Endpoints/Admin；frontend：src/pages/admin |
| [T-021](T-021-deploy-and-load-test.md) | 部署、监控、备份与压测 | 后端 | M5 | T-005、T-006 | backend/deploy、backend/tests/load |
| [T-022](T-022-templates-test-mode-wizard.md) | 流程模板库、测试模式与接入向导 | 后端 + 前端 | M4 | T-006、T-008、T-010 | Flows、Integration 模块；frontend：src/pages/admin |
| [T-023](T-023-pilot.md) | 试点：费用报销和采购申请 | 全部 | M6 | T-006～T-022 | 全部 |
| [T-044](T-044-verified-hooks-generation.md) | 钩子与应用身份代际集成验证 | 全部 | M2～M4 | T-041、T-006 | 已验证来源的稳定集成 |
| [T-037](T-037-sdk-open-api.md) | .NET SDK 首批开放接口 | 调用端 | M4 | T-027、T-006 稳定协议 | src/ApprovalService.Client、tests/ApprovalService.Client.Checks |
| [T-024](T-024-high-fidelity-prototypes.md) | 全功能高保真交互原型 | 原型文档 | M1 | T-000 | docs/prototypes |
| [T-029](T-029-work-calendar.md) | 工作日历与节点时限基础 | 后端 | M3 | T-005 | Domain/Notifications/Calendar |
| [T-025](T-025-frontend-foundation.md) | 完善前端公共框架 | 前端 | M1 | T-000 | frontend/src、frontend/tests |
| [T-026](T-026-backend-configuration-validation.md) | 后端数据配置启动校验 | 后端 | 框架完善 | T-000 | Infrastructure 配置、后端检查 |
| [T-027](T-027-client-foundation.md) | 调用端基础连接与错误边界 | 调用端 | M4 前置框架 | T-000 | src/ApprovalService.Client、tests/ApprovalService.Client.Checks |
| [T-028](T-028-departure-runtime.md) | 离职在途待办按节点策略处理 | 后端 | M2 / M3 | T-004、T-005、T-007 衔接 | Engine/Commands、Application/Runtime、Infrastructure/Persistence |
| [T-030](T-030-notification-channels.md) | 消息渠道与持久发送基础 | 后端 | M3 | T-005 | Notifications 模块 |
| [T-031](T-031-center-frontend.md) | 审批中心电脑前端基础 | 前端 | M3 | T-025、T-008、T-009、T-010 | frontend/src/pages/center、frontend/src/model/center.ts |
| [T-032](T-032-integration-baseline.md) | 已验收功能的可验证集成基线 | 全部 | M2～M4 | T-001～T-005、T-008～T-010、T-025～T-028、T-030、T-031 | backend、frontend、client、docs |
| [T-033](T-033-admin-frontend.md) | 管理后台前端基础 | 前端 | M3 | T-025、T-008、T-031 | frontend/src/pages/admin、frontend/src/model/admin.ts |
| [T-034](T-034-flow-operation-policies.md) | 流程级撤回与退回重提策略 | 后端 + 前端 | M2 / M1 | T-001、T-004、T-008～T-010、T-032 | Contracts/Flows、Engine/Definitions、frontend/src/designer/workbench |
| [T-035](T-035-mobile-frontend.md) | 手机审批中心前端基础 | 前端 | M3 | T-032、T-033 | frontend/src/pages/mobile、frontend/src/model/mobile.ts |
| [T-036](T-036-ui-integration.md) | 已验收前端成果的可验证集成基线 | 全部 | M2～M4 | T-032、T-034、T-035 | backend、frontend、client、docs |
| [T-038](T-038-verified-integration.md) | 已验证稳定分支整合快照 | 全部 | M2～M4 | T-036、T-006/T-007/T-037 稳定提交 | backend、frontend、client、docs |
| [T-039](T-039-verified-integration.md) | 后续稳定功能集成与应用附件原子关联 | 全部 | M2～M4 | T-038、T-006/T-007/T-013/T-029 稳定提交 | backend、frontend、client、docs |
| [T-040](T-040-sdk-live-fixture.md) | SDK 真实服务临时种库夹具 | 后端检查 + 调用端检查 | M4 | T-038、T-037 | backend/tests、client/tests、verify.ps1 |
| [T-041](T-041-client-app-auth-generation.md) | 接入应用身份代际与嵌入会话永久失效 | 后端 | M4 | T-006、T-007、T-039、T-042 | Integration、Identity、Persistence |
| [T-042](T-042-verified-hook-integration.md) | 钩子与 SDK 夹具稳定集成快照 | 全部 | M2～M4 | T-039、T-040、T-006 稳定提交 | backend、frontend、client、docs |
| [T-043](T-043-confidential-visibility.md) | 保密流程与抄送收件人附件可见性 | 后端 + 契约镜像 | M3 | T-013、T-041、T-042 | Runtime、Files、Contracts/Flows、frontend/src/model |
| [T-045](T-045-canvas-polish.md) | 自由画布走线与评审预览 | 前端 | M1 | T-009、T-011 | src/designer/canvas |
| [T-046](T-046-bpmn-modeler.md) | 使用标准 BPMN 建模器替换自绘画布 | 前端 | M1 | T-009、T-045 | src/designer/canvas |
| [T-049](T-049-reporting-foundation.md) | 报表真实事实读取基础 | 后端 | M5 | T-005、T-007 | Domain/Reporting、Infrastructure/Reporting |
| [T-050](T-050-feature-integration.md) | 功能分支整合与统一验收 | 全部 | M5 | 已提交稳定快照 | backend、frontend、client、docs |
| [T-051](T-051-sdk-completion.md) | SDK 开放接口补全切片 | 调用端 | M4 | T-006 稳定快照 | client/src、client/tests |
| [T-052](T-052-embed-host.md) | 同源审批中心页面托管与内嵌来源限制 | 后端 + 契约镜像 | M3 | T-011 稳定快照 | Api/Hosting、Center、Application/Integration |
| [T-053](T-053-service-node-properties.md) | BPMN 画布的审批服务属性 | 前端 | M1 | T-046 已验证稳定快照 | src/designer/workbench、src/designer/canvas |
| [T-054](T-054-reporting-metrics.md) | 真实统计事实的指标计算 | 后端 | M5 | T-049 已验证稳定快照 | Domain/Reporting、backend/tests |
| [T-055](T-055-assignee-publication.md) | 指定审批人兜底的发布校验 | 前端 + 后端 | M1 | T-053 已验证稳定快照 | Engine/Definitions、frontend/src/designer/canvas |
| [T-056](T-056-reporting-cross-period.md) | 跨期报表事实读取 | 后端 | M5 | T-049、T-054 已验证稳定快照 | Domain/Reporting、Infrastructure/Reporting |
| [T-057](T-057-reporting-query-service.md) | 组织管理员内部报表查询 | 后端 | M5 | T-054、T-056 已验证稳定快照 | Application/Reporting、Infrastructure/Reporting |
| [T-058](T-058-reporting-http.md) | 受保护的报表查询接口 | 后端 | M5 | T-057 已验证稳定快照 | Application/Reporting、Api/Endpoints |
| [T-059](T-059-reporting-frontend.md) | 管理后台真实报表页面 | 前端 | M5 | T-058 已验证稳定快照 | frontend/src/pages/admin、frontend/src/api |
| [T-060](T-060-reporting-projection.md) | 报表小时桶与增量水位规划 | 后端 | M5 | T-058、T-059 已验证稳定快照 | Domain/Reporting、backend/tests |
| [T-061](T-061-reporting-export.md) | 报表汇总 Excel 导出 | 后端 | M5 | T-058、T-059 已验证稳定快照 | Application/Reporting、Infrastructure/Reporting、Api/Endpoints |
| [T-062](T-062-reporting-download.md) | 报表页 Excel 下载入口 | 前端 | M5 | T-061 已验证稳定快照 | frontend/src/api、frontend/src/pages/admin |
| [T-063](T-063-handling-action-alias.md) | 办理节点 submit 与 handle 兼容 | 后端 | M1 | T-002、T-004、T-022 已验证基线 | Engine/Commands、backend/tests |
| [T-064](T-064-dynamic-condition-evaluation.md) | 后续网关按审批人填写值求条件 | 后端 | M1 | T-004、T-022、T-063 已验证基线 | Application/Runtime、Application/Administration、backend/tests |
| [T-065](T-065-bpmn-viewport-fit.md) | BPMN 画布居中适配与完整显示 | 前端 | M1 | T-046 已验证稳定快照 | src/designer/canvas |

“主要目录”是相对于对应部分（`frontend/`、`backend/src/`、`client/`）的路径。


新增任务：复制 [_template.md](_template.md)，编号取当前最大值加 1，并在上表追加一行（只追加，不改别的行）。
| [T-067](T-067-work-calendar-admin.md) | 在线工作日历管理 | 待验收 | Codex 2026-10-26 | T-029 |
| [T-066](T-066-role-management.md) | 组织角色管理 | 待验收 | Codex 2026-10-26 | T-007、T-020 管理基础 |
| [T-069](T-069-admin-intervention.md) | 管理员运行干预协议与页面 | 待验收 | Codex 2026-10-26 | T-020、用户已批准管理协议 |
| [T-068](T-068-sidebar-stage-badge.md) | 移除工作区过期阶段标记 | 待验收 | Codex 2026-10-26 | T-025 |
| [T-071](T-071-center-graph-polish.md) | 审批中心 BPMN 进度图精修 | 待验收 | Codex 2026-10-26 | T-011 |
| [T-072](T-072-session-refresh.md) | 刷新页面恢复本地登录会话 | 待验收 | Codex 2026-10-26 | T-050 稳定集成 |
| [T-073](T-073-compact-designer.md) | 压缩流程设计工作区顶部 | 待验收 | Codex 2026-10-26 | T-050 稳定集成 |

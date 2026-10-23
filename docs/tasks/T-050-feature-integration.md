# T-050 功能分支整合与统一验收

状态：进行中
负责人：Codex 2026-10-23
里程碑：M5
依赖：—（整合各任务已提交、待验收的稳定快照）
设计书：§arch、§designer、§center、§integration、§stats

## 目标

将已完成的标准 BPMN 设计器、开放接口、通知和随后完成的审批中心、管理后台、报表基础分支整合到一条可验证的独立分支，供用户统一查看与验收。

## 范围

- 合并 `ai/codex/T-046-bpmn-modeler`、`ai/codex/T-006-open-api`、`ai/codex/T-014-notification-rules` 的完整提交；其他分支完成后再合入。
- 仅处理上述分支合并产生的冲突与跨部分契约、注册、编译和测试问题；不引入额外业务功能。
- `docs/tasks/T-050-feature-integration.md`：记录整合顺序、验证结果与交接。

## 共享文件

- 合并可能触及 `docs/tasks/README.md`、`docs/adr/README.md`、`docs/architecture.md`、`docs/contracts.md`、`docs/design/approval-service-design.html`。
- 合并可能触及 `frontend/package.json`、`frontend/package-lock.json`、`frontend/src/App.vue`、`frontend/src/model/routes.ts`。
- 合并可能触及 `backend/src/ApprovalService.Contracts/**`、`backend/src/ApprovalService.Api/Program.cs`、应用与基础设施服务登记、三库迁移登记及检查程序入口。
- 这些共享文件只按双方已验证的行为合并，不能确定时遵守 AGENTS.md R13 停下并记交接。

## 验收标准

- [ ] 各完成分支的功能与任务文档均在整合分支且没有遗漏。
- [ ] 冲突已按契约优先和各任务交接记录处理，编译与场景检查通过。
- [ ] `verify.ps1` 全量通过，外置 Chrome 验证主要页面。
- [ ] 交接说明剩余未完成工作与主分支合并边界。

## 不做

- 不把整合分支直接合并到主分支或推送远程；该步骤按 AGENTS.md §6.2 征得用户确认。
- 不自行决定报表统计口径或新增不兼容契约。

## 交接记录

- 2026-10-23 · Codex · 已整合 T-046 BPMN、T-006 开放接口、T-014 通知和 T-049 真实报表事实读取。合并保留中心查询与动作重试检查、开放接口文档两侧内容及通知规则；前端流程模型因合并后超过 500 行，将打印设置拆为 `flow-print.ts`，后端文本契约检查同步读取该文件。`npm ci` 后前端 124 项检查和生产构建通过，后端 822 项场景及契约检查通过。T-011/T-020/T-051 仍在独立分支进行中，未合入；后续应先合 T-011 再合含其迁移 cherry-pick 的 T-020，检查三数据库迁移快照，最后跑全量验证和 Chrome 复核。
- 2026-10-23 · Codex · 从主分支创建独立整合分支，先合入已完成且全量验证通过的功能，后续分支待各负责人交接后再集成。共享文件变化仅限冲突处理和必要同步。


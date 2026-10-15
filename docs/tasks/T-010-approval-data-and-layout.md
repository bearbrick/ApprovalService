# T-010 审批数据定义、表达式与详情排版

状态：待验收
负责人：Codex 2026-10-15
里程碑：M1
依赖：T-001
设计书：§form

## 目标

定义业务系统要传的字段（含明细表）、计算字段、摘要字段、节点字段权限、审批人填写的字段；设计审批人看到的排版；发起时按定义校验，表达式在后端安全地算。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Engine/Expressions/`（新建）：自己写的表达式解析（四则运算、比较、且或、SUM、AVG、MAX、MIN、IF、ANY、日期差、人民币大写），不执行任意代码
- 后端发起时的数据校验（必传、类型、明细表），错误带字段路径（`data.items[0].qty`）
- `backend/src/ApprovalService.Engine/Definitions/FlowSemanticValidator.cs`：发布时调用受限表达式解析器，不能让无效条件或公式进入运行时
- `backend/src/ApprovalService.Domain/Runtime/ApprovalSerialNumber.cs`：审批编号规则（前缀、日期、流水号）；序号由调用方分配
- `frontend/src/designer/data/`（新建）：字段定义、排版设计（分组、几列、明细列宽、摘要字段）
- `backend/tests/ApprovalService.Backend.Checks/ApprovalDataChecks.cs`、`frontend/tests/data-checks.ts`：表达式、数据校验、发布前解析与排版场景检查
- `backend/tests/ApprovalService.Backend.Checks/FlowDefinitionChecks.cs`：补发布阶段的无效表达式回归
- `backend/README.md`、`frontend/README.md`：表达式与排版使用说明

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/contracts.md`
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：登记审批数据检查
- `frontend/tests/checks.ts`：登记纯数据模型检查
- `docs/architecture.md`：说明表达式与排版模块落点

## 验收标准

- [x] 表达式检查覆盖正常、边界和恶意输入（超长、深嵌套、未知函数）
- [x] 同一份定义能生成接口的 JSON 样例（给 `flows/{code}/schema` 用）
- [x] 人民币大写和编号规则有检查
- [x] 排版设计器能预览审批详情的样子
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 审批中心的详情页（T-011）

## 交接记录

- 2026-10-15 · Codex · 第二轮复审的三项边界已修复：发布阶段静态校验函数参数数、点号字段与明细表并存时只在数组前缀中择最长、附件数组逐项拒绝空 ID。`docs/contracts.md` 同步规则，回归检查已加入。最终全量 `verify.ps1` 通过：后端 126、调用端 33、前端 65 项。再次交接独立验收。
- 2026-10-15 · Codex · 第二轮独立验收发现带字段引用的函数缺参数仍可发布、普通点号字段与明细路径并存时解析选错、附件数组空 ID 可绕过必填。继续修复三项并补检查；修完会重新运行全量验证和交接。
- 2026-10-15 · Codex · 完成受限表达式解析/求值、审批数据校验和 JSON 样例、人民币大写、编号规则，以及前端字段/节点权限/详情排版编辑组件和只读预览。复审时修复必填文本及附件样例、含点或连字符字段 key、平铺表达式深度，并在流程发布前校验表达式语法、引用与纯常量错误。`verify.ps1` 全量通过：后端 123、调用端 33、前端 65 项。排版预览组件待 T-008 工作台接入，业务系统实际发起与计算字段写回由后续开放接口任务承接；设计书语义未修改。
- 2026-10-15 · Codex · 独立验收发现发布校验仅核字段引用而不解析受限表达式，会使未知函数等无效条件通过发布。将 `FlowSemanticValidator.cs` 与回归场景明确纳入本任务范围；同时修复样例与求值语义后串行复验。此次扩围只接通已定义的受限表达式，不改流程契约。
- 2026-10-15 · Codex · T-001 已独立验收，认领审批数据与表达式。会修改共享文件 `docs/contracts.md`、`docs/architecture.md`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`、`frontend/tests/checks.ts`，分别登记表达式语义、模块落点与检查入口；已预先登记。前端排版组件和后端表达式解析按不同目录并行，设计器完整工作台属于 T-008。从 `main` 建任务分支并纳入已验收的 T-001，未合并主分支或推送。
<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

# T-001 流程定义 JSON 与执行图编译

状态：已完成
负责人：Codex 2026-10-15
里程碑：M1
依赖：—
设计书：§designer、§form、§engine

## 目标

定下流程定义的格式（自由画布设计 JSON、执行图、审批数据定义、详情排版、打印设置），实现自由画布编译成执行图和发布校验。设计器、引擎都在它上面做，是后面所有任务的基础。

## 范围

可以新建、修改的文件：

- `backend/src/ApprovalService.Contracts/Flows/`（新建）：流程定义模型，带格式版本号 `schemaVersion`；仅自由画布，不新增树形模型
- `backend/src/ApprovalService.Engine/Definitions/`（新建）：自由画布 → 执行图、发布校验、BPMN 2.0 导出和导入
- `backend/samples/flows/`（新建）：示例流程（采购申请、费用报销、质量异常），开发环境可以导入
- `frontend/src/model/flow.ts`（新建）：流程定义的前端副本
- `frontend/tests/checks.ts`：纯模型的 Node 场景检查
- `backend/tests/ApprovalService.Backend.Checks/FlowDefinitionChecks.cs`（新建）
- `backend/README.md`、`frontend/README.md`：补正式模型的使用说明

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `docs/contracts.md`：补“流程定义 JSON”一节
- `backend/src/ApprovalService.Contracts/**`
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：加一行调用
- `ContractChecks.cs`：核对前端副本的节点类型、字段类型和后端一致
- `docs/architecture.md`：补设计坐标与执行图分离的具体落点

## 验收标准

- [x] 模型覆盖设计书第 3、5 节：审批、办理、抄送、排他 / 包容 / 并行分支、子流程、自动节点；审批数据字段（含明细表、计算字段、摘要字段、节点字段权限、审批人填写的字段）、详情排版、打印设置
- [x] 自由画布可编译成执行图；设计坐标和运行语义分离，不实现树形模型或画法转换
- [x] 发布校验：每个节点从开始都能走到、也都能走到结束；并行网关分叉和汇合成对；排他、包容网关除兜底的线外都有条件；条件里用到的字段存在；审批节点有审批人来源。一次列出全部问题，中文说明
- [x] 自由画布导出为 BPMN 2.0 文件（带本系统的扩展信息）；只导入本系统导出的文件，别的来源给出明确的错误
- [x] 前端副本和后端模型一致，后端检查程序按文本核对
- [x] 三个示例流程都能编译、校验通过；每条校验规则都有检查
- [x] `verify.ps1` 通过（见 AGENTS.md）
- [x] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 设计器界面（T-008、T-009、T-010）
- 运行时（T-002）

## 交接记录

- 2026-10-15 · Codex（独立验收）· 逐项核对正式模型、发布静态校验、无坐标执行图、BPMN 导入导出、三份样例、前端副本和相关文档；未发现 T-001 范围内的阻塞问题。重新运行全量 `verify.ps1`，后端 100 项、调用端 33 项、前端 54 项及构建和格式检查均通过。确认验收完成；运行时表达式求值与设计器交互仍归后续任务。
- 2026-10-15 · Codex · 完成正式自由画布 JSON、无坐标执行图编译、发布静态校验、BPMN 2.0 导入导出、采购/报销/质量异常三份样例及跨端一致性检查。允许排他返工回路；并行和包容分支要求配对汇合。全量 `verify.ps1` 通过：后端 100 项、调用端 33 项、前端 54 项；构建与格式检查通过。设计书 §designer/§form/§engine 的既定行为未变，无需同步改设计本身。BPMN 扩展的 SHA-256 只校验完整性，不提供密码学来源认证；实际导入入口仍需授权和发布校验。未做设计器界面或运行时，留给 T-008/T-009/T-002。分支未合并主分支、未推送。
- 2026-10-15 · Codex · 认领并行开发。本任务会修改共享文件 `docs/contracts.md`、`docs/architecture.md`、`backend/src/ApprovalService.Contracts/Flows/**`、`backend/tests/ApprovalService.Backend.Checks/Program.cs`、`backend/tests/ApprovalService.Backend.Checks/ContractChecks.cs`，分别用于登记正式 JSON 契约和架构落点、接入场景检查、核对前端副本；修改会单独提交。以 `main` 建任务分支并纳入已验证的框架评审结果，尚未合并主分支或推送。
- 2026-10-13 · Codex · 按用户明确决定和 ADR 0007，将尚未认领的双设计器计划调整为统一自由画布；任务状态仍待认领。本轮只同步规划，不实现业务代码。

<!-- 按时间倒序追加：日期 · 谁 · 做了什么 / 卡在哪里 / 留给下一位的话 -->

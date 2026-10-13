# Copilot 说明

本仓库给所有 AI 助手的说明统一写在仓库根目录的 `AGENTS.md`，动手前请完整阅读。最重要的几条：

- 仓库分成 `frontend/`（流程设计器、审批中心、管理后台）、`backend/`（契约、引擎、服务端）、`client/`（.NET SDK、示例业务系统）三个部分，互不引用项目和文件。
- 每项工作对应 `docs/tasks/` 下的一个任务文件，只改任务范围内的文件。
- 审批服务不产生业务单据，发起都来自业务系统；对业务系统只提供数据接口，不提供界面组件（R14）。
- 契约（错误码、HTTP 头、流程定义 JSON）的权威定义在 `backend/src/ApprovalService.Contracts`；改契约要同时改副本和 `docs/contracts.md`。
- 后端依赖方向：Contracts ← Engine ← Domain ← Application ← Infrastructure ← Api；引擎核心不碰数据库、文件、网络。
- `client/src/ApprovalService.Client` 必须能编译到 net461，不依赖任何 NuGet 包。
- 注释和文档用中文，标识符和提交信息用英文。
- 交付前通过：`powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1`（只改一个部分可加 `-Part backend`、`-Part client` 或 `-Part frontend`）。

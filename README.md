# 审批流服务（ApprovalService）

独立部署的审批服务，是公司产品平台里的一个微服务（和标签服务并列）。

- **只管审批**：画审批流程（钉钉式的树形画法，或按 BPMN 画自由画布）、按流程流转、记录每个节点谁在什么时候怎么批的。中国式审批的规则尽量做全：会签、或签、票签、退回、加签、转交、委托、审批矩阵……（设计书第 4 节）。
- **不产生业务单据**：单据在业务系统里，所有审批都由业务系统通过接口发起；审批服务把审批要用的数据存一份快照。
- **业务系统少改造**：只提供数据接口（开放接口和 .NET SDK）。业务系统加一个“提交审批”按钮、给一个按单号查单据的接口就能接；登录后能拿到当前用户的待办数，跳转或内嵌审批中心的页面直接审批。
- **审批人在哪都能审**：审批中心的电脑网页和手机网页；结果回调给业务系统；审批单可以打印成 PDF。

设计书：[docs/design/approval-service-design.html](docs/design/approval-service-design.html)（用浏览器打开）。

## 仓库结构

| 部分 | 内容 |
| --- | --- |
| [frontend/](frontend/README.md) | 流程设计器、审批中心（电脑和手机）、管理后台：Vue 3 + TypeScript + Vite |
| [backend/](backend/README.md) | 服务端：.NET 10，契约、引擎核心、领域、应用、基础设施、接口六个项目 |
| [client/](client/README.md) | 给业务系统的 .NET SDK（兼容 .NET Framework 4.6.1），示例业务系统（对接类库 + WinUI 3 界面） |
| [docs/](docs/) | 设计书、架构、契约、架构决策（ADR）、任务 |

三个部分互不引用，只通过 HTTP 交互。技术栈和代码结构沿用标签服务，方便同一批人维护。

## 快速开始

需要 .NET SDK 10.0.100 以上、Node 22.18 以上。

```powershell
# 后端：http://localhost:5280
dotnet run --project backend/src/ApprovalService.Api
curl.exe http://localhost:5280/api/v1/info

# 前端：http://localhost:5273（接口请求转给 5280）
cd frontend; npm ci; npm run dev

# 全部验证（编译、格式、检查程序，含 SDK 对真实服务的端到端检查）
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1
```

## 开发方式

本仓库按任务协作开发，可能有多个 AI 助手同时参与：

- 规则：[AGENTS.md](AGENTS.md)（所有 AI 助手的共同入口）
- 任务：[docs/tasks/README.md](docs/tasks/README.md)
- 架构：[docs/architecture.md](docs/architecture.md)
- 契约：[docs/contracts.md](docs/contracts.md)
- 架构决策：[docs/adr/README.md](docs/adr/README.md)

当前状态：骨架已完成（项目结构、分层和依赖检查、注释检查、统一验证脚本、服务信息的最小链路），功能按任务逐个实现。

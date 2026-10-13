# 0001 技术栈和仓库结构沿用标签服务，后端多一个引擎核心项目

状态：已采纳（2026-10-13 用户确认）
日期：2026-10-13
关联：T-000

## 背景

审批流服务和标签服务是公司产品平台里并列的两个服务，以后由同一批人维护，最终要接入同一套统一登录、主数据、ESB。用户要求技术栈和代码结构沿用标签服务；数据库要支持多种，MVP 先用 SQLite。

## 决定

- 仓库分成 `frontend/`、`backend/`、`client/` 三个互不引用的部分，各自的解决方案和检查程序，根目录 `verify.ps1` 统一验证（和标签服务一样）。
- 后端 .NET 10、ASP.NET Core，按领域驱动设计分层，命名沿用标签服务（`XxxEntity`、`IXxxAppService`、`XxxDto`、`XxxEndpoints`）。和标签服务不同的是多一个**引擎核心**项目 `ApprovalService.Engine`，放在 Contracts 和 Domain 之间：Contracts ← Engine ← Domain ← Application ← Infrastructure ← Api。
- 数据库用 EF Core，MVP 用 SQLite；SQL Server、PostgreSQL 的迁移也生成，切换只改配置。
- 前端 Vue 3 + TypeScript + Vite，不用路由库，用地址的 # 部分区分页面；手机端是同一套前端里的 `#/m`。
- SDK 兼容 .NET Framework 4.6.1、不依赖任何包，JSON 读写沿用标签服务 SDK 的自带实现。
- 测试用各部分的检查程序（`check("中文描述", 条件)`），不用 xUnit；注释、分层、文件规模、契约一致性都由检查程序核对。
- 包版本和标签服务保持一致。

## 备选方案

- 引擎放进领域层：引擎是审批服务最核心、最需要反复测试的部分，单独一个项目更容易保证它不碰数据库、文件、网络（检查程序按项目核对），也方便预览、测试模式在内存里直接跑。
- 用路由库（vue-router）：标签服务没用，页面不多时 # 区分够用；以后页面多了再写 ADR。

## 影响

- 标签服务的检查程序（注释检查、源码登记检查、前端 JSDoc 检查）和 SDK 的 JSON 读写直接复制过来改名使用。
- 以后两个服务接入统一登录时，用同一种方式改。

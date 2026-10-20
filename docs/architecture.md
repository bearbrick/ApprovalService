# 架构

设计说明见设计书 [docs/design/approval-service-design.html](design/approval-service-design.html)（第 2、9、10 节），硬性规则见 [AGENTS.md](../AGENTS.md)。本文写代码怎么组织、新东西放哪。

## 总览

```
业务系统（Web、WinForms）──开放接口 / SDK──┐
审批人（电脑、手机网页）──审批中心 API────┤
流程管理员（浏览器）──管理 API──────────┤
                                       ▼
                         审批流服务（ASP.NET Core，单进程）
                         Api → Application → Domain → Engine
                                    ↓
                             Infrastructure：数据库、附件、消息、出站适配层、后台任务
                                    ↓
                     业务系统的回调、钩子接口；邮件（以后企业微信、钉钉）
```

仓库分成三个互不引用的部分：`frontend/`（网页）、`backend/`（服务端）、`client/`（SDK 和示例业务系统），只通过 HTTP 交互。

## 后端分层

依赖只能从上往下：Contracts ← Engine ← Domain ← Application ← Infrastructure ← Api。后端检查程序的 `ArchitectureChecks` 会核对项目引用和禁止的 using。

| 项目 | 职责 | 可以引用 | 不能碰 |
| --- | --- | --- | --- |
| `ApprovalService.Contracts` | 对外契约：接口路径、HTTP 头、错误码、请求和响应体、流程定义 JSON | 无 | 任何项目和包 |
| `ApprovalService.Engine` | 引擎核心：执行图、令牌、命令、事件、审批规则 | Contracts | 任何包；数据库、文件、网络、依赖注入、配置、日志 |
| `ApprovalService.Domain` | 领域层：实体（`XxxEntity`）、领域规则（`XxxRules`）、仓储接口（`IXxxRepository`）、`UserFriendlyException` | Contracts、Engine | 任何包；EF Core、ASP.NET Core |
| `ApprovalService.Application` | 应用层：应用服务（`IXxxAppService`）、输入输出模型（`XxxDto`）、对基础设施的抽象 | Contracts、Engine、Domain | EF Core、ASP.NET Core |
| `ApprovalService.Infrastructure` | 基础设施层：EF Core 和三种数据库、仓储实现、附件存储、消息渠道、出站适配层、后台任务 | 以上全部 | — |
| `ApprovalService.Api` | 接口层：路由（`XxxEndpoints`）、鉴权、错误格式、请求 ID、组装（`Program`） | Contracts、Domain、Application、Infrastructure | 直接访问数据库 |

新功能的落点：

- 流程怎么走、找谁审、几个人怎么审 → 引擎核心。
- 其他业务规则（委托是否生效、编号怎么生成……）→ 领域层。
- 用例（权限和范围检查、调引擎、存库、记操作日志、转成 Dto）→ 应用层的 `XxxAppService`，在 `AddApprovalApplication` 登记。
- 数据库、文件、邮件、HTTP 出站 → 基础设施层，在 `AddApprovalInfrastructure` 登记。
- 路由 → 接口层的 `XxxEndpoints`，处理函数一般一行：调应用服务。

## 业务模块

应用层、领域层、基础设施层都按同样的模块分文件夹：

| 模块 | 内容 | 设计书 |
| --- | --- | --- |
| `Flows` | 流程、流程版本、自由画布设计 JSON、编译成执行图、发布校验、导入导出 | 第 3、5 节 |
| `Runtime` | 审批单、任务、命令处理、操作记录、委托、审批矩阵、工作日历 | 第 4、9 节 |
| `Organization` | 部门、人员、角色、岗位职级、上下级、分管关系、业务系统绑定、推送变更 | 第 11 节 |
| `Identity` | 账号、登录、嵌入令牌、权限 | 第 11 节 |
| `Integration` | 接入应用、API Key、回调、钩子、出站适配层、待发队列 | 第 12 节 |
| `Notifications` | 消息渠道（邮件、站内）、提醒规则、待办汇总 | 第 6 节 |
| `Files` | 附件存储（本地、MinIO / S3）、权限检查、清理 | 第 9 节 |
| `Printing` | 审批单 PDF | 第 7 节 |
| `Reporting` | 统计汇总、报表 | 第 8 节 |
| `Services` | 服务自身的信息（骨架已有） | — |

模块之间只通过接口调用，以后哪个模块要拆成平台的公共服务（消息中心、文件服务、统一认证、主数据）都好拆。

## 引擎核心

引擎核心是单独的项目，只有流程执行的逻辑，预览流程、测试模式直接在内存里跑同一个引擎（设计书第 9 节）。

- **执行图**：自由画布发布时编译成执行图（节点、连线、网关），引擎只认执行图。
- **定义与布局分离**：正式设计 JSON 的字段、节点与连线模型定义在 Contracts 的 `Flows/`；编译和发布校验放在 Engine 的 `Definitions/`。设计坐标只用于前端编辑，执行图只保留流转语义。BPMN 导出携带本服务扩展信息，导入仅接收本服务的兼容版本。
- **令牌**：运行时用令牌标记走到哪。并行网关分叉生出几个令牌、汇合时等齐；包容网关只等真正走了的分支；回路、子流程用同一套机制。
- **命令和事件**：每个操作（同意、拒绝、退回、转交、加签、撤回、跳转……）是一条命令。引擎检查能不能做 → 算出新状态 → 产生事件；应用层把新状态和事件在一个事务里存下，事件写进操作记录（只追加、带校验值），回调、消息、统计都从事件来。
- **纯**：时间、ID、组织架构查询结果都由调用方传入，同样的输入一定得到同样的结果；不在引擎里调外部系统。
- **上限**：一张审批单最多推进 `EngineLimits.MaxStepsPerInstance` 步，子流程最多嵌套 `EngineLimits.MaxSubprocessDepth` 层。
- **检查**：设计书第 4 节需求清单每条至少一个场景检查；另外随机生成流程图和操作顺序反复跑，找卡死和重复推进。

## 扩展点

每个扩展点是一个接口，加新功能就是写一个实现、在登记处加一行，引擎核心不动（设计书第 10 节）。接口名是建议，实现对应任务时定：

| 扩展点 | 建议的接口 | 初版的实现 | 以后 |
| --- | --- | --- | --- |
| 节点类型 | `INodeBehavior` | 审批、办理、抄送、三种网关、子流程、自动节点 | 等待节点等 |
| 审批人来源 | `IAssigneeResolver` | 设计书第 4 节的 14 种 | 项目成员、HR 系统 |
| 多人审批方式 | `IApprovalPolicy` | 依次、会签、或签、票签 | 新的通过规则 |
| 条件和计算函数 | `IExpressionFunction` | SUM、AVG、MAX、MIN、IF、日期差 | 业务需要的新函数 |
| 自动节点动作 | `IServiceTaskAction` | 调业务系统的 HTTP 接口 | 经 ESB、发消息队列 |
| 消息渠道 | `INotificationChannel` | 邮件、站内消息 | 企业微信、钉钉、App 推送、短信 |
| 事件发布 | `IEventPublisher` | 回调 | 消息总线、ESB |
| 附件存储 | `IFileStorage` | 本地、MinIO / S3 | 平台文件服务 |
| 组织架构来源 | `IOrganizationSource` | 自己维护 + 业务系统推送 | 主数据系统 |
| 登录 | `IIdentityProvider` | 本地账号、嵌入令牌 | 公司统一登录（OIDC） |
| 打印 | `IApprovalPrinter` | 默认样式加配置 | 自定义打印模板 |

引擎相关的扩展点（前四个）定义在引擎核心里，其余定义在应用层、实现在基础设施层。

## 平台化预留

- 所有调外部系统的地方（回调、钩子、自动节点、消息）都经过出站适配层，现在直接发 HTTP，以后改配置走 ESB。
- 现在自己验 API Key 和登录令牌；以后有 API 网关时，可以改成信任网关传来的身份。
- 请求带进来的链路 ID 一路传下去（`RequestTraceMiddleware`，回调和钩子也带）。
- 进程不保存状态；后台任务按“抢锁”执行，换成 SQL Server 或 PostgreSQL 后可以多开实例。

## 数据

- MVP 用 SQLite（数据目录 `ApprovalService:Data:Directory`）；和标签服务一样用 EF Core，三种数据库的迁移都生成，不写某一种数据库特有的 SQL，切换只改配置（`DataOptions`）。
- 审批库和业务库分开：审批服务不连业务库，业务数据只通过接口进出，审批库里只存审批要用的快照。
- 主要数据表见设计书第 9 节。
- `Domain/Runtime` 定义流程、版本、审批单、任务投影、只追加事件、待发项和定时任务；`Infrastructure/Persistence` 为三种提供程序维护同一模型和各自迁移。SQLite 启动时迁移，数据库不可连接时就绪检查返回 503。
- `Application/Runtime` 从固定发布版本加载执行图，把引擎结果、任务投影、事件校验链与待发项交给仓储一次提交。审批单的 `Version` 是乐观锁；并发命令只允许一次推进，重试由调用方重新读取状态。
- `Infrastructure/Jobs` 通过数据库条件更新抢占有期限的租约；外部调用在数据库事务外进行。失败按退避表重新排期，进程重启后继续领取到期项目，处理器由集成模块登记。

## 前端

- `src/model/`：纯逻辑模块，Node 下能直接运行，`tests/checks.ts` 里检查。
- `src/api/`：调后端接口，统一的错误格式转成 `ApiError`。
- `src/pages/`：页面；`WorkspacePage` 提供尚未接入业务的工作区，不能把未接入误显示为无数据。后续按模块接入独立页面。
- `src/model/routes.ts`、`navigation.ts`：hash 路由与导航元数据；已登记路由严格匹配，未知地址显示未找到页面；手机端使用 `#/m` 独立外壳。导航只代表入口，不是权限控制。
- `src/components/layout/`：桌面/手机外壳，统一顶栏、侧栏、抽屉和底部导航；`src/components/ui/`：公共图标、空状态、原生对话框、子页面错误恢复边界。
- `src/composables/`：导航历史/滚动/焦点与服务连接生命周期；普通导航回到顶部，浏览器历史按条目恢复位置。服务信息重试取消旧请求，卸载后不再更新状态。
- `src/api/`：通用 JSON 请求负责 HTTP/字段错误、状态码/链路信息、超时/取消和响应解析；服务信息入口额外验证响应结构。只发一次请求，不自动重试业务写操作，不实现令牌或鉴权。
- `src/style.css`、`src/styles/`：沿用标签服务与原型的设计变量和公共样式；不从原型目录跨部分导入文件。
- `tests/`：路由、HTTP 和组件场景检查，加上注释与规模检查；Vue 自带编译器/渲染器仅用于检查，不新增运行依赖。
- 以后加：`src/designer/`（统一自由画布设计器）、`src/stores/`（业务状态）；当前不放演示业务数据。

## 调用端

- `src/ApprovalService.Client`：SDK，兼容 net461，不依赖任何包，协议常量是后端 Contracts 的副本。
- `samples/ApprovalService.Demo.Integration`：示例业务系统的对接类库（net48、net10.0），不依赖界面框架，业务系统照抄它。
- `samples/ApprovalService.Demo.WinUI`（待建）：示例业务系统的 WinUI 3 界面，只引用对接类库。

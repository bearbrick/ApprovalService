# T-011 审批中心（电脑网页）

状态：进行中
负责人：Codex 2026-10-23
里程碑：M3
依赖：T-004、T-005、T-007
设计书：§center、§rules

## 目标

审批人登录后在审批中心处理审批：列表、详情、全部审批操作、批量同意、委托设置、签名和常用语；能被业务系统内嵌（免登录）。

## 范围

可以新建、修改的文件：

- 后端审批中心接口（登录用户）和应用服务
- `backend/src/ApprovalService.Domain/Center/`、`Application/Center/`、`Infrastructure/Center/`：中心查询模型与授权仓储
- `backend/src/ApprovalService.Api/Endpoints/Center/`、`Contracts/Protocol/Center*.cs`：JWT 中心路由和向后兼容响应
- `backend/tests/ApprovalService.Backend.Checks/Center*.cs`：真实 SQLite/JWT/HTTP 检查
- `backend/README.md`、`docs/contracts.md`、`docs/architecture.md`、设计书 §center/§api：实际接口行为同步
- `frontend/src/pages/center/`：待我处理、我已处理、抄送我的、我发起的（只看进度）；详情（按排版显示业务数据，或嵌入业务系统的原单；意见栏；时间线加流程图高亮；操作按钮）
- 内嵌模式：用嵌入令牌打开，审批完用 postMessage 通知外面

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `frontend/src/App.vue`、`frontend/src/model/routes.ts`
- `backend/src/ApprovalService.Api/Program.cs`：仅登记中心查询路由
- `backend/src/ApprovalService.Application/ApplicationServiceCollectionExtensions.cs`、`backend/src/ApprovalService.Infrastructure/InfrastructureServiceCollectionExtensions.cs`：仅登记中心用例与仓储
- `backend/tests/ApprovalService.Backend.Checks/Program.cs`：登记中心检查
- `backend/src/ApprovalService.Contracts/Protocol/Center*.cs`、`docs/contracts.md`：新增中心查询协议
- `docs/architecture.md`：同步查询层安全边界

## 验收标准

- [x] 不能发起审批、不能改业务数据（R14）
- [ ] 列表能筛选、搜索，显示摘要字段，加急排前、未读加粗；批量同意（流程不允许时不出现）
- [ ] 第 4 节审批人能做的操作在详情页都能做；每个操作做完列表和角标数量刷新
- [ ] 在 iframe 里内嵌能用；只有登记过的网站能嵌入
- [x] `verify.ps1` 通过（当前后端只读切片，见交接记录）
- [x] 相关文档已更新（当前切片的 README、架构、契约、设计书）

## 不做

- 手机网页（T-012）
- 打印（T-015）

## 交接记录

- 2026-10-23 · Codex · 完成首个后端只读切片：新增 JWT `GET /api/v1/center/instances` 四列表和 `/{id}` 详情，人员只取认证声明，嵌入应用范围与参与关系先在 SQL 过滤，再筛选、计数、去重、排序和分页；详情不存在或不可见统一 404。待办覆盖当前代理人及原审批人，已办/抄送复用 T-043 可信参与投影，保密查询不给无关管理员或未来/取消任务自动授权。固定版本摘要和详情递归裁剪隐藏字段及明细列，不返回未定义键、运行状态、原始审计字段或私人暂存意见；历史参与者不能借 future queued 节点放宽字段权限。新增 20 项真实 SQLite/JWT/HTTP 及仓储边界检查，最终全量 `verify.ps1` 通过：后端 728、调用端 50（含真实服务端到端）、前端 120 项，构建零警告；三库模型一致性通过，未连接真实 SQL Server/PostgreSQL。同步契约、后端 README、架构及设计书 §center/§api，只描述已实现切片；没有新增依赖，初次工作树还原均用本机 NuGet/npm 缓存。下一步接前端登录/刷新/嵌入兑换与中心 adapter、读标记、字段搜索、排版/流程图、操作及批量审批、个人设置、iframe 允许来源和 postMessage；额外查看人/管理查询未在本入口开放。没有新增 SDK 或前端协议副本，因为中心客户端尚未接线；前端不持有 API Key。任务保持进行中，未合并 main、未推送。
- 2026-10-23 · Codex · 为四列表和保密详情复用持久参与者，合入 T-043 固定已验证提交 `b3e3e8e`。唯一合并冲突为架构文档相邻段落，保留 T-044 已实现取人/自动动作说明，同时保留 T-043 参与者和附件可见性说明；不更改两者实现。当前代理原审批人依据设计书仍可查看和处理 pending 任务，已与 T-043 验证口径统一。
- 2026-10-23 · Codex · 从 main 创建独立工作树和任务分支，认领后合入已验收 T-007 的稳定快照 `537c200`（包含 T-004、T-005），本轮先实现审批中心 JWT 只读四列表与详情。预登记范围：新增 Domain/Application/Infrastructure 的 Center 查询模块、Api/Endpoints/Center、Contracts/Protocol 的中心路由与响应、后端中心场景检查；共享登记涉及 Api/Program.cs、Application 与 Infrastructure 的 ServiceCollectionExtensions、检查 Program.cs、docs/contracts.md、docs/architecture.md；同步 backend/README.md 与设计书 §center/§api。不修改 Runtime/Persistence 实现，与 T-043 协调参与者投影，后续接入稳定版本。前端和审批写操作留待后续切片，不向前端提供 API Key。

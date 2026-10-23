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
- `frontend/src/pages/center/`：待我处理、我已处理、抄送我的、我发起的（只看进度）；详情（按排版显示业务数据，或嵌入业务系统的原单；意见栏；时间线加流程图高亮；操作按钮）
- 内嵌模式：用嵌入令牌打开，审批完用 postMessage 通知外面

## 共享文件

改这些文件前先在交接记录里写明改什么；改动尽量小，单独成一个提交：

- `frontend/src/App.vue`、`frontend/src/model/routes.ts`
- 登记文件

## 验收标准

- [ ] 不能发起审批、不能改业务数据（R14）
- [ ] 列表能筛选、搜索，显示摘要字段，加急排前、未读加粗；批量同意（流程不允许时不出现）
- [ ] 第 4 节审批人能做的操作在详情页都能做；每个操作做完列表和角标数量刷新
- [ ] 在 iframe 里内嵌能用；只有登记过的网站能嵌入
- [ ] `verify.ps1` 通过（见 AGENTS.md）
- [ ] 相关文档已更新（README、架构、契约、设计书受影响的部分）

## 不做

- 手机网页（T-012）
- 打印（T-015）

## 交接记录

- 2026-10-23 · Codex · 从 main 创建独立工作树和任务分支，认领后合入已验收 T-007 的稳定快照 `537c200`（包含 T-004、T-005），本轮先实现审批中心 JWT 只读四列表与详情。预登记范围：新增 Domain/Application/Infrastructure 的 Center 查询模块、Api/Endpoints/Center、Contracts/Protocol 的中心路由与响应、后端中心场景检查；共享登记涉及 Api/Program.cs、Application 与 Infrastructure 的 ServiceCollectionExtensions、检查 Program.cs、docs/contracts.md、docs/architecture.md；同步 backend/README.md 与设计书 §center/§api。不修改 Runtime/Persistence 实现，与 T-043 协调参与者投影，后续接入稳定版本。前端和审批写操作留待后续切片，不向前端提供 API Key。

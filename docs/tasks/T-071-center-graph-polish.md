# T-071 审批中心 BPMN 进度图精修

状态：进行中
负责人：Codex 2026-10-26
里程碑：M3
依赖：T-011 已集成只读进度图切片
设计书：§center、§designer

## 目标
完整居中显示已授权 BPMN 进度图，改善分支与回路的正交走线。

## 范围
- `frontend/src/pages/center/CenterProgressGraph.vue`
- `frontend/src/pages/center/center-progress-diagram.ts`
- `frontend/tests/center-checks.ts`
- 本任务文件

## 共享文件
无。

## 验收标准
- [ ] 完整图居中留白，容器尺寸变化后重新适配。
- [ ] 顺序、分支、回路、自环有正交走线场景检查。
- [ ] 保持服务端安全投影、BPMN Viewer 与 bpmn.io 标识。
- [ ] 前端 check/build 通过，根任务执行全量 verify 与 Chrome 验收。

## 不做
不改契约、详情页、后端和依赖，不重新设计流程。

## 交接记录
- 2026-10-26 · Codex · 按根任务授权从最新集成分支建立独立工作树，登记上述范围；复用本机既有 node_modules，不下载包。浏览器视觉验收由根任务使用外置 Chrome 完成。

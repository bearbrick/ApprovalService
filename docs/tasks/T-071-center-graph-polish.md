# T-071 审批中心 BPMN 进度图精修

状态：待验收
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
- [x] 完整图居中留白，容器尺寸变化后重新适配。
- [x] 顺序、分支、回路、自环有正交走线场景检查。
- [x] 保持服务端安全投影、BPMN Viewer 与 bpmn.io 标识。
- [x] 前端 check/build 通过，根任务执行全量 verify 与 Chrome 验收。

## 不做
不改契约、详情页、后端和依赖，不重新设计流程。

## 交接记录
- 2026-10-26 · Codex · 完成完整图居中与 15% 留白、容器 ResizeObserver 适配；保留现有 BPMN Viewer 和 bpmn.io 标识。分叉/汇合采用上下端口，回路走整个安全子图下方的独立通道，自环不再退化，直线消除重复点与共线折点；空投影清空旧图。新增五项几何场景，覆盖分支/汇合、回路/自环、直线、垂直流转和正交性。verify.ps1 -Part frontend 通过（222 项检查、类型检查、生产构建）；保留既有打包体积提示。纯前端实现无契约或依赖变化；未改设计书，展示行为仍符合 §center。根任务整合后执行全量验证和外置 Chrome 视觉验收。复杂任意节点布局仍沿用发布坐标，本切片不实现全局自动重排或通用避障算法。
- 2026-10-26 · Codex · 按根任务授权从最新集成分支建立独立工作树，登记上述范围；复用本机既有 node_modules，不下载包。浏览器视觉验收由根任务使用外置 Chrome 完成。

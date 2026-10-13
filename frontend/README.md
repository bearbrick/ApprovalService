# 前端（frontend/）

审批流服务的网页：流程设计器、审批中心（电脑和手机）、管理后台。Vue 3 + TypeScript + Vite，规则见根目录 [AGENTS.md](../AGENTS.md) §5。

## 页面

用地址的 # 部分区分页面（`src/model/routes.ts`）：

| 地址 | 页面 |
| --- | --- |
| `#/` | 审批中心（给审批人用；不能发起审批、不能改业务数据） |
| `#/designer` | 流程设计：钉钉式、自由画布，审批数据定义和详情排版 |
| `#/admin` | 管理后台：流程、组织架构、接入应用、日志、统计、系统信息 |
| `#/m` | 手机端审批中心：按手机单独设计的布局，底部四个标签 |

骨架阶段每个页面只有占位说明，顶栏右侧显示和服务端的连接状态（调 `GET /api/v1/info`）。

## 命令

```powershell
npm ci
npm run dev       # http://localhost:5273，/api 转给 http://localhost:5280
npm run check     # 检查程序：纯逻辑模块、注释（tests/docs.ts）、文件规模
npm run build     # vue-tsc 类型检查 + 打包
```

## 目录

| 目录 | 内容 |
| --- | --- |
| `src/model/` | 纯逻辑模块，Node 下能直接运行，在 `tests/checks.ts` 里检查 |
| `src/api/` | 调后端接口 |
| `src/pages/` | 页面 |
| `tests/` | 检查程序 |

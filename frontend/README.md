# 前端（frontend/）

审批流服务的网页：流程设计器、审批中心（电脑和手机）、管理后台。Vue 3 + TypeScript + Vite，规则见根目录 [AGENTS.md](../AGENTS.md) §5。

## 页面

用地址的 # 部分区分页面（`src/model/routes.ts`）：

| 地址 | 页面 |
| --- | --- |
| `#/` | 审批中心（给审批人用；不能发起审批、不能改业务数据） |
| `#/designer` | 流程设计：统一自由画布，审批数据定义和详情排版 |
| `#/admin` | 管理后台：流程、组织架构、接入应用、日志、统计、系统信息 |
| `#/m` | 手机端审批中心：按手机单独设计的布局，底部四个标签 |

公共框架已具备桌面顶栏/侧栏、手机底部导航、二级页签、空状态、对话框、错误恢复和服务连接状态。顶栏切换工作区，侧栏展示当前工作区的子入口；审批中心不在正文重复列出侧栏入口。当前业务页面明确显示“功能尚未接入”，不读取审批业务数据，不提供假登录、假统计或原型样例。

中心子页面使用 `#/center/done`、`#/center/cc`、`#/center/started`、`#/center/settings`；手机使用 `#/m/done`、`#/m/cc`、`#/m/me`。详情地址预留为 `#/center/approvals/{id}` 和 `#/m/approvals/{id}`，只解析参数，不查询审批单。

管理入口见 `src/model/navigation.ts`，例如 `#/admin/org/roles`、`#/admin/outbound/queue`。设计器工作区预留 `#/designer/{flowCode}/{tab}`，支持 design（自由画布）、fields、layout、rules、print、versions。未知页面显示明确的返回入口，手机未知路径仍使用手机外壳。

正式自由画布流程定义的 TypeScript 副本位于 `src/model/flow.ts`，供后续设计器使用；字段与后端 Contracts 保持一致。当前页面尚未接入流程设计、发布或审批业务接口。

自由画布组件位于 `src/designer/canvas/FlowCanvasEditor.vue`。工作台传入 `v-model: FlowDefinition`，可用 `issues` 标出服务端发布问题，监听 `select` 获取选中节点或连线，并通过 `inspector` 插槽接管属性面板；`check()`、`focusElement(id)` 等公开方法可用于检查列表定位。独立使用时组件自己维护撤销栈；工作台传入 `history-mode="external"` 后，每次完整编辑通过 `editCommitted(previousDefinition)` 提交旧快照，撤销与重做按钮和快捷键分别发 `undo`、`redo` 事件，由工作台统一管理历史。节点拖动只在松开时提交一次历史，拖动中的 `update:modelValue` 只更新当前画面。组件内可以拖节点、连任意有向线、编辑分支条件、撤销重做、缩放平移和网格吸附。BPMN 导出包含标准图形与本服务完整扩展，导入只接受版本兼容且图形、摘要都一致的本服务文件。前端静态检查镜像后端的图结构、字段引用和受限表达式语法规则，并对纯常量表达式提前检查除零等可求值错误；最终发布仍由后端再次校验。草稿持久化与完整审批属性由工作台负责。

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
| `src/designer/canvas/` | SVG 自由画布、图操作、发布静态校验镜像和 BPMN 文件处理 |
| `src/api/` | 调后端接口 |
| `src/pages/` | 页面 |
| `src/components/layout/` | 桌面/手机外壳，不承载业务数据 |
| `src/components/ui/` | 图标、空状态、原生模态框、错误恢复边界 |
| `src/composables/` | 路由历史与服务连接的生命周期 |
| `src/styles/` | 公共组件及布局样式；设计变量在 style.css |
| `public/branding/` | 复用已有单位品牌图片，不打包系统字体 |
| `tests/` | 检查程序 |

## 后续模块接入方式

1. 在 `model/navigation.ts` 登记入口，在 `model/routes.ts` 明确匹配规则和参数；不要把未知地址悄悄当作首页。
2. 业务页面放进 `pages/` 下对应模块，并在 `App.vue` 的错误边界内选择页面。布局只收页面元数据，不查业务数据，不以导航隐藏替代权限校验。
3. 使用 `requestJson<T>(path, options)` 发送一次 JSON 请求；`getJson` 是 GET 兼容入口。方法、JSON body、headers、timeoutMs、signal 均由调用方传入。不自动重试写操作，不存储凭据。成功响应的结构由各接口入口校验，通用层只保证 JSON 可解析；204 接口应声明 void。
4. `ApiError` 保留 code、message、status、errors、traceId、requestId、kind。取消、超时、网络和无效响应是前端分类；错误码不回写或冒充新服务端契约。页面显示持久错误和明确重试入口。
5. `useServiceConnection` 只请求已有 `/api/v1/info`，默认十五秒超时。重试取消旧请求，旧响应不能覆盖新状态，卸载后取消请求。它表示服务信息可读取，不代表数据库、登录或审批业务就绪。
6. `UiDialog` 由 open 和 close 事件控制，正文与 footer 使用插槽；异步保存的 loading、错误保留和防重复提交由使用方维护。原生 dialog 管理模态焦点，关闭恢复触发元素。
7. `UiErrorBoundary` 用于渲染/生命周期错误恢复，重试会重新挂载子页面；请求异常应由页面显式处理，不以错误边界替代业务错误处理。

普通导航滚动到顶部并聚焦页面标题；前进后退按独立历史条目恢复滚动。桌面窄屏侧栏使用模态导航，隐藏侧栏不占键盘焦点；手机页独立布局且支持底部安全区。

## 检查与边界

`npm run check` 包含路由、HTTP/连接生命周期、对话框/错误边界场景和源码规范；使用已安装的 Vue 编译器与最小渲染宿主，无新增测试依赖。宿主检查不替代浏览器：桌面/手机导航、真实服务连接、ESC 焦点恢复、刷新和前进后退另做浏览器验收，截图位于 `tests/evidence/`。

开发时先在仓库根目录运行 `dotnet run --project backend/src/ApprovalService.Api`，再在本目录运行 `npm run dev`。没有启动后端时连接状态显示失败，可在“服务连接”中重试；其他框架导航仍可使用。

未实现流程引擎、审批动作、登录令牌、权限、业务表单、数据库持久化。高保真原型仍在独立的原型任务分支，本任务从 main 建立，不会把原型的 localStorage 模拟逻辑搬进产品。

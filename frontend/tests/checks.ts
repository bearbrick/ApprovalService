/**
 * 前端检查程序：用 Node 直接运行（类型擦除），检查不依赖浏览器的模块和源码规范。任何一项失败抛异常，退出码非 0。
 * 运行：npm run check。新增 src/model、src/editor 里的纯逻辑模块时在这里补检查（AGENTS.md §5）。
 */
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { NAV_ITEMS, parseRoute, resolveRoute } from '../src/model/routes.ts'
import { flowFieldTypes, flowNodeTypes, flowSchemaVersion } from '../src/model/flow.ts'
import { ADMIN_TABS, DESIGNER_TABS, SECTION_ITEMS } from '../src/model/navigation.ts'
import { runHttpChecks } from './http-checks.ts'
import { runDataChecks } from './data-checks.ts'
import { runUiChecks } from './ui-checks.ts'
import { checkDocs, sourceFiles } from './docs.ts'

/**
 * 通过的检查项数。
 */
let passed = 0

/**
 * 记录一项检查：通过时打印 PASS，失败时抛异常结束。
 * @param name 检查的中文描述。
 * @param value 是否通过。
 */
function check(name: string, value: boolean): void {
  if (!value) throw new Error('FAIL: ' + name)
  console.log('PASS: ' + name)
  passed++
}

/**
 * 页面路由。
 */
function checkRoutes(): void {
  check('默认地址进入审批中心，未知地址明确返回未找到页面', parseRoute('') === 'center' && parseRoute('#/') === 'center' && parseRoute('#/unknown') === 'not-found')
  check('#/designer、#/admin、#/m 开头分别进流程设计、管理后台、手机端',
    parseRoute('#/designer/PURCHASE_REQUEST') === 'designer' && parseRoute('#/admin?tab=org') === 'admin' && parseRoute('#/m/approvals/42') === 'mobile')
  check('顶栏导航项的地址都能解析回自己的页面', NAV_ITEMS.every((item) => parseRoute(item.hash) === item.key))
  check('全部子导航地址都指向登记的页面和分组', Object.entries(SECTION_ITEMS).every(([page, items]) => items.every(item => {
    const route = resolveRoute(item.hash)
    return route.page === page && route.section === item.key
  })))
  check('管理页签可直接打开', Object.entries(ADMIN_TABS).every(([section, tabs]) => tabs.every(tab => {
    const route = resolveRoute(`#/admin/${section}/${tab}`)
    return route.page === 'admin' && route.tab === tab
  })))
  check('流程参数安全解码且六个页签保留流程编码', DESIGNER_TABS.every(tab => {
    const route = resolveRoute(`#/designer/%E9%87%87%E8%B4%AD/${tab}`)
    return route.resourceId === '采购' && route.tab === tab
  }))
  check('审批详情保留编号，手机详情使用独立外壳', resolveRoute('#/center/approvals/id-1').resourceId === 'id-1' && resolveRoute('#/m/approvals/id-2').mobile)
  check('未知子路由、损坏编码和多余路径不会静默回到首页', ['#/admin/nope', '#/m/wrong', '#/designer/A/wrong', '#/center/done/extra', '#/designer/%FF', '#/designer/A%2FB', '#/admin//org'].every(hash => resolveRoute(hash).page === 'not-found'))
  check('手机未知地址仍保留手机外壳', resolveRoute('#/m/wrong').mobile)
  check('查询参数与末尾斜线不影响合法路由', resolveRoute('#/admin/org/roles/?view=all').tab === 'roles')
}

/**
 * 核对正式流程模型的基础约束，避免设计器以后误用旧树形模型或重复的种类编码。
 */
function checkFlowModel(): void {
  const nodes = Object.values(flowNodeTypes)
  const fields = Object.values(flowFieldTypes)
  check('正式流程格式版本已登记且节点种类各不相同',
    flowSchemaVersion === 1 && nodes.length === new Set(nodes).size)
  check('自由画布包含审批、办理、抄送、三类网关和自动节点',
    ['approval', 'handling', 'cc', 'exclusiveGateway', 'inclusiveGateway', 'parallelGateway', 'automatic'].every(type => nodes.includes(type as typeof nodes[number])))
  check('审批数据支持明细表、金额、人员、部门与附件且字段编码唯一',
    ['detailTable', 'money', 'person', 'department', 'attachment'].every(type => fields.includes(type as typeof fields[number]))
    && fields.length === new Set(fields).size)
}

/**
 * 源码规范：注释完整（tests/docs.ts）、文件不超过 500 行。
 */
function checkSource(): void {
  /**
   * frontend 目录。
   */
  const frontendRoot = join(fileURLToPath(new URL('.', import.meta.url)), '..')
  const docProblems = checkDocs(frontendRoot)
  check(`模块级声明都有完整的 JSDoc（三行说明、@param、@returns）${docProblems.length > 0 ? `（${docProblems.length} 处）：\n  ` + docProblems.slice(0, 40).join('\n  ') : ''}`, docProblems.length === 0)

  /**
   * 超过 500 行的源码文件（AGENTS.md R11）。
   */
  const largeFiles = sourceFiles(join(frontendRoot, 'src')).filter((path) => readFileSync(path, 'utf8').split('\n').length > 500)
  check(`src 下的源码文件都不超过 500 行${largeFiles.length > 0 ? '：' + largeFiles.join('、') : ''}`, largeFiles.length === 0)
}

checkRoutes()
checkFlowModel()
runDataChecks(check)
await runHttpChecks(check)
await runUiChecks(check)
checkSource()
console.log(`前端检查全部通过：${passed} 项`)

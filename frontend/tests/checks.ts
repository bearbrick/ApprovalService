/**
 * 前端检查程序：用 Node 直接运行（类型擦除），检查不依赖浏览器的模块和源码规范。任何一项失败抛异常，退出码非 0。
 * 运行：npm run check。新增 src/model、src/editor 里的纯逻辑模块时在这里补检查（AGENTS.md §5）。
 */
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { NAV_ITEMS, parseRoute } from '../src/model/routes.ts'
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
  check('空地址和不认识的地址都进审批中心', parseRoute('') === 'center' && parseRoute('#/') === 'center' && parseRoute('#/unknown') === 'center')
  check('#/designer、#/admin、#/m 开头分别进流程设计、管理后台、手机端',
    parseRoute('#/designer/PURCHASE_REQUEST') === 'designer' && parseRoute('#/admin?tab=org') === 'admin' && parseRoute('#/m/approvals/42') === 'mobile')
  check('顶栏导航项的地址都能解析回自己的页面', NAV_ITEMS.every((item) => parseRoute(item.hash) === item.key))
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
checkSource()
console.log(`前端检查全部通过：${passed} 项`)

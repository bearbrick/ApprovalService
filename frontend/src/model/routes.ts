/**
 * 页面路由：用地址的 # 部分区分页面（不依赖路由库，和标签服务一致）。纯逻辑，Node 下能直接运行，检查程序会核对。
 */

/**
 * 页面。
 */
export type PageKey = 'center' | 'designer' | 'admin' | 'mobile'

/**
 * 顶栏上的一个导航项。
 */
export interface NavItem {
  /**
   * 对应的页面。
   */
  key: PageKey
  /**
   * 显示的名称。
   */
  title: string
  /**
   * 地址的 # 部分，如 #/designer。
   */
  hash: string
}

/**
 * 电脑端顶栏的导航项，按显示顺序。手机端（#/m）有自己的底部标签，不在这里。
 */
export const NAV_ITEMS: readonly NavItem[] = [
  { key: 'center', title: '审批中心', hash: '#/' },
  { key: 'designer', title: '流程设计', hash: '#/designer' },
  { key: 'admin', title: '管理后台', hash: '#/admin' },
]

/**
 * 按地址的 # 部分找页面：#/designer 开头是流程设计，#/admin 开头是管理后台，#/m 开头是手机端，其余（包括空的）是审批中心。
 * @param hash location.hash，如 #/designer/PURCHASE_REQUEST。
 * @returns 页面。
 */
export function parseRoute(hash: string): PageKey {
  const path = hash.replace(/^#/, '').replace(/^\/+/, '')
  const first = path.split(/[/?]/)[0]
  if (first === 'designer') return 'designer'
  if (first === 'admin') return 'admin'
  if (first === 'm') return 'mobile'
  return 'center'
}

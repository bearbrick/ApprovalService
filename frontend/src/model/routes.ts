import { ADMIN_TABS, DESIGNER_TABS, SECTION_ITEMS, TAB_TITLES } from './navigation.ts'
import type { WorkspaceKey } from './navigation.ts'

/**
 * 已知工作区与未找到页面。
 */
export type PageKey = WorkspaceKey | 'not-found'

/**
 * 完整的界面路由，不包含鉴权或业务数据。
 */
export interface AppRoute {
  /**
   * 页面工作区。
   */
  page: PageKey
  /**
   * 原始地址，供历史记录和页面重置使用。
   */
  hash: string
  /**
   * 当前子导航标识。
   */
  section: string
  /**
   * 当前二级页签。
   */
  tab: string
  /**
   * 流程编码或审批单标识，只作路由参数。
   */
  resourceId: string
  /**
   * 页面标题。
   */
  title: string
  /**
   * 页面用途说明。
   */
  description: string
  /**
   * 是否保留手机外壳，包括手机未知页面。
   */
  mobile: boolean
}

/**
 * 桌面顶栏的主导航。
 */
export const NAV_ITEMS = [
  { key: 'center', title: '审批中心', hash: '#/' },
  { key: 'designer', title: '流程设计', hash: '#/designer' },
  { key: 'admin', title: '管理后台', hash: '#/admin' },
] as const

/**
 * 解析 hash 地址，严格匹配已登记路径并安全解码参数。
 * @param hash 浏览器地址的 hash 部分。
 * @returns 页面元数据；未知或编码损坏时返回未找到页面。
 */
export function resolveRoute(hash: string): AppRoute {
  const raw = hash.replace(/^#/, '').split('?')[0]!.replace(/^\/+|\/+$/g, '')
  const missing: AppRoute = { page: 'not-found', hash, section: '', tab: '', resourceId: '', title: '页面不存在', description: '地址可能已变更，请通过导航选择页面。', mobile: raw === 'm' || raw.startsWith('m/') }
  let parts: string[]
  try {
    parts = raw ? raw.split('/').map(decodeURIComponent) : []
    // 编码后的斜线和控制字符不能改变路径边界。
    if (parts.some(part => !part || /[\/\\\u0000-\u001f]/.test(part))) return missing
  } catch {
    return missing
  }
  const first = parts[0] ?? 'center'
  const page: WorkspaceKey | undefined = first === 'm' ? 'mobile' : first === 'center' || first === 'designer' || first === 'admin' ? first : undefined
  if (!page) return missing
  const section = parts[1] ?? SECTION_ITEMS[page][0]!.key
  if ((page === 'center' || page === 'mobile') && section === 'approvals' && parts.length === 3) {
    return { ...missing, page, section: 'todo', resourceId: parts[2]!, title: '审批详情', description: '审批数据与操作记录将在业务模块接入后展示。' }
  }
  const item = SECTION_ITEMS[page].find(candidate => candidate.key === section)
  if (!item && page === 'designer' && parts.length >= 2 && parts.length <= 3) {
    const tab = parts[2] ?? 'design'
    if (!(DESIGNER_TABS as readonly string[]).includes(tab)) return missing
    return { ...missing, page, section: 'flows', tab, resourceId: section, title: '流程工作区', description: '流程设计、数据与展示配置的工作区。' }
  }
  if (!item || parts.length > (page === 'admin' ? 3 : 2)) return missing
  const tab = parts[2] ?? (page === 'admin' ? ADMIN_TABS[section]?.[0] ?? '' : '')
  if (tab && !ADMIN_TABS[section]?.includes(tab)) return missing
  return { ...missing, page, section, tab, title: tab ? TAB_TITLES[tab] ?? item.title : item.title, description: item.description }
}

/**
 * 兼容骨架已有的工作区解析入口。
 * @param hash 浏览器地址的 hash 部分。
 * @returns 页面工作区。
 */
export function parseRoute(hash: string): PageKey {
  return resolveRoute(hash).page
}

import { ADMIN_VIEWS, adminCellText, filterAdminRows, resolveAdminView } from '../src/model/admin.ts'
import type { AdminRow } from '../src/model/admin.ts'
import { ADMIN_TABS } from '../src/model/navigation.ts'
import { adminAdapter, AdminAdapterUnavailableError } from '../src/api/admin-adapter.ts'

/**
 * 检查管理员页面的路径覆盖、安全列与未接入边界。
 * @param check 统一的场景断言入口。
 * @returns 场景执行完成。
 */
export async function runAdminChecks(check: (name: string, value: boolean) => void): Promise<void> {
  check('管理后台所有既有一级入口都有正式页面定义',
    ['overview', 'flows', 'runtime'].every(section => !!resolveAdminView(section, '')))
  check('组织、应用、出站、统计和系统管理的全部页签都有页面定义',
    Object.entries(ADMIN_TABS).every(([section, tabs]) => tabs.every(tab => !!resolveAdminView(section, tab))))
  check('管理页面编码唯一且列不登记密码、密钥或连接串',
    new Set(ADMIN_VIEWS.map(view => view.key)).size === ADMIN_VIEWS.length
    && ADMIN_VIEWS.every(view => view.columns.every(column => !/(password|secret|apiKey|connectionString|privateKey)/i.test(column.key))))
  const rows: AdminRow[] = [
    { id: 'one', cells: { name: '采购流程', code: 'PURCHASE', hiddenSecret: 'private-token' } },
    { id: 'two', cells: { name: '费用流程', code: 'EXPENSE', hiddenSecret: 'other-token' } },
  ]
  const columns = [{ key: 'name', title: '名称' }, { key: 'code', title: '编码' }]
  check('管理搜索只匹配可见且已授权的列，不能通过隐藏字段命中',
    filterAdminRows(rows, columns, '采购').map(row => row.id).join(',') === 'one'
    && filterAdminRows(rows, columns, 'private-token').length === 0
    && rows.length === 2)
  check('管理表格未定义的列用占位符，不回退到原始行对象', adminCellText(rows[0]!, { key: 'status', title: '状态' }) === '—')
  let unavailable = false
  try { await adminAdapter.loadView('apps/list', new AbortController().signal) } catch (error) { unavailable = error instanceof AdminAdapterUnavailableError }
  check('未接入的管理页面明确拒绝，不返回虚构统计或密钥', unavailable)
}

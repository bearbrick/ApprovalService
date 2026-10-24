import { getJson } from './http.ts'
import { adminAccessToken } from './admin-session.ts'

/**
 * 管理报表的本地时间与维度筛选。
 */
export interface ReportFilter {
  /**
   * 本地时间下界，包含。
   */
  fromLocal: string
  /**
   * 本地时间上界，不包含。
   */
  untilLocal: string
  /**
   * 可选流程编码。
   */
  flowCode: string
  /**
   * 可选来源应用编码。
   */
  clientAppCode: string
  /**
   * 可选发起部门快照 ID。
   */
  departmentId: string
}

/**
 * 报表概览，保密审批仅有匿名数量。
 */
export interface ReportOverview {
  /**
   * 公开流程发起量。
   */
  visibleStartedCount: number
  /**
   * 公开流程结束量。
   */
  visibleCompletedCount: number
  /**
   * 公开流程当前待办数。
   */
  visiblePendingCount: number
  /**
   * 公开流程已到期任务中的逾期数。
   */
  visibleOverdueCount: number
  /**
   * 保密流程匿名发起量。
   */
  confidentialStartedCount: number
  /**
   * 保密流程匿名结束量。
   */
  confidentialCompletedCount: number
}

/**
 * 单流程可见指标。
 */
export interface ReportFlow {
  /**
   * 流程编码。
   */
  flowCode: string
  /**
   * 发起量。
   */
  startedCount: number
  /**
   * 结束量。
   */
  completedCount: number
  /**
   * 通过量。
   */
  approvedCount: number
  /**
   * 拒绝量。
   */
  rejectedCount: number
  /**
   * 撤回量。
   */
  withdrawnCount: number
  /**
   * 平均历时分钟数；无样本时为空。
   */
  averageDurationMinutes?: number | null
  /**
   * P90 历时分钟数；无样本时为空。
   */
  p90DurationMinutes?: number | null
}

/**
 * 单节点可见指标。
 */
export interface ReportNode {
  /**
   * 流程编码。
   */
  flowCode: string
  /**
   * 节点 ID。
   */
  nodeId: string
  /**
   * 完成任务数。
   */
  completedCount: number
  /**
   * 平均处理分钟数；无样本时为空。
   */
  averageDurationMinutes?: number | null
  /**
   * 已到期任务数。
   */
  dueCount: number
  /**
   * 其中逾期任务数。
   */
  overdueCount: number
}

/**
 * 单人员可见指标。
 */
export interface ReportPerson {
  /**
   * 实际处理人的内部人员 ID。
   */
  assigneeId: string
  /**
   * 当前待办数。
   */
  pendingCount: number
  /**
   * 完成任务数。
   */
  completedCount: number
  /**
   * 平均处理分钟数；无样本时为空。
   */
  averageDurationMinutes?: number | null
  /**
   * 已到期任务中的逾期数。
   */
  overdueCount: number
}

/**
 * 四条接口合并后的完整可信响应。
 */
export interface ReportBundle {
  /**
   * 概览。
   */
  overview: ReportOverview
  /**
   * 非保密流程。
   */
  flows: ReportFlow[]
  /**
   * 非保密节点。
   */
  nodes: ReportNode[]
  /**
   * 非保密人员。
   */
  people: ReportPerson[]
}

/**
 * 把用户所选本地时间转换成服务端要求的 UTC 半开区间。
 * @param filter 日期与维度筛选。
 * @returns 统一的 URL 查询字符串。
 */
export function reportQuery(filter: ReportFilter): string {
  const from = new Date(filter.fromLocal).getTime()
  const until = new Date(filter.untilLocal).getTime()
  if (!Number.isFinite(from) || !Number.isFinite(until) || from >= until) {
    throw new Error('请选择有效的时间范围；结束时间必须晚于开始时间。')
  }
  const query = new URLSearchParams({ fromUtc: new Date(from).toISOString(), untilUtc: new Date(until).toISOString() })
  if (filter.flowCode.trim()) query.set('flowCode', filter.flowCode.trim())
  if (filter.clientAppCode.trim()) query.set('clientAppCode', filter.clientAppCode.trim())
  if (filter.departmentId.trim()) query.set('departmentId', filter.departmentId.trim())
  return `?${query.toString()}`
}

/**
 * 检查安全字段的基本结构。
 * @param value 未知响应体。
 * @returns 是否为普通对象。
 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * 检查数量为非负整数。
 * @param value 未知字段。
 * @returns 数量有效时为真。
 */
function isCount(value: unknown): value is number {
  return Number.isSafeInteger(value) && (value as number) >= 0
}

/**
 * 检查可省略的非负耗时。
 * @param value 未知字段。
 * @returns 空值或有效耗时时为真。
 */
function isDuration(value: unknown): boolean {
  return value == null || (typeof value === 'number' && Number.isFinite(value) && value >= 0)
}

/**
 * 检查报表概览是否完整。
 * @param value 未知响应体。
 * @returns 响应可安全展示时为真。
 */
function isOverview(value: unknown): value is ReportOverview {
  return isRecord(value) && ['visibleStartedCount', 'visibleCompletedCount', 'visiblePendingCount',
    'visibleOverdueCount', 'confidentialStartedCount', 'confidentialCompletedCount'].every(key => isCount(value[key]))
}

/**
 * 检查流程、节点或人员列表项的字段。
 * @param value 未知列表项。
 * @param strings 必须存在的文本属性。
 * @param counts 必须存在的数量属性。
 * @param durations 可空的耗时属性。
 * @returns 字段完整时为真。
 */
function isMetric(value: unknown, strings: string[], counts: string[], durations: string[]): boolean {
  return isRecord(value) && strings.every(key => typeof value[key] === 'string' && (value[key] as string).length > 0)
    && counts.every(key => isCount(value[key])) && durations.every(key => isDuration(value[key]))
}

/**
 * 在同一时间和维度筛选下读取四条受保护接口；任意一条失败则不展示部分数据。
 * @param filter 日期与维度筛选。
 * @param signal 页面切换或重试的取消信号。
 * @returns 结构完整的公开指标与保密匿名数量。
 */
export async function loadAdminReports(filter: ReportFilter, signal: AbortSignal): Promise<ReportBundle> {
  const token = await adminAccessToken('reports')
  const query = reportQuery(filter)
  const options = { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' as RequestCache, signal }
  const [overview, flows, nodes, people] = await Promise.all([
    getJson<unknown>('/api/v1/admin/reports/overview' + query, options),
    getJson<unknown>('/api/v1/admin/reports/flows' + query, options),
    getJson<unknown>('/api/v1/admin/reports/nodes' + query, options),
    getJson<unknown>('/api/v1/admin/reports/people' + query, options),
  ])
  if (!isOverview(overview) || !isRecord(flows) || !Array.isArray(flows.items)
    || !isCount(flows.confidentialStartedCount) || !isCount(flows.confidentialCompletedCount)
    || !isRecord(nodes) || !Array.isArray(nodes.items)
    || !isRecord(people) || !Array.isArray(people.items)
    || !flows.items.every(item => isMetric(item, ['flowCode'], ['startedCount', 'completedCount', 'approvedCount',
      'rejectedCount', 'withdrawnCount'], ['averageDurationMinutes', 'p90DurationMinutes']))
    || !nodes.items.every(item => isMetric(item, ['flowCode', 'nodeId'], ['completedCount', 'dueCount', 'overdueCount'],
      ['averageDurationMinutes']))
    || !people.items.every(item => isMetric(item, ['assigneeId'], ['pendingCount', 'completedCount', 'overdueCount'],
      ['averageDurationMinutes']))) {
    throw new Error('报表响应不完整，请稍后重试。')
  }
  return { overview, flows: flows.items as ReportFlow[], nodes: nodes.items as ReportNode[],
    people: people.items as ReportPerson[] }
}

import { getJson, requestJson } from './http.ts'
import { batchApproveCenter, executeCenterAction } from './center-actions.ts'
import type { CenterActionResult } from './center-actions.ts'
import type { CenterActionInput, CenterBatchResult } from './center-actions.ts'
import { centerAccessToken, centerSession } from './center-session.ts'
import { centerDetailLayout } from './center-layout.ts'
import { fields, isFieldDefinitions } from './center-fields.ts'
import type { CenterWireFieldDefinition } from './center-fields.ts'
import type { CenterAction, CenterAttachment, CenterDetail, CenterItem, CenterSection, CenterSummaryField, CenterTimelineEntry } from '../model/center.ts'

export type { CenterActionInput, CenterBatchResult } from './center-actions.ts'

/**
 * 尚未登录人员会话时的明确状态，供共用适配器的手机页辨别。
 */
export class CenterAdapterUnavailableError extends Error {
  /**
   * 构造未登录状态。
   */
  constructor() {
    super('请先登录审批中心。')
    this.name = 'CenterAdapterUnavailableError'
  }
}
/**
 * 服务端授权后的列表摘要。
 */
interface CenterWireItem {
  /**
   * 审批单标识。
   */
  id: string
  /**
   * 审批编号。
   */
  serialNo: string
  /**
   * 流程编码。
   */
  flowCode: string
  /**
   * 流程名称。
   */
  flowName: string
  /**
   * 来源应用显示名称。
   */
  sourceName?: string
  /**
   * 发起人显示名称。
   */
  initiatorName?: string
  /**
   * 当前人工环节。
   */
  currentStep?: string | null
  /**
   * 业务单号。
   */
  businessKey: string
  /**
   * 审批标题。
   */
  title: string
  /**
   * 审批状态。
   */
  status: string
  /**
   * 来源应用编码。
   */
  clientAppCode: string
  /**
   * 发起人员标识。
   */
  initiatorId: string
  /**
   * 是否加急。
   */
  isUrgent: boolean
  /**
   * 当前登录人员是否有尚未看过的活动。
   */
  isUnread?: boolean
  /**
   * 发起时间。
   */
  startedAt: string
  /**
   * 结束时间。
   */
  endedAt?: string | null
  /**
   * 按权限裁剪的摘要。
   */
  summary: Record<string, unknown>
  /**
   * 仅包含摘要中已授权字段的发布定义。
   */
  summaryFieldDefinitions?: CenterWireFieldDefinition[]
  /**
   * 当前人员可处理的任务标识。
   */
  pendingTaskIds: string[]
  /**
   * 是否有可批量同意的任务。
   */
  canBulkApprove?: boolean
  /**
   * 服务端确认可批量同意的任务 ID。
   */
  bulkApprovableTaskIds?: string[]
}

/**
 * 服务端分页响应。
 */
interface CenterWirePage {
  /**
   * 授权过滤后的总数。
   */
  total: number
  /**
   * 跳过的数量。
   */
  skip: number
  /**
   * 本页上限。
   */
  take: number
  /**
   * 本页摘要。
   */
  items: CenterWireItem[]
}

/**
 * 服务端只读任务。
 */

interface CenterWireTask {
  /**
   * 任务标识。
   */
  id: string
  /**
   * 节点标识。
   */
  nodeId: string
  /**
   * 节点名称。
   */
  nodeName: string
  /**
   * 审批人标识。
   */
  assigneeId: string
  /**
   * 任务状态。
   */
  status: string
  /**
   * 完成时间。
   */
  completedAt: string | null
  /**
   * 审批意见。
   */
  opinion: string | null
  /**
   * 当前人员在这项任务上的许可命令。
   */
  allowedActions?: string[]
}

/**
 * 服务端安全时间线。
 */

interface CenterWireEvent {
  /**
   * 审批单内递增的活动序号。
   */
  sequence: number
  /**
   * 事件标识。
   */
  eventId: string
  /**
   * 事件类型。
   */
  kind: string
  /**
   * 节点标识。
   */
  nodeId: string | null
  /**
   * 操作人。
   */
  actorId: string | null
  /**
   * 发生时间。
   */
  occurredAt: string
  /**
   * 审批意见。
   */
  opinion: string | null
}

/**
 * 服务端授权后的详情。
 */

interface CenterWireDetail {
  /**
   * 审批摘要。
   */
  instance: CenterWireItem
  /**
   * 按字段权限裁剪的数据。
   */
  data: Record<string, unknown>
  /**
   * 仅包含详情数据已授权字段的发布定义。
   */
  fieldDefinitions?: CenterWireFieldDefinition[]
  /**
   * 仅包含当前人员已授权字段的锁定版本排版。
   */
  layout?: unknown
  /**
   * 当前人员可见且仍关联本审批单的文件元数据。
   */
  attachments?: CenterAttachment[]
  /**
   * 只读任务。
   */
  tasks: CenterWireTask[]
  /**
   * 安全事件。
   */
  events: CenterWireEvent[]
  /**
   * 当前执行节点。
   */
  activeNodeIds: string[]
}

/**
 * 审批中心的数据入口，所有请求使用人员会话而非应用密钥。
 */
export interface CenterDataAdapter {
  /**
   * 读取当前人员有权查看的整类列表。
   * @param section 列表分组。
   * @param signal 路由切换时的取消信号。
   * @param search 可见字段内容或单据元数据搜索词。
   * @returns 已授权的审批摘要。
   */
  loadList(section: CenterSection, signal: AbortSignal, search?: string): Promise<readonly CenterItem[]>
  /**
   * 读取一张审批单的授权详情。
   * @param id 审批单标识。
   * @param signal 路由切换时的取消信号。
   * @returns 已授权的详情。
   */
  loadDetail(id: string, signal: AbortSignal): Promise<CenterDetail>
  /**
   * 详情成功显示后让服务端按当前 JWT 显式推进本人已读游标。
   * @param id 审批单标识。
   * @param signal 页面切换时的取消信号。
   * @param seenThroughSequence 本次详情实际展示的最大活动序号。
   * @returns 服务端确认完成。
   */
  markRead(id: string, signal: AbortSignal, seenThroughSequence: number): Promise<void>
  /**
   * 使用当前 JWT 身份办理一项任务。
   * @param taskId 服务端返回的任务 ID。
   * @param input 任务命令。
   * @returns 服务端确认的任务和审批单 ID。
   */
  executeAction(taskId: string, input: CenterActionInput): Promise<CenterActionResult>
  /**
   * 按任务 ID 逐项批量同意。
   * @param taskIds 服务端声明可批量同意的任务。
   * @param opinion 共用意见。
   * @returns 与输入顺序一致的结果。
   */
  batchApprove(taskIds: readonly string[], opinion: string): Promise<readonly CenterBatchResult[]>
}

/**
 * 判断接口响应是否为普通对象。
 * @param value 待检查数据。
 * @returns 是否为对象。
 */
function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * 校验服务器列表响应的安全字段，异常响应不能伪装成空列表。
 * @param value 服务器响应。
 * @returns 是否为中心列表响应。
 */
function isPage(value: unknown): value is CenterWirePage {
  return isObject(value) && Number.isSafeInteger(value.total) && Number.isSafeInteger(value.skip)
    && Number.isSafeInteger(value.take) && Array.isArray(value.items)
    && value.items.every(isWireItem)
}

/**
 * 校验每条授权摘要。
 * @param value 待检查数据。
 * @returns 是否为摘要。
 */
function isWireItem(value: unknown): value is CenterWireItem {
  return isObject(value) && ['id', 'serialNo', 'flowCode', 'flowName', 'businessKey', 'title', 'status', 'clientAppCode', 'initiatorId', 'startedAt']
    .every(key => typeof value[key] === 'string') && typeof value.isUrgent === 'boolean'
    && (value.endedAt === undefined || value.endedAt === null || typeof value.endedAt === 'string')
    && isObject(value.summary)
    && (value.summaryFieldDefinitions === undefined || isFieldDefinitions(value.summaryFieldDefinitions))
    && Array.isArray(value.pendingTaskIds)
    && value.pendingTaskIds.every(id => typeof id === 'string')
}

/**
 * 校验详情的必要结构。
 * @param value 服务器响应。
 * @returns 是否为详情。
 */
function isDetail(value: unknown): value is CenterWireDetail {
  return isObject(value) && isWireItem(value.instance) && isObject(value.data)
    && (value.fieldDefinitions === undefined || isFieldDefinitions(value.fieldDefinitions))
    && (value.attachments === undefined || Array.isArray(value.attachments) && value.attachments.every(file =>
      isObject(file) && typeof file.id === 'string' && typeof file.fileName === 'string' &&
      typeof file.contentType === 'string' && typeof file.size === 'number'))
    && Array.isArray(value.tasks) && value.tasks.every(task => isObject(task)
      && ['id', 'nodeId', 'nodeName', 'assigneeId', 'status'].every(key => typeof task[key] === 'string'))
    && Array.isArray(value.events) && value.events.every(event => isObject(event)
      && typeof event.eventId === 'string' && typeof event.kind === 'string' && typeof event.occurredAt === 'string'
      && Number.isSafeInteger(event.sequence) && (event.sequence as number) >= 0)
    && Array.isArray(value.activeNodeIds) && value.activeNodeIds.every(id => typeof id === 'string')
}

/**
 * 将引擎稳定命令映射为中心界面操作，未知命令不展示。
 * @param code 服务端命令编码。
 * @returns 已支持的界面操作。
 */
function action(code: string): CenterAction | null {
  const aliases: Record<string, CenterAction> = {
    approve: 'approve', handle: 'handle', acknowledge: 'acknowledge', reject: 'reject', return: 'return',
    transfer: 'transfer', addSigner: 'addSign', removeSigner: 'removeSign', consult: 'consult', reply: 'reply',
    requestSupplement: 'supplement', copyTo: 'copy', revokeApproval: 'undoApproval', comment: 'comment', saveDraft: 'saveDraft',
  }
  return aliases[code] ?? null
}

/**
 * 转换服务端摘要；尚无许可协议的操作始终禁用。
 * @param wire 已授权摘要。
 * @param section 当前列表。
 * @param tasks 详情中的只读任务。
 * @returns 界面摘要。
 */
function item(wire: CenterWireItem, section: CenterSection, tasks: readonly CenterWireTask[] = []): CenterItem {
  const pending = tasks.find(task => wire.pendingTaskIds.includes(task.id))
  const latest = tasks.at(-1)
  const summary: CenterSummaryField[] = fields(wire.summary, wire.summaryFieldDefinitions)
  const allowed = tasks.filter(task => task.allowedActions?.length).flatMap(task => task.allowedActions ?? [])
    .map(action).filter((code): code is CenterAction => code !== null)
  const bulkTaskIds = wire.bulkApprovableTaskIds ?? []
  if (wire.canBulkApprove && bulkTaskIds.length && !allowed.includes('approve')) allowed.push('approve')
  return {
    id: wire.id, section, title: wire.title, serialNo: wire.serialNo, businessKey: wire.businessKey,
    flowCode: wire.flowCode, flowName: wire.flowName, sourceName: wire.sourceName || wire.clientAppCode,
    initiatorName: wire.initiatorName || wire.initiatorId, currentStep: wire.currentStep || (pending?.nodeName ?? latest?.nodeName ?? '—'),
    status: wire.status, createdAt: wire.startedAt, updatedAt: wire.endedAt ?? wire.startedAt,
    urgent: wire.isUrgent, unread: wire.isUnread === true, allowsBatchApprove: Boolean(wire.canBulkApprove && bulkTaskIds.length),
    bulkTaskIds, allowedActions: [...new Set(allowed)], summary,
  }
}

/**
 * 构造当前人员 JWT 请求头。
 * @returns 授权请求头。
 */
async function headers(): Promise<HeadersInit> {
  if (!centerSession()) throw new CenterAdapterUnavailableError()
  return { Authorization: `Bearer ${await centerAccessToken()}` }
}

/**
 * 真实中心适配器；逐页读取时每次均经过服务端身份和参与关系过滤。
 */
export const centerAdapter: CenterDataAdapter = {
  /**
   * @inheritdoc
   */

  async loadList(section, signal, search = '') {
    const rows: CenterItem[] = []
    let skip = 0
    let total = 0
    do {
      const query = new URLSearchParams({ section, skip: String(skip), take: '100' })
      if (search.trim()) query.set('search', search.trim())
      const page = await getJson<CenterWirePage>(`/api/v1/center/instances?${query}`, {
        headers: await headers(), signal, cache: 'no-store',
      })
      if (!isPage(page) || page.skip !== skip || page.total < 0 || page.items.length > 100) {
        throw new Error('审批列表响应不完整，请稍后重试。')
      }
      total = page.total
      rows.push(...page.items.map(row => item(row, section)))
      skip += page.items.length
      if (page.items.length === 0 && skip < total) throw new Error('审批列表分页响应不完整，请稍后重试。')
    } while (skip < total)
    return rows
  },
  /**
   * @inheritdoc
   */

  async loadDetail(id, signal) {
    const wire = await getJson<CenterWireDetail>(`/api/v1/center/instances/${encodeURIComponent(id)}`, {
      headers: await headers(), signal, cache: 'no-store',
    })
    if (!isDetail(wire)) throw new Error('审批详情响应不完整，请稍后重试。')
    const personId = centerSession()?.personId
    const section: CenterSection = wire.instance.pendingTaskIds.length ? 'todo'
      : wire.instance.initiatorId === personId ? 'started' : 'done'
    const timeline: CenterTimelineEntry[] = wire.events.map(event => ({
      id: event.eventId, title: event.kind, actor: event.actorId ?? '系统', occurredAt: event.occurredAt,
      opinion: event.opinion ?? '', current: Boolean(event.nodeId && wire.activeNodeIds.includes(event.nodeId)),
    }))
    const actionTasks = wire.tasks.filter(task => task.allowedActions?.length).map(task => ({
      taskId: task.id, nodeName: task.nodeName,
      actions: (task.allowedActions ?? []).map(action).filter((code): code is CenterAction => code !== null),
    }))
    const attachments = wire.attachments ?? []
    const visibleFields = fields(wire.data, wire.fieldDefinitions)
    return { item: item(wire.instance, section, wire.tasks), actionTasks, fields: visibleFields,
      readSequence: wire.events.reduce((maximum, event) => Math.max(maximum, event.sequence), 0),
      layout: centerDetailLayout(wire.layout, visibleFields),
      timeline, attachmentNames: attachments.map(file => file.fileName), attachments }
  },
  /**
   * @inheritdoc
   */
  async markRead(id, signal, seenThroughSequence) {
    await requestJson<void>(`/api/v1/center/instances/${encodeURIComponent(id)}/read`, {
      method: 'POST', headers: await headers(), signal, cache: 'no-store',
      body: { seenThroughSequence },
    })
  },
  executeAction: executeCenterAction,
  batchApprove: batchApproveCenter,
}

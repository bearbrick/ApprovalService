import { getJson, requestJson } from './http.ts'
import { adminAccessToken } from './admin-session.ts'

/**
 * 当前可改派的任务摘要。
 */
export interface InterventionTask {
  /**
   * 不透明任务 ID。
   */
  id: string
  /**
   * 所属节点 ID。
   */
  nodeId: string
  /**
   * 节点名称。
   */
  name: string
  /**
   * 当前人员内部 ID。
   */
  assigneeId: string
  /**
   * 待办或排队状态。
   */
  status: string
}

/**
 * 锁定版本中的人工节点。
 */
export interface InterventionNode {
  /**
   * 节点 ID。
   */
  id: string
  /**
   * 节点名称。
   */
  name: string
}

/**
 * 服务端授权后的运行元数据，不含业务数据。
 */
export interface InterventionRecord {
  /**
   * 审批单 ID。
   */
  id: string
  /**
   * 审批标题。
   */
  title: string
  /**
   * 审批编号。
   */
  serialNo: string
  /**
   * 流程编码。
   */
  flowCode: string
  /**
   * 运行状态。
   */
  status: string
  /**
   * 读取时的运行修订。
   */
  version: number
  /**
   * 锁定发布版本 ID。
   */
  flowVersionId: string
  /**
   * 当前任务选项。
   */
  tasks: InterventionTask[]
  /**
   * 可跳转人工节点选项。
   */
  nodes: InterventionNode[]
}

/**
 * 明确确认后提交的干预参数。
 */
export interface InterventionCommand {
  /**
   * 管理操作。
   */
  action: 'reassign' | 'jump' | 'terminate'
  /**
   * 读取时修订，用于拒绝过期操作。
   */
  expectedVersion: number
  /**
   * 必填干预原因。
   */
  reason: string
  /**
   * 改派任务 ID。
   */
  taskId?: string
  /**
   * 改派接手人员内部 ID。
   */
  targetPersonId?: string
  /**
   * 跳转人工节点 ID。
   */
  targetNodeId?: string
}

/**
 * 核对接口安全摘要，拒绝部分或错误响应。
 * @param value 未知服务端响应。
 * @returns 能完整用于页面时为真。
 */
export function isInterventionRecord(value: unknown): value is InterventionRecord {
  if (!value || typeof value !== 'object') return false
  const row = value as Record<string, unknown>
  return ['id', 'title', 'serialNo', 'flowCode', 'status', 'flowVersionId'].every(key => typeof row[key] === 'string')
    && Number.isSafeInteger(row.version) && (row.version as number) > 0
    && Array.isArray(row.tasks) && row.tasks.every(task => task && ['id', 'nodeId', 'name', 'assigneeId', 'status']
      .every(key => typeof task[key] === 'string'))
    && Array.isArray(row.nodes) && row.nodes.every(node => node && typeof node.id === 'string' && typeof node.name === 'string')
}

/**
 * 请求最近一百条已授权在途单据。
 * @param signal 页面取消信号。
 * @returns 当前范围内安全摘要。
 */
export async function listInterventions(signal?: AbortSignal): Promise<InterventionRecord[]> {
  const token = await adminAccessToken('flows')
  const rows = await getJson<unknown>('/api/v1/admin/interventions', {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store', signal,
  })
  if (!Array.isArray(rows) || !rows.every(isInterventionRecord)) throw new Error('运行干预列表响应不完整。')
  return rows
}

/**
 * 重新授权读取指定单据。
 * @param id 审批单 ID。
 * @param signal 页面取消信号。
 * @returns 当前真实详情。
 */
export async function getIntervention(id: string, signal?: AbortSignal): Promise<InterventionRecord> {
  const token = await adminAccessToken('flows')
  const row = await getJson<unknown>(`/api/v1/admin/interventions/${encodeURIComponent(id)}`, {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store', signal,
  })
  if (!isInterventionRecord(row)) throw new Error('运行干预详情响应不完整。')
  return row
}

/**
 * 使用稳定请求 ID 提交单次干预，不自动重试写操作。
 * @param id 审批单 ID。
 * @param input 已确认操作。
 * @param requestId 本次确认固定的请求 ID，未知结果重试时复用。
 * @returns 服务端确认命令成功。
 */
export async function executeIntervention(id: string, input: InterventionCommand, requestId: string): Promise<void> {
  const token = await adminAccessToken('flows')
  await requestJson<void>(`/api/v1/admin/interventions/${encodeURIComponent(id)}/commands`, {
    method: 'POST', body: input, headers: { Authorization: `Bearer ${token}`, 'X-Request-Id': requestId },
  })
}

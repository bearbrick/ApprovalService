import { requestJson } from './http.ts'
import { centerAccessToken, centerSession } from './center-session.ts'

/**
 * 中心任务的操作输入，人员 ID 均为审批服务内部 ID。
 */
export interface CenterActionInput {
  /**
   * 服务器命令编码。
   */
  action: string
  /**
   * 审批意见。
   */
  opinion?: string
  /**
   * 本次明确选用的本人手写签名资产 ID。
   */
  signatureFileId?: string
  /**
   * 可选目标人员 ID。
   */
  targetPersonId?: string
  /**
   * 可选退回节点 ID。
   */
  targetNodeId?: string
  /**
   * 征询或加签关联 ID。
   */
  relatedId?: string
  /**
   * 退回或加签模式。
   */
  mode?: string
  /**
   * 提及的内部人员 ID。
   */
  mentionedPersonIds?: string[]
  /**
   * 本节点可编辑的审批人字段。
   */
  fieldValues?: Record<string, unknown>
}

/**
 * 批量审批中单项任务的独立结果。
 */
export interface CenterBatchResult {
  /**
   * 任务 ID。
   */
  taskId: string
  /**
   * 此任务是否成功。
   */
  succeeded: boolean
  /**
   * 成功办理时对应的审批单 ID。
   */
  instanceId?: string | null
  /**
   * 失败时的中文说明。
   */
  message?: string | null
}

/**
 * 单任务成功提交后服务端返回的安全标识。
 */
export interface CenterActionResult {
  /**
   * 本次办理的任务 ID。
   */
  taskId: string
  /**
   * 本次办理的审批单 ID。
   */
  instanceId: string
}

/**
 * 为中心写请求取得当前人员 JWT 和独立幂等请求 ID。
 * @returns 本次请求头。
 */
async function actionHeaders(): Promise<Record<string, string>> {
  if (!centerSession()) throw new Error('请先登录审批中心。')
  return { Authorization: `Bearer ${await centerAccessToken()}`, 'X-Request-Id': crypto.randomUUID() }
}

/**
 * 使用当前 JWT 身份办理一项任务。
 * @param taskId 服务端返回的任务 ID。
 * @param input 任务命令。
 * @returns 服务端确认的任务和审批单 ID。
 */
export async function executeCenterAction(taskId: string, input: CenterActionInput): Promise<CenterActionResult> {
  const result = await requestJson<CenterActionResult>(`/api/v1/center/tasks/${encodeURIComponent(taskId)}/actions`, {
    method: 'POST', headers: await actionHeaders(),
    body: { ...input, mentionedPersonIds: input.mentionedPersonIds ?? [] }, cache: 'no-store',
  })
  if (!result || result.taskId !== taskId || typeof result.instanceId !== 'string' || !result.instanceId) {
    throw new Error('审批操作响应不完整，请刷新后核对处理结果。')
  }
  return result
}

/**
 * 按任务 ID 逐项批量同意，并校验逐项结果形状。
 * @param taskIds 服务端声明可批量同意的任务。
 * @param opinion 共用意见。
 * @returns 与输入顺序一致的结果。
 */
export async function batchApproveCenter(taskIds: readonly string[], opinion: string): Promise<readonly CenterBatchResult[]> {
  const result = await requestJson<{ items: CenterBatchResult[] }>('/api/v1/center/tasks/batch-approve', {
    method: 'POST', headers: await actionHeaders(), body: { taskIds, opinion }, cache: 'no-store',
  })
  if (!result || !Array.isArray(result.items) || result.items.some(row =>
    !row || typeof row.taskId !== 'string' || typeof row.succeeded !== 'boolean' ||
    (row.succeeded && (typeof row.instanceId !== 'string' || !row.instanceId)))) {
    throw new Error('批量同意响应不完整，请刷新待办核对处理结果。')
  }
  return result.items
}

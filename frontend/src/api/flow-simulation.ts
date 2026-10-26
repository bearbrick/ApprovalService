import { getJson, requestJson } from './http.ts'
import { adminAccessToken } from './admin-session.ts'
import type { SimulationInput } from '../designer/workbench/simulation-model.ts'

/**
 * 纯内存测试任务摘要。
 */
export interface SimulationTask {
  /**
   * 假任务 ID。
   */
  id: string
  /**
   * 当前节点 ID。
   */
  nodeId: string
  /**
   * 假处理人 ID。
   */
  assigneeId: string
  /**
   * pending 或终态。
   */
  status: string
  /**
   * 成功推进时应发送的引擎命令。
   */
  completionKind: string
}

/**
 * 等待显式假外部结果的令牌。
 */
export interface SimulationWait {
  /**
   * 令牌 ID。
   */
  tokenId: string
  /**
   * 等待节点 ID。
   */
  nodeId: string
  /**
   * action 或 subprocess。
   */
  kind: string
}

/**
 * 会话事件只显示服务端允许的摘要。
 */
export interface SimulationEvent {
  /**
   * 事件种类。
   */
  kind: string
  /**
   * 关联节点 ID。
   */
  nodeId: string | null
  /**
   * 关联任务 ID。
   */
  taskId: string | null
  /**
   * 关联令牌 ID。
   */
  tokenId: string | null
}

/**
 * 发布前试跑的当前状态，不含真实审批单。
 */
export interface SimulationSession {
  /**
   * 随机内存会话 ID。
   */
  id: string
  /**
   * 草稿流程编码。
   */
  flowCode: string
  /**
   * 闲置过期时间。
   */
  expiresAt: string
  /**
   * 引擎运行状态。
   */
  status: string
  /**
   * 本会话任务。
   */
  tasks: SimulationTask[]
  /**
   * 外部等待。
   */
  waits: SimulationWait[]
  /**
   * 已产生事件。
   */
  events: SimulationEvent[]
}

/**
 * 模拟一步的受限命令。
 */
export interface SimulationStep {
  /**
   * 成功、拒绝或假外部结果的命令编码。
   */
  kind: string
  /**
   * 人工任务 ID。
   */
  taskId?: string
  /**
   * 外部等待令牌 ID。
   */
  tokenId?: string
  /**
   * 当前假处理人 ID。
   */
  actorId?: string
  /**
   * 子流程是否成功。
   */
  success?: boolean
  /**
   * 自动节点的假结果。
   */
  actionResult?: unknown
}

/**
 * 将未知响应缩小为普通对象。
 * @param value 未经校验的服务端响应。
 * @returns 非数组对象时返回真。
 */
function record(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value)
}

/**
 * 避免损坏的响应被当作可继续操作的会话。
 * @param value 未经校验的服务端响应。
 * @returns 完整的会话摘要。
 */
function requireSession(value: unknown): SimulationSession {
  if (!record(value) || ['id', 'flowCode', 'expiresAt', 'status'].some(key => typeof value[key] !== 'string')
    || !Array.isArray(value.tasks) || !Array.isArray(value.waits) || !Array.isArray(value.events)
    || !value.tasks.every(item => record(item) && ['id', 'nodeId', 'assigneeId', 'status', 'completionKind']
      .every(key => typeof item[key] === 'string'))
    || !value.waits.every(item => record(item) && ['tokenId', 'nodeId', 'kind']
      .every(key => typeof item[key] === 'string'))
    || !value.events.every(item => record(item) && typeof item.kind === 'string')) {
    throw new Error('测试模式返回的数据不完整，请重新开始。')
  }
  return value as unknown as SimulationSession
}

/**
 * 创建仅驻留在服务端内存中的模拟会话。
 * @param input 当前草稿和全部假输入。
 * @returns 首步执行后的状态。
 */
export async function startFlowSimulation(input: SimulationInput): Promise<SimulationSession> {
  return requireSession(await requestJson<unknown>('/api/v1/admin/flows/simulations', {
    method: 'POST', headers: { Authorization: `Bearer ${await adminAccessToken('flows')}` },
    body: input, cache: 'no-store',
  }))
}

/**
 * 从服务端重新读取当前内存会话。
 * @param id 模拟会话 ID。
 * @returns 当前状态。
 */
export async function getFlowSimulation(id: string): Promise<SimulationSession> {
  return requireSession(await getJson<unknown>(`/api/v1/admin/flows/simulations/${encodeURIComponent(id)}`, {
    headers: { Authorization: `Bearer ${await adminAccessToken('flows')}` }, cache: 'no-store',
  }))
}

/**
 * 提交一条假任务决定或假外部结果。
 * @param id 模拟会话 ID。
 * @param step 仅允许的测试命令。
 * @returns 下一步状态。
 */
export async function stepFlowSimulation(id: string, step: SimulationStep): Promise<SimulationSession> {
  return requireSession(await requestJson<unknown>(`/api/v1/admin/flows/simulations/${encodeURIComponent(id)}/steps`, {
    method: 'POST', headers: { Authorization: `Bearer ${await adminAccessToken('flows')}` },
    body: step, cache: 'no-store',
  }))
}

/**
 * 删除当前内存会话；服务端过期时也可直接清空本地界面。
 * @param id 模拟会话 ID。
 * @returns 删除完成。
 */
export async function deleteFlowSimulation(id: string): Promise<void> {
  await requestJson<void>(`/api/v1/admin/flows/simulations/${encodeURIComponent(id)}`, {
    method: 'DELETE', headers: { Authorization: `Bearer ${await adminAccessToken('flows')}` }, cache: 'no-store',
  })
}

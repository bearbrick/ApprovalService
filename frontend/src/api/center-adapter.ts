import type { CenterDetail, CenterItem, CenterSection } from '../model/center.ts'

/**
 * 审批中心接口尚未接入时的明确状态，不假装空列表。
 */
export class CenterAdapterUnavailableError extends Error {
  /**
   * 构造界面可辨认的未接入错误。
   */
  constructor() {
    super('审批中心接口尚未接入。')
    this.name = 'CenterAdapterUnavailableError'
  }
}

/**
 * 审批中心页面需要的只读数据入口；正式接口实现由后续任务提供。
 */
export interface CenterDataAdapter {
  /**
   * 读取当前登录用户有权查看的列表。
   * @param section 列表分组。
   * @param signal 路由切换时的取消信号。
   * @returns 已授权的审批摘要。
   */
  loadList(section: CenterSection, signal: AbortSignal): Promise<readonly CenterItem[]>
  /**
   * 读取一张审批单的只读详情。
   * @param id 审批单标识。
   * @param signal 路由切换时的取消信号。
   * @returns 已授权的详情。
   */
  loadDetail(id: string, signal: AbortSignal): Promise<CenterDetail>
}

/**
 * 未接入的数据适配器，避免向尚未确定的接口或匿名端点发请求。
 */
export const centerAdapter: CenterDataAdapter = {
  /** @inheritdoc */
  async loadList(): Promise<readonly CenterItem[]> {
    throw new CenterAdapterUnavailableError()
  },
  /** @inheritdoc */
  async loadDetail(): Promise<CenterDetail> {
    throw new CenterAdapterUnavailableError()
  },
}

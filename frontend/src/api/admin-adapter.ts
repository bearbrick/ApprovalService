import type { AdminViewData } from '../model/admin.ts'

/**
 * 管理后台真实鉴权与接口未接入时的明确状态。
 */
export class AdminAdapterUnavailableError extends Error {
  /**
   * 构造页面可辨认的未接入错误。
   */
  constructor() {
    super('管理后台权限与数据接口尚未接入。')
    this.name = 'AdminAdapterUnavailableError'
  }
}

/**
 * 管理页面只读数据适配入口，正式实现必须附带管理员权限。
 */
export interface AdminDataAdapter {
  /**
   * 读取当前登录管理员有权查看的页面数据。
   * @param viewKey 管理页面编码。
   * @param signal 路由切换或重试的取消信号。
   * @returns 按权限过滤后的列表与指标。
   */
  loadView(viewKey: string, signal: AbortSignal): Promise<AdminViewData>
}

/**
 * 未接入时不向匿名或猜测的管理端点发送请求。
 */
export const adminAdapter: AdminDataAdapter = {
  /** @inheritdoc */
  async loadView(): Promise<AdminViewData> {
    throw new AdminAdapterUnavailableError()
  },
}

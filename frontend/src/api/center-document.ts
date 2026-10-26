import { requestJson } from './http.ts'
import { centerAccessToken } from './center-session.ts'

/**
 * 审批服务已验签和来源校验的一次性业务原单地址。
 */
export interface CenterDocumentAddress {
  /**
   * 只读 HTTPS 地址。
   */
  url: string
  /**
   * 业务系统声明的到期时间。
   */
  expiresAt: string
}

/**
 * 用当前登录人员身份为一张已授权审批单申请原单地址。
 * @param instanceId 审批单 ID。
 * @returns 不应缓存的一次性地址。
 */
export async function fetchCenterDocumentAddress(instanceId: string): Promise<CenterDocumentAddress> {
  return requestJson<CenterDocumentAddress>(
    `/api/v1/center/instances/${encodeURIComponent(instanceId)}/document-url`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${await centerAccessToken()}` },
      cache: 'no-store',
    },
  )
}

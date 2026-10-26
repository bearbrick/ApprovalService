import { getJson, requestJson } from './http.ts'
import { adminAccessToken } from './admin-session.ts'

/**
 * 当前应用的原单签名钩子及可信子页面来源。
 */
export interface AdminDocumentSettings {
  /**
   * 应用编码。
   */
  code: string
  /**
   * 可空的完整钩子地址。
   */
  documentUrl: string | null
  /**
   * 原单页面的完整 HTTPS 来源。
   */
  allowedDocumentOrigins: string[]
}

/**
 * 读取本应用的原单安全设置。
 * @param code 应用编码。
 * @returns 当前设置。
 */
export async function getAdminDocumentSettings(code: string): Promise<AdminDocumentSettings> {
  const result = await getJson<AdminDocumentSettings>(
    `/api/v1/admin/apps/${encodeURIComponent(code)}/document-settings`, {
      headers: { Authorization: `Bearer ${await adminAccessToken()}` }, cache: 'no-store',
    },
  )
  if (!result || result.code !== code || !Array.isArray(result.allowedDocumentOrigins)) {
    throw new Error('业务原单配置响应不完整。')
  }
  return result
}

/**
 * 完整保存某应用的钩子地址和可信页面来源。
 * @param code 应用编码。
 * @param documentUrl 可空的签名钩子地址。
 * @param allowedDocumentOrigins 完整 HTTPS 来源列表。
 * @param reason 管理操作原因。
 * @returns 已保存的规范配置。
 */
export async function saveAdminDocumentSettings(code: string, documentUrl: string | null,
  allowedDocumentOrigins: readonly string[], reason: string): Promise<AdminDocumentSettings> {
  const result = await requestJson<AdminDocumentSettings>(
    `/api/v1/admin/apps/${encodeURIComponent(code)}/document-settings`, {
      method: 'PUT', headers: { Authorization: `Bearer ${await adminAccessToken()}` },
      body: { documentUrl, allowedDocumentOrigins, reason }, cache: 'no-store',
    },
  )
  if (!result || result.code !== code || !Array.isArray(result.allowedDocumentOrigins)) {
    throw new Error('业务原单配置保存响应不完整。')
  }
  return result
}

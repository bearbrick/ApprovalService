import { adminAccessToken } from './admin-session.ts'
import { getJson, requestJson } from './http.ts'

/**
 * 系统信息的安全摘要。
 */
export interface SystemBranding {
  /**
 * 单位名称。
 */
  companyName: string
  /**
 * Logo 内容摘要。
 */
  logoSha256: string | null
  /**
 * 乐观并发修订。
 */
  revision: number
}

/**
 * 读取当前设置。
 * @returns 服务端设置。
 */
export async function loadSystemBranding(): Promise<SystemBranding> {
  const token = await adminAccessToken()
  const value = await getJson<SystemBranding>('/api/v1/admin/system-info', {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  })
  if (!value || typeof value.companyName !== 'string' || typeof value.revision !== 'number'
    || (value.logoSha256 !== null && typeof value.logoSha256 !== 'string')) {
    throw new Error('系统信息响应不完整。')
  }
  return value
}

/**
 * 保存单位名称。
 * @param companyName 新名称。
 * @param expectedRevision 编辑时读取的修订。
 * @param reason 操作原因。
 * @returns 新设置。
 */
export async function saveCompanyName(companyName: string, expectedRevision: number,
  reason: string): Promise<SystemBranding> {
  const token = await adminAccessToken()
  return requestJson<SystemBranding>('/api/v1/admin/system-info', {
    method: 'PUT', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
    body: { companyName, expectedRevision, reason },
  })
}

/**
 * 读取当前 Logo 并生成可释放的预览地址。
 * @returns 浏览器图片地址。
 */
export async function loadSystemLogoUrl(): Promise<string> {
  const token = await adminAccessToken()
  const response = await fetch('/api/v1/admin/system-info/logo', {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  })
  if (!response.ok) throw new Error('无法读取当前 Logo。')
  if (response.headers.get('content-type') !== 'image/png') {
    throw new Error('Logo 响应类型不正确。')
  }
  return URL.createObjectURL(await response.blob())
}

/**
 * 上传图片并记录原因。
 * @param file 本地图片。
 * @param expectedRevision 编辑时读取的修订。
 * @param reason 操作原因。
 * @returns 新设置。
 */
export async function uploadSystemLogo(file: File, expectedRevision: number,
  reason: string): Promise<SystemBranding> {
  if (file.size > 1024 * 1024 || file.type !== 'image/png') {
    throw new Error('请选择不超过 1 MiB 的 PNG 图片。')
  }
  const bytes = new Uint8Array(await file.arrayBuffer())
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  const token = await adminAccessToken()
  return requestJson<SystemBranding>('/api/v1/admin/system-info/logo', {
    method: 'POST', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
    body: { contentBase64: btoa(binary), expectedRevision, reason },
  })
}

/**
 * 移除当前 Logo。
 * @param expectedRevision 编辑时读取的修订。
 * @param reason 操作原因。
 * @returns 新设置。
 */
export async function deleteSystemLogo(expectedRevision: number, reason: string): Promise<SystemBranding> {
  const token = await adminAccessToken()
  return requestJson<SystemBranding>('/api/v1/admin/system-info/logo', {
    method: 'DELETE', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
    body: { expectedRevision, reason },
  })
}

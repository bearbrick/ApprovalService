import { adminAccessToken } from './admin-session.ts'
import { requestJson } from './http.ts'

/**
 * 角色接口的完整安全摘要。
 */
export interface ManagedRole {
  /**
   * 不可变编码。
   */
  code: string
  /**
   * 显示名称。
   */
  name: string
  /**
   * 名称和成员共用的修订。
   */
  revision: number
  /**
   * 是否为内置管理角色。
   */
  isBuiltIn: boolean
  /**
   * 内部人员 ID。
   */
  personIds: string[]
}

/**
 * 拒绝不完整响应，避免用空成员覆盖真实授权。
 * @param value 未信任的响应。
 * @returns 完整角色。
 */
export function readManagedRole(value: unknown): ManagedRole {
  if (!value || typeof value !== 'object') throw new Error('角色响应不完整。')
  const item = value as ManagedRole
  if (typeof item.code !== 'string' || !item.code || typeof item.name !== 'string'
    || !Number.isSafeInteger(item.revision) || item.revision < 0 || typeof item.isBuiltIn !== 'boolean'
    || !Array.isArray(item.personIds) || item.personIds.some(id => typeof id !== 'string' || !id)
    || new Set(item.personIds).size !== item.personIds.length) throw new Error('角色响应不完整。')
  return item
}

/**
 * 读取有权限的完整角色目录。
 * @param signal 页面取消信号。
 * @returns 角色列表。
 */
export async function listManagedRoles(signal?: AbortSignal): Promise<ManagedRole[]> {
  const token = await adminAccessToken()
  const value = await requestJson<unknown>('/api/v1/admin/roles', {
    headers: { Authorization: `Bearer ${token}` }, cache: 'no-store', signal,
  })
  if (!Array.isArray(value)) throw new Error('角色列表响应不完整。')
  return value.map(readManagedRole)
}

/**
 * 创建角色或按原始修订保存单项修改。
 * @param method 创建或修改方法。
 * @param code 创建时为空，否则为不可变编码。
 * @param members 是否更新成员。
 * @param body 本次请求正文。
 * @returns 已保存的服务端角色。
 */
export async function saveManagedRole(method: 'POST' | 'PUT', code: string | null, members: boolean,
  body: Record<string, unknown>): Promise<ManagedRole> {
  const token = await adminAccessToken()
  const path = '/api/v1/admin/roles' + (code === null ? '' : `/${encodeURIComponent(code)}${members ? '/members' : ''}`)
  return readManagedRole(await requestJson<unknown>(path, {
    method, body, headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
  }))
}

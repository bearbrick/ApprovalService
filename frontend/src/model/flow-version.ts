/**
 * 用于区分当前发布版与可复制的旧版本。
 */
export interface PublishedVersionLike {
  /**
   * 流程内递增的版本号。
   */
  version: number
  /**
   * 是否当前新审批单使用的发布版。
   */
  isCurrent: boolean
}

/**
 * 仅列出可复制为草稿的旧发布版，并按版本号倒序排列。
 * @template T 带发布版本字段的安全摘要。
 * @param versions 服务端返回的不可变版本摘要。
 * @returns 不修改输入的旧版本列表。
 */
export function restorableVersions<T extends PublishedVersionLike>(versions: readonly T[]): T[] {
  return versions.filter(version => !version.isCurrent).sort((left, right) => right.version - left.version)
}

/**
 * 检查恢复原因符合服务端要求，防止空原因触发写请求。
 * @param reason 管理员输入的审计原因。
 * @returns 去除首尾空白后长度在二至二百字之间时为真。
 */
export function validRestoreReason(reason: string): boolean {
  const length = reason.trim().length
  return length >= 2 && length <= 200
}

import type { CenterDetailField } from '../model/center.ts'

/**
 * 服务端只为实际返回的数据键提供的发布字段元数据。
 */
export interface CenterWireFieldDefinition {
  /**
   * 当前层字段编码。
   */
  key: string
  /**
   * 发布时的字段名称。
   */
  name: string
  /**
   * 发布时的字段种类。
   */
  type: string
  /**
   * 已授权的明细子字段。
   */
  children: CenterWireFieldDefinition[]
}

/**
 * 判断响应的一层值是否为普通对象。
 * @param value 待检查数据。
 * @returns 是否为对象。
 */
function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * 校验可选的发布字段元数据，防止损坏结构进入展示层。
 * @param value 服务响应中的字段定义。
 * @returns 是否为完整字段定义数组。
 */
export function isFieldDefinitions(value: unknown): value is CenterWireFieldDefinition[] {
  return Array.isArray(value) && value.every(field => isObject(field) &&
    typeof field.key === 'string' && typeof field.name === 'string' && typeof field.type === 'string' &&
    Array.isArray(field.children) && field.children.every(child => isObject(child) &&
      typeof child.key === 'string' && typeof child.name === 'string' && typeof child.type === 'string'))
}

/**
 * 把已授权字段的 JSON 值转为易读文本。
 * @param value 字段值。
 * @returns 只读展示文本。
 */
function displayValue(value: unknown): string {
  if (value === null || value === undefined) return '—'
  if (typeof value === 'string') return value
  if (typeof value === 'number') return String(value)
  if (typeof value === 'boolean') return value ? '是' : '否'
  if (Array.isArray(value)) return `${value.length} 项`
  return JSON.stringify(value)
}

/**
 * 按服务端已裁剪字段和发布名称构造只读文字或明细表。
 * @param data 已授权的数据对象。
 * @param definitions 服务端返回的已授权字段元数据。
 * @returns 展示字段。
 */
export function fields(data: Record<string, unknown>, definitions: readonly CenterWireFieldDefinition[] = []): CenterDetailField[] {
  const byKey = new Map(definitions.map(definition => [definition.key, definition]))
  return Object.entries(data).map(([key, value]) => {
    const definition = byKey.get(key)
    const result: CenterDetailField = { key, label: definition?.name || key,
      value: definition?.type === 'attachment' ? `${Array.isArray(value) ? value.length : 1} 个附件`
        : Array.isArray(value) && definition?.type === 'detailTable' ? `${value.length} 条明细` : displayValue(value) }
    if (Array.isArray(value) && definition?.type === 'detailTable' && definition.children.length) {
      const columns = definition.children.map(child => ({ key: child.key, label: child.name || child.key }))
      result.columns = columns
      result.rows = value.filter(isObject).map(row => columns.map(column => displayValue(row[column.key])))
    }
    return result
  })
}

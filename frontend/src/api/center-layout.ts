import type { CenterDetailField, CenterDetailLayout } from '../model/center.ts'

/**
 * 判断服务端响应的一层值是否为对象。
 * @param value 待检查的值。
 * @returns 是否为普通对象。
 */
function isObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

/**
 * 只用已授权字段解释服务端排版；旧服务和异常结构回退自动排版。
 * @param wire 服务端裁剪后的排版元数据。
 * @param fields 当前人员已授权的详情字段。
 * @returns 安全可绘制的详情排版。
 */
export function centerDetailLayout(wire: unknown, fields: readonly CenterDetailField[]): CenterDetailLayout {
  const automatic: CenterDetailLayout = { mode: 'auto', groups: [] }
  if (isObject(wire) && wire.mode === 'embed' && typeof wire.embedPageCode === 'string' &&
    wire.embedPageCode.length > 0) return { mode: 'embed', embedPageCode: wire.embedPageCode, groups: [] }
  if (!isObject(wire) || wire.mode !== 'custom' || !Array.isArray(wire.groups)) return automatic
  const visible = new Map(fields.map(field => [field.key, field]))
  const groups = wire.groups.flatMap(group => {
    if (!isObject(group) || !Array.isArray(group.items) || typeof group.columns !== 'number' ||
      !Number.isInteger(group.columns) || group.columns < 1 || group.columns > 3 || typeof group.title !== 'string') return []
    const items = group.items.flatMap(source => {
      if (!isObject(source) || typeof source.fieldKey !== 'string') return []
      const field = visible.get(source.fieldKey)
      if (!field) return []
      const columnSpan = typeof source.columnSpan === 'number' && Number.isInteger(source.columnSpan)
        ? Math.max(1, Math.min(group.columns as number, source.columnSpan)) : 1
      const allowedColumns = new Set(field.columns?.map(column => column.key) ?? [])
      const widths = isObject(source.detailColumnWidths) ? Object.fromEntries(Object.entries(source.detailColumnWidths)
        .filter((entry): entry is [string, number] => allowedColumns.has(entry[0]) &&
          typeof entry[1] === 'number' && entry[1] > 0 && entry[1] <= 100)) : {}
      return [{ field, columnSpan, detailColumnWidths: widths,
        moneyUppercase: typeof source.moneyUppercase === 'string' ? source.moneyUppercase : undefined }]
    })
    return items.length ? [{ title: group.title, columns: group.columns as number, items }] : []
  })
  return groups.length ? { mode: 'custom', groups } : automatic
}

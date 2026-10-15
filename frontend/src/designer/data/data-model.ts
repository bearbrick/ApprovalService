import type { FlowDataDefinition, FlowDetailLayout, FlowFieldDefinition, FlowLayoutGroup, FlowLayoutItem } from '../../model/flow.ts'

/**
 * 可用于摘要、排版和节点权限的字段引用。
 */
export interface FieldReference {
  /**
   * 字段路径；明细子字段用表 key 加句点。
   */
  key: string
  /**
   * 含父级名称的展示文字。
   */
  label: string
  /**
   * 字段定义。
   */
  field: FlowFieldDefinition
}

/**
 * 详情预览的一个分组。
 */
export interface PreviewGroup {
  /**
   * 分组标题。
   */
  title: string
  /**
   * 一行列数。
   */
  columns: number
  /**
   * 该组展示的字段和排版属性。
   */
  items: PreviewItem[]
}

/**
 * 详情预览中的字段。
 */
export interface PreviewItem {
  /**
   * 字段定义。
   */
  field: FlowFieldDefinition
  /**
   * 字段排版配置。
   */
  layout: FlowLayoutItem
}

/**
 * 新建一个符合契约的字段。
 * @param key 字段标识。
 * @param name 展示名称。
 * @returns 可直接加入定义的字段。
 */
export function createField(key: string, name: string): FlowFieldDefinition {
  return { key, name, type: 'text', required: false, approverInput: false, children: [] }
}

/**
 * 深拷贝字段定义，避免组件直接修改父级流程草稿。
 * @param data 原审批数据定义。
 * @returns 新的字段定义。
 */
export function copyData(data: FlowDataDefinition): FlowDataDefinition {
  return { fields: data.fields.map(copyField), summaryFields: [...data.summaryFields] }
}

/**
 * 深拷贝一个字段及其明细子字段。
 * @param field 原字段。
 * @returns 新字段。
 */
export function copyField(field: FlowFieldDefinition): FlowFieldDefinition {
  return { ...field, children: field.children.map(copyField) }
}

/**
 * 深拷贝详情排版设置。
 * @param layout 原排版。
 * @returns 新排版。
 */
export function copyLayout(layout: FlowDetailLayout): FlowDetailLayout {
  return {
    ...layout,
    groups: layout.groups.map(group => ({
      ...group,
      items: group.items.map(item => ({ ...item, detailColumnWidths: { ...item.detailColumnWidths } })),
    })),
  }
}

/**
 * 展开字段路径，明细表本身与子字段都可供节点权限引用。
 * @param data 审批数据定义。
 * @returns 展开的字段引用。
 */
export function fieldReferences(data: FlowDataDefinition): FieldReference[] {
  return data.fields.flatMap(field => [
    { key: field.key, label: field.name, field },
    ...field.children.map(child => ({ key: `${field.key}.${child.key}`, label: `${field.name} · ${child.name}`, field: child })),
  ])
}

/**
 * 检查字段 key 和数据关系；返回可供编辑器展示的中文问题。
 * @param data 审批数据定义。
 * @returns 校验问题清单。
 */
export function validateDataDefinition(data: FlowDataDefinition): string[] {
  const problems: string[] = []
  const seen = new Set<string>()
  for (const field of data.fields) {
    const key = field.key
    if (!key.trim()) problems.push('审批数据字段缺少 key。')
    if (seen.has(key)) problems.push(`字段 key“${key}”重复。`)
    seen.add(key)
    if (field.type !== 'detailTable' && field.children.length) problems.push(`只有明细表“${key}”可以包含子字段。`)
    if (field.type === 'detailTable' && !field.children.length) problems.push(`明细表“${key}”至少需要一个子字段。`)
    const childSeen = new Set<string>()
    for (const child of field.children) {
      const childKey = child.key
      if (!childKey.trim()) problems.push(`明细表“${key}”的子字段缺少 key。`)
      if (childSeen.has(childKey)) problems.push(`明细子字段“${key}.${childKey}”重复。`)
      childSeen.add(childKey)
    }
  }
  const known = new Set(fieldReferences(data).map(reference => reference.key))
  for (const key of data.summaryFields) {
    if (!known.has(key)) problems.push(`摘要字段“${key}”不存在。`)
  }
  return problems
}

/**
 * 给接口文档或业务系统生成字段 JSON 样例；审批人填写与计算字段不要求业务系统提交。
 * @param data 审批数据定义。
 * @returns 数据样例对象。
 */
export function sampleSubmission(data: FlowDataDefinition): Record<string, unknown> {
  const sample: Record<string, unknown> = {}
  for (const field of data.fields) {
    if (field.approverInput || field.formula?.trim()) continue
    if (field.type === 'detailTable') {
      const row: Record<string, unknown> = {}
      for (const child of field.children) {
        if (!child.approverInput && !child.formula?.trim()) row[child.key] = sampleValue(child)
      }
      sample[field.key] = [row]
    } else sample[field.key] = sampleValue(field)
  }
  return sample
}

/**
 * 返回一种字段类型的无业务含义样例。
 * @param field 字段定义。
 * @returns 可序列化样例值。
 */
function sampleValue(field: FlowFieldDefinition): string | number | string[] {
  if (field.type === 'number' || field.type === 'money') return 0
  if (field.type === 'date') return '2026-10-15'
  if (field.type === 'attachment') return []
  return field.type === 'person' ? 'USER_ID' : field.type === 'department' ? 'DEPT_ID' : ''
}

/**
 * 把字段自动排版或自定义排版转换为预览分组，并忽略已删除字段。
 * @param data 审批数据定义。
 * @param layout 详情排版。
 * @returns 用于只读预览的分组。
 */
export function previewGroups(data: FlowDataDefinition, layout: FlowDetailLayout): PreviewGroup[] {
  const lookup = new Map(data.fields.map(field => [field.key, field]))
  if (layout.mode === 'embed') return []
  if (layout.mode !== 'custom') {
    return [{ title: '审批数据', columns: 2, items: data.fields.map(field => ({ field, layout: createLayoutItem(field.key) })) }]
  }
  return layout.groups.map(group => ({
    title: group.title,
    columns: Math.max(1, Math.min(3, group.columns)),
    items: group.items.flatMap(item => {
      const field = lookup.get(item.fieldKey)
      return field ? [{ field, layout: item }] : []
    }),
  }))
}

/**
 * 为一个字段创建默认详情项。
 * @param fieldKey 顶层字段 key。
 * @returns 默认的排版配置。
 */
export function createLayoutItem(fieldKey: string): FlowLayoutItem {
  return { fieldKey, columnSpan: 1, detailColumnWidths: {}, showMoneyUppercase: false }
}

/**
 * 创建一个空的自定义详情分组。
 * @param title 分组名称。
 * @returns 默认两列的分组。
 */
export function createLayoutGroup(title: string): FlowLayoutGroup {
  return { title, columns: 2, items: [] }
}

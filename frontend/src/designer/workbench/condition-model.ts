import type { FlowDataDefinition } from '../../model/flow.ts'
import { fieldReferences } from '../data/data-model.ts'

/**
 * 条件编辑器的一条比较规则。
 */
export interface ConditionRule {
  /**
   * 字段 key，可引用计算字段和明细子字段。
   */
  fieldKey: string
  /**
   * 可选的聚合方式。
   */
  aggregate: '' | 'SUM' | 'AVG' | 'MAX' | 'MIN' | 'ANY'
  /**
   * 比较操作符。
   */
  operator: '>' | '>=' | '<' | '<=' | '==' | '!='
  /**
   * 右侧常量文本。
   */
  value: string
}

/**
 * 同一组内规则的连接方式。
 */
export interface ConditionGroup {
  /**
   * 组内使用且或或。
   */
  join: 'AND' | 'OR'
  /**
   * 条件规则。
   */
  rules: ConditionRule[]
}

/**
 * 新建一条可编辑规则。
 * @param fieldKey 默认字段 key。
 * @returns 条件规则。
 */
export function createConditionRule(fieldKey = ''): ConditionRule {
  return { fieldKey, aggregate: '', operator: '>=', value: '0' }
}

/**
 * 把字段、明细聚合和常量编译成受限表达式；组与组之间按 OR 连接。
 * @param groups 条件组。
 * @param data 审批数据定义。
 * @returns 后端表达式文本。
 */
export function buildCondition(groups: ConditionGroup[], data: FlowDataDefinition): string {
  const references = new Set(fieldReferences(data).map(reference => reference.key))
  return groups.filter(group => group.rules.length).map(group => {
    const parts = group.rules.map(rule => {
      if (!references.has(rule.fieldKey)) throw new Error(`字段“${rule.fieldKey}”不存在。`)
      const reference = fieldReferences(data).find(item => item.key === rule.fieldKey)!
      if (rule.aggregate && !rule.fieldKey.includes('.') && rule.aggregate !== 'ANY') throw new Error('数值聚合请选择明细表子字段。')
      if (rule.aggregate && !['number', 'money'].includes(reference.field.type) && rule.aggregate !== 'ANY') throw new Error('数值聚合只能使用数字或金额字段。')
      const referenceText = '$' + '{' + rule.fieldKey + '}'
      const left = rule.aggregate ? `${rule.aggregate}(${referenceText})` : referenceText
      const raw = rule.value.trim()
      if (!raw) throw new Error('条件比较值不能为空。')
      const right = /^-?\d+(?:\.\d+)?$/.test(raw) || raw === 'true' || raw === 'false' ? raw : `'${raw.replace(/\\/g, '\\\\').replace(/'/g, "\\'")}'`
      return `${left} ${rule.operator} ${right}`
    })
    return `(${parts.join(` ${group.join} `)})`
  }).join(' OR ')
}

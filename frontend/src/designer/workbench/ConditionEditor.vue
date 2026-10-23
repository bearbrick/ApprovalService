<script setup lang="ts">
import { computed, ref } from 'vue'
import type { FlowDataDefinition } from '../../model/flow.ts'
import { fieldReferences } from '../data/data-model.ts'
import { buildCondition, createConditionRule } from './condition-model.ts'
import type { ConditionGroup, ConditionRule } from './condition-model.ts'

/**
 * 条件编辑器输入。
 */
const props = defineProps<{
  /**
   * 当前条件表达式。
   */
  condition: string
  /**
   * 字段和计算字段定义。
   */
  data: FlowDataDefinition
}>()
/**
 * 条件表达式变化。
 */
const emit = defineEmits<{
  /**
   * 更新线上的正式受限表达式。
   */
  'update:condition': [value: string]
}>()
/**
 * 可供选择的字段，明细子字段也可做聚合。
 */
const fields = computed(() => fieldReferences(props.data).filter(item => item.field.type !== 'detailTable'))
/**
 * 结构化构建器的条件组。
 */
const groups = ref<ConditionGroup[]>([{ join: 'AND', rules: [createConditionRule()] }])
/**
 * 结构化构建时可纠正的问题。
 */
const error = ref('')
/**
 * 保留表达式原文的编辑方式。
 */
const rawMode = ref(Boolean(props.condition))

/**
 * 增加一组条件；各组之间为“或”。
 */
function addGroup(): void {
  groups.value.push({ join: 'AND', rules: [createConditionRule()] })
}
/**
 * 增加当前组内的一条规则。
 * @param group 当前条件组。
 */
function addRule(group: ConditionGroup): void {
  group.rules.push(createConditionRule())
}
/**
 * 用结构化条件生成正式表达式。
 */
function applyBuilder(): void {
  try {
    error.value = ''
    emit('update:condition', buildCondition(groups.value, props.data))
  } catch (problem) {
    error.value = problem instanceof Error ? problem.message : '条件格式不正确。'
  }
}
/**
 * 更新一条规则的某项设置。
 * @param rule 要修改的规则。
 * @param key 要修改的属性。
 * @param value 输入值。
 */
function updateRule(rule: ConditionRule, key: keyof ConditionRule, value: string): void {
  Object.assign(rule, { [key]: value })
}
</script>

<template>
  <section class="condition-editor">
    <div class="condition-mode"><strong>路线条件</strong><button type="button" class="text-button" @click="rawMode = !rawMode">{{ rawMode && condition ? '用新条件替换当前表达式' : rawMode ? '使用条件构建器' : '编辑表达式原文' }}</button></div>
    <p class="hint">条件只使用审批数据与发起人上下文；多组之间按“或”连接。计算字段可直接引用，明细列可选择合计。</p>
    <p v-if="!rawMode && condition" class="hint">当前表达式不会自动转成构建器规则；点击“应用条件”会替换它。</p>
    <template v-if="!rawMode">
      <div v-for="(group, groupIndex) in groups" :key="groupIndex" class="condition-group">
        <div class="condition-group-head"><strong>第 {{ groupIndex + 1 }} 组</strong><label>组内关系<select v-model="group.join"><option value="AND">且 · 全部满足</option><option value="OR">或 · 任一满足</option></select></label><button type="button" class="text-button" :disabled="groups.length === 1" @click="groups.splice(groupIndex, 1)">移除组</button></div>
        <div v-for="(rule, index) in group.rules" :key="index" class="condition-rule">
          <select :aria-label="`第 ${groupIndex + 1} 组第 ${index + 1} 条字段`" :value="rule.fieldKey" @change="updateRule(rule, 'fieldKey', ($event.target as HTMLSelectElement).value)"><option value="">选择字段</option><option v-for="field in fields" :key="field.key" :value="field.key">{{ field.label }}{{ field.field.formula ? ' · 计算' : '' }}</option></select>
          <select :aria-label="`第 ${index + 1} 条聚合`" :value="rule.aggregate" @change="updateRule(rule, 'aggregate', ($event.target as HTMLSelectElement).value)"><option value="">直接比较</option><option value="SUM">明细合计</option><option value="AVG">明细平均</option><option value="MAX">明细最大</option><option value="MIN">明细最小</option><option value="ANY">明细任一</option></select>
          <select :aria-label="`第 ${index + 1} 条操作符`" :value="rule.operator" @change="updateRule(rule, 'operator', ($event.target as HTMLSelectElement).value)"><option v-for="operator in ['>', '>=', '<', '<=', '==', '!=']" :key="operator" :value="operator">{{ operator }}</option></select>
          <input :aria-label="`第 ${index + 1} 条比较值`" :value="rule.value" placeholder="比较值" @input="updateRule(rule, 'value', ($event.target as HTMLInputElement).value)" />
          <button type="button" class="text-button" :disabled="group.rules.length === 1" @click="group.rules.splice(index, 1)">移除</button>
        </div>
        <button type="button" class="text-button" @click="addRule(group)">＋ 添加组内条件</button>
      </div>
      <div class="condition-actions"><button type="button" class="text-button" @click="addGroup">＋ 添加或条件组</button><button type="button" class="btn btn-primary" @click="applyBuilder">应用条件</button></div>
    </template>
    <label v-else class="field-label">受限表达式<textarea :value="condition" rows="5" spellcheck="false" placeholder="${totalAmount} >= 50000 AND ${initiator.department} == 'FINANCE'" @input="emit('update:condition', ($event.target as HTMLTextAreaElement).value)" /></label>
    <p v-if="error" class="input-error" role="alert">{{ error }}</p>
    <p v-if="condition" class="condition-preview"><span>当前路线条件</span><code>{{ condition }}</code></p>
  </section>
</template>

<style scoped>
.condition-editor{padding:14px;border:1px solid var(--line);border-radius:6px;background:#fafcff}.condition-mode,.condition-group-head,.condition-actions{display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap}.condition-mode strong{font-size:13px}.hint{margin:8px 0 14px;color:var(--muted);font-size:12px;line-height:1.6}.condition-group{padding:12px;margin:10px 0;border:1px solid var(--line);border-radius:5px;background:#fff}.condition-group-head label{display:flex;align-items:center;gap:6px;color:var(--muted);font-size:12px}.condition-rule{display:grid;grid-template-columns:minmax(110px,2fr) minmax(90px,1fr) 64px minmax(80px,1fr) auto;gap:6px;margin:10px 0}.condition-rule select,.condition-rule input,.condition-group-head select,.field-label textarea{width:100%;min-width:0;padding:6px;border:1px solid #cbd5e1;border-radius:4px;background:#fff}.text-button{padding:4px;border:0;background:none;color:var(--blue);white-space:nowrap}.condition-actions{margin-top:12px}.field-label{display:grid;gap:6px;font-weight:600}.field-label textarea{font:12px Consolas,monospace;resize:vertical}.condition-preview{display:grid;gap:4px;margin-top:12px;color:var(--muted)}.condition-preview code{display:block;padding:8px;overflow-wrap:anywhere;background:#edf3fd;color:var(--ink)}.input-error{margin-top:8px;color:var(--danger)}@media(max-width:750px){.condition-rule{grid-template-columns:1fr 1fr}.condition-rule select:first-child{grid-column:1/-1}}
</style>

<script setup lang="ts">
import { computed } from 'vue'
import type { FlowDataDefinition, FlowFieldDefinition, FlowFieldType } from '../../model/flow.ts'
import { copyData, createField, sampleSubmission, validateDataDefinition } from './data-model.ts'

/**
 * 审批数据字段编辑器的输入。
 */
const props = defineProps<{
  /**
   * 当前流程草稿的审批数据定义。
   */
  data: FlowDataDefinition
}>()

/**
 * 每次修改都传回独立的新定义。
 */
const emit = defineEmits<{
  /**
   * 数据定义变化。
   */
  'update:data': [value: FlowDataDefinition]
}>()

/**
 * 与正式契约一致的字段类型清单。
 */
const fieldTypes: { value: FlowFieldType; label: string }[] = [
  { value: 'text', label: '文本' }, { value: 'number', label: '数字' },
  { value: 'money', label: '金额' }, { value: 'date', label: '日期' },
  { value: 'person', label: '人员' }, { value: 'department', label: '部门' },
  { value: 'attachment', label: '附件' }, { value: 'detailTable', label: '明细表' },
]

/**
 * 当前编辑问题，仅用于设计时反馈，发布仍以服务端校验为准。
 */
const problems = computed(() => validateDataDefinition(props.data))

/**
 * 业务系统提交审批数据时可参考的 JSON 样例。
 */
const sampleJson = computed(() => JSON.stringify(sampleSubmission(props.data), null, 2))

/**
 * 创建一个不与现有字段冲突的 key。
 * @param base 建议的前缀。
 * @param fields 同层字段。
 * @returns 可直接编辑的 key。
 */
function nextKey(base: string, fields: FlowFieldDefinition[]): string {
  let index = fields.length + 1
  while (fields.some(field => field.key === `${base}${index}`)) index++
  return `${base}${index}`
}

/**
 * 增加顶层字段。
 */
function addField(): void {
  const next = copyData(props.data)
  next.fields.push(createField(nextKey('field', next.fields), '新字段'))
  emit('update:data', next)
}

/**
 * 修改一个顶层字段的属性。
 * @param index 顶层字段序号。
 * @param property 要修改的属性。
 * @param value 新值。
 */
function updateField(index: number, property: 'key' | 'name' | 'type' | 'required' | 'approverInput' | 'formula', value: string | boolean): void {
  const next = copyData(props.data)
  const field = next.fields[index]
  if (!field) return
  if (property === 'key') {
    const oldKey = field.key
    field.key = String(value)
    next.summaryFields = next.summaryFields.map(key => key === oldKey ? field.key : key)
  } else if (property === 'name') field.name = String(value)
  else if (property === 'required') field.required = Boolean(value)
  else if (property === 'approverInput') field.approverInput = Boolean(value)
  else if (property === 'formula') field.formula = String(value) || null
  else {
    field.type = value as FlowFieldType
    if (field.type !== 'detailTable') field.children = []
    else next.summaryFields = next.summaryFields.filter(key => key !== field.key)
  }
  emit('update:data', next)
}

/**
 * 删除顶层字段，并移除对应摘要引用。
 * @param index 顶层字段序号。
 */
function removeField(index: number): void {
  const next = copyData(props.data)
  const removed = next.fields.splice(index, 1)[0]
  if (removed) next.summaryFields = next.summaryFields.filter(key => key !== removed.key)
  emit('update:data', next)
}

/**
 * 在同层调整字段顺序。
 * @param index 当前字段序号。
 * @param delta 移动方向。
 */
function moveField(index: number, delta: number): void {
  const next = copyData(props.data)
  const target = index + delta
  if (target < 0 || target >= next.fields.length) return
  const [field] = next.fields.splice(index, 1)
  if (field) next.fields.splice(target, 0, field)
  emit('update:data', next)
}

/**
 * 开关待办摘要字段。
 * @param field 当前字段。
 */
function toggleSummary(field: FlowFieldDefinition): void {
  if (field.type === 'detailTable') return
  const next = copyData(props.data)
  next.summaryFields = next.summaryFields.includes(field.key)
    ? next.summaryFields.filter(key => key !== field.key)
    : [...next.summaryFields, field.key]
  emit('update:data', next)
}

/**
 * 给明细表增加子字段。
 * @param tableIndex 明细表序号。
 */
function addChild(tableIndex: number): void {
  const next = copyData(props.data)
  const table = next.fields[tableIndex]
  if (!table || table.type !== 'detailTable') return
  table.children.push(createField(nextKey('column', table.children), '新明细列'))
  emit('update:data', next)
}

/**
 * 修改明细表子字段。
 * @param tableIndex 明细表序号。
 * @param childIndex 子字段序号。
 * @param property 要修改的属性。
 * @param value 新值。
 */
function updateChild(tableIndex: number, childIndex: number, property: 'key' | 'name' | 'type' | 'required' | 'formula', value: string | boolean): void {
  const next = copyData(props.data)
  const child = next.fields[tableIndex]?.children[childIndex]
  if (!child) return
  if (property === 'key') child.key = String(value)
  else if (property === 'name') child.name = String(value)
  else if (property === 'required') child.required = Boolean(value)
  else if (property === 'formula') child.formula = String(value) || null
  else child.type = value as FlowFieldType
  emit('update:data', next)
}

/**
 * 删除一个明细子字段。
 * @param tableIndex 明细表序号。
 * @param childIndex 子字段序号。
 */
function removeChild(tableIndex: number, childIndex: number): void {
  const next = copyData(props.data)
  next.fields[tableIndex]?.children.splice(childIndex, 1)
  emit('update:data', next)
}
</script>

<template>
  <div class="data-editor">
    <div class="data-intro">业务单据由业务系统维护。这里定义提交时的数据快照、审批判断所需字段，以及审批人获授权后填写的字段。</div>
    <div class="data-toolbar"><div><h2>审批数据字段</h2><p>key 是业务系统接口使用的稳定字段标识，发布后变更需发布新版本。</p></div><button class="btn btn-primary" type="button" @click="addField">＋ 添加字段</button></div>
    <div v-if="!data.fields.length" class="data-empty">尚无字段。添加字段后可配置摘要和只读详情排版。</div>
    <div v-for="(field, index) in data.fields" :key="index" class="field-card">
      <div class="field-card-head"><strong>{{ field.name || '未命名字段' }}</strong><span class="badge">{{ field.key || '未填写 key' }}</span><span class="field-spacer"></span><button class="btn" type="button" :disabled="index === 0" :aria-label="`上移 ${field.name}`" @click="moveField(index, -1)">↑</button><button class="btn" type="button" :disabled="index === data.fields.length - 1" :aria-label="`下移 ${field.name}`" @click="moveField(index, 1)">↓</button><button class="btn field-danger" type="button" :aria-label="`删除 ${field.name}`" @click="removeField(index)">删除</button></div>
      <div class="field-grid">
        <label>字段 key<input :value="field.key" @input="updateField(index, 'key', ($event.target as HTMLInputElement).value)" /></label>
        <label>显示名称<input :value="field.name" @input="updateField(index, 'name', ($event.target as HTMLInputElement).value)" /></label>
        <label>数据类型<select :value="field.type" @change="updateField(index, 'type', ($event.target as HTMLSelectElement).value)"><option v-for="type in fieldTypes" :key="type.value" :value="type.value">{{ type.label }}</option></select></label>
      </div>
      <div class="field-options"><label><input type="checkbox" :checked="field.required" @change="updateField(index, 'required', ($event.target as HTMLInputElement).checked)" /> 必传</label><label><input type="checkbox" :checked="field.approverInput" :disabled="field.type === 'detailTable' || !!field.formula" @change="updateField(index, 'approverInput', ($event.target as HTMLInputElement).checked)" /> 审批人填写</label><label><input type="checkbox" :checked="data.summaryFields.includes(field.key)" :disabled="field.type === 'detailTable'" @change="toggleSummary(field)" /> 待办摘要</label></div>
      <label v-if="field.type !== 'detailTable'" class="formula-label">计算表达式 <span>由服务端安全计算，不执行脚本；例如 SUM(${items.amount})</span><input :value="field.formula || ''" :disabled="field.approverInput" placeholder="留空表示由业务系统提交" @input="updateField(index, 'formula', ($event.target as HTMLInputElement).value)" /></label>
      <div v-if="field.type === 'detailTable'" class="children"><div class="children-head"><strong>明细列</strong><button class="btn" type="button" @click="addChild(index)">＋ 添加列</button></div><div v-for="(child, childIndex) in field.children" :key="childIndex" class="child-row"><label>key<input :value="child.key" @input="updateChild(index, childIndex, 'key', ($event.target as HTMLInputElement).value)" /></label><label>名称<input :value="child.name" @input="updateChild(index, childIndex, 'name', ($event.target as HTMLInputElement).value)" /></label><label>类型<select :value="child.type" @change="updateChild(index, childIndex, 'type', ($event.target as HTMLSelectElement).value)"><option v-for="type in fieldTypes.filter(item => item.value !== 'detailTable')" :key="type.value" :value="type.value">{{ type.label }}</option></select></label><label class="child-required"><input type="checkbox" :checked="child.required" @change="updateChild(index, childIndex, 'required', ($event.target as HTMLInputElement).checked)" /> 必传</label><button class="btn field-danger" type="button" :aria-label="`删除 ${child.name}`" @click="removeChild(index, childIndex)">删除</button></div></div>
    </div>
    <div class="data-bottom"><section class="panel data-panel"><div class="panel-heading"><h2>摘要字段</h2><span class="badge">{{ data.summaryFields.length }} 个</span></div><p>建议选 3～5 个关键字段，用于待办列表和通知。</p><div class="summary-chips"><span v-for="key in data.summaryFields" :key="key" class="badge">{{ data.fields.find(field => field.key === key)?.name || key }}</span><span v-if="!data.summaryFields.length" class="muted">尚未选择</span></div></section><section class="panel data-panel"><div class="panel-heading"><h2>提交数据 JSON 样例</h2></div><p>给业务系统对接使用；计算字段和审批人填写字段无需提交。</p><pre>{{ sampleJson }}</pre></section></div>
    <div v-if="problems.length" class="data-errors" role="status"><strong>请完善字段定义</strong><ul><li v-for="(problem, index) in problems" :key="index">{{ problem }}</li></ul></div>
  </div>
</template>

<style scoped>
.data-editor{display:grid;gap:18px;padding:20px 0 28px}.data-intro{padding:12px 15px;border:1px solid #cbdcf8;background:#f2f7ff;color:#27518d;border-radius:5px}.data-toolbar,.field-card-head,.children-head{display:flex;align-items:center;gap:10px;justify-content:space-between}.data-toolbar h2{font-size:16px}.data-toolbar p,.data-panel p{color:var(--muted);margin-top:4px}.data-empty{padding:45px;text-align:center;border:1px dashed var(--line);color:var(--muted)}.field-card{border:1px solid var(--line);border-radius:6px;overflow:hidden}.field-card-head{padding:10px 14px;background:#f8faff;border-bottom:1px solid var(--line)}.field-card-head .btn{min-height:29px;padding:3px 9px}.field-spacer{flex:1}.field-danger{color:var(--danger)}.field-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;padding:15px}.field-grid label,.child-row label,.formula-label{display:grid;gap:5px;color:var(--muted);font-size:12px}.field-grid input,.field-grid select,.child-row input,.child-row select,.formula-label input{width:100%;min-height:34px;padding:6px 8px;border:1px solid #cbd5e1;border-radius:4px;color:var(--ink);background:#fff}.field-options{display:flex;gap:22px;flex-wrap:wrap;padding:0 15px 13px}.field-options label{display:flex;align-items:center;gap:5px}.formula-label{padding:0 15px 15px}.formula-label span{font-weight:400}.children{margin:0 15px 15px;padding:12px;border:1px solid var(--line);background:#fbfcff}.children-head{margin-bottom:10px}.child-row{display:grid;grid-template-columns:1fr 1fr 140px 65px auto;gap:8px;align-items:end;margin-top:8px}.child-required{display:flex!important;align-items:center;min-height:34px}.data-bottom{display:grid;grid-template-columns:1fr 1fr;gap:16px}.data-panel{padding-bottom:15px}.data-panel p,.data-panel pre,.summary-chips{margin:12px 16px}.data-panel pre{overflow:auto;background:#f6f8fb;padding:12px;font-size:12px}.summary-chips{display:flex;gap:6px;flex-wrap:wrap}.data-errors{padding:12px 16px;border:1px solid #efc7cd;background:#fff6f7;color:#9a3340;border-radius:5px}.data-errors ul{margin:8px 0 0;padding-left:18px}@media(max-width:760px){.field-grid,.data-bottom{grid-template-columns:1fr}.child-row{grid-template-columns:1fr 1fr}.data-toolbar{align-items:start}.field-card-head{flex-wrap:wrap}}
</style>

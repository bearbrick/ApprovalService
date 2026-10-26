<script setup lang="ts">
import { computed, ref } from 'vue'
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
 * 当前展开的公式或明细行；折叠只改变展示，不修改字段定义。
 */
const expandedIndex = ref<number | null>(null)

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
    else {
      next.summaryFields = next.summaryFields.filter(key => key !== field.key)
      expandedIndex.value = index
    }
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
  if (expandedIndex.value === index) expandedIndex.value = null
  else if (expandedIndex.value !== null && expandedIndex.value > index) expandedIndex.value--
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
  // 展开状态跟随移动的字段，避免排序后显示到另一条配置上。
  if (expandedIndex.value === index) expandedIndex.value = target
  else if (expandedIndex.value === target) expandedIndex.value = index
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
    <div class="data-toolbar"><div><h2>审批数据字段 <span class="field-count">{{ data.fields.length }} 个</span></h2><p>定义业务系统提交的数据快照；key 发布后变更需发布新版本。</p></div><button class="btn btn-primary" type="button" @click="addField">＋ 添加字段</button></div>
    <div v-if="!data.fields.length" class="data-empty">尚无字段。添加字段后可配置摘要和只读详情排版。</div>
    <div v-else class="field-table-scroll" role="region" aria-label="审批字段定义" tabindex="0">
      <table class="field-table"><colgroup><col class="number-column" /><col class="key-column" /><col class="name-column" /><col class="type-column" /><col class="check-column" /><col class="input-column" /><col class="summary-column" /><col class="config-column" /><col class="action-column" /></colgroup>
        <thead><tr><th scope="col">#</th><th scope="col">字段 key</th><th scope="col">显示名称</th><th scope="col">数据类型</th><th scope="col">必传</th><th scope="col">审批人填写</th><th scope="col">待办摘要</th><th scope="col">配置</th><th scope="col">操作</th></tr></thead>
        <tbody><template v-for="(field, index) in data.fields" :key="index">
          <tr class="field-row" :class="{ expanded: expandedIndex === index }">
            <td class="field-number">{{ index + 1 }}</td>
            <td><input :aria-label="`字段 key ${index + 1}`" :value="field.key" @input="updateField(index, 'key', ($event.target as HTMLInputElement).value)" /></td>
            <td><input :aria-label="`显示名称 ${index + 1}`" :value="field.name" @input="updateField(index, 'name', ($event.target as HTMLInputElement).value)" /></td>
            <td><select :aria-label="`数据类型 ${field.name}`" :value="field.type" @change="updateField(index, 'type', ($event.target as HTMLSelectElement).value)"><option v-for="type in fieldTypes" :key="type.value" :value="type.value">{{ type.label }}</option></select></td>
            <td class="field-check"><input type="checkbox" :aria-label="`必传 ${field.name}`" :checked="field.required" @change="updateField(index, 'required', ($event.target as HTMLInputElement).checked)" /></td>
            <td class="field-check"><input type="checkbox" :aria-label="`审批人填写 ${field.name}`" :checked="field.approverInput" :disabled="field.type === 'detailTable' || !!field.formula" @change="updateField(index, 'approverInput', ($event.target as HTMLInputElement).checked)" /></td>
            <td class="field-check"><input type="checkbox" :aria-label="`待办摘要 ${field.name}`" :checked="data.summaryFields.includes(field.key)" :disabled="field.type === 'detailTable'" @change="toggleSummary(field)" /></td>
            <td><button type="button" class="field-config" :class="{ configured: field.type === 'detailTable' || !!field.formula }" :aria-label="`配置 ${field.name}`" :aria-expanded="expandedIndex === index" @click="expandedIndex = expandedIndex === index ? null : index">{{ field.type === 'detailTable' ? `明细 ${field.children.length} 列` : field.formula ? '已设公式' : '公式' }} <span aria-hidden="true">{{ expandedIndex === index ? '⌃' : '⌄' }}</span></button></td>
            <td><div class="field-actions"><button class="btn" type="button" :disabled="index === 0" :aria-label="`上移 ${field.name}`" @click="moveField(index, -1)">↑</button><button class="btn" type="button" :disabled="index === data.fields.length - 1" :aria-label="`下移 ${field.name}`" @click="moveField(index, 1)">↓</button><button class="btn field-danger" type="button" :aria-label="`删除 ${field.name}`" @click="removeField(index)">删除</button></div></td>
          </tr>
          <tr v-if="expandedIndex === index" class="field-expansion"><td colspan="9">
            <div v-if="field.type === 'detailTable'" class="children"><div class="children-head"><strong>{{ field.name || '明细表' }} · 明细列</strong><button class="btn" type="button" @click="addChild(index)">＋ 添加列</button></div><p v-if="!field.children.length" class="muted">尚无明细列，请添加。</p><div v-for="(child, childIndex) in field.children" :key="childIndex" class="child-row"><label>key<input :value="child.key" @input="updateChild(index, childIndex, 'key', ($event.target as HTMLInputElement).value)" /></label><label>名称<input :value="child.name" @input="updateChild(index, childIndex, 'name', ($event.target as HTMLInputElement).value)" /></label><label>类型<select :value="child.type" @change="updateChild(index, childIndex, 'type', ($event.target as HTMLSelectElement).value)"><option v-for="type in fieldTypes.filter(item => item.value !== 'detailTable')" :key="type.value" :value="type.value">{{ type.label }}</option></select></label><label class="child-required"><input type="checkbox" :checked="child.required" @change="updateChild(index, childIndex, 'required', ($event.target as HTMLInputElement).checked)" /> 必传</label><button class="btn field-danger" type="button" :aria-label="`删除 ${child.name}`" @click="removeChild(index, childIndex)">删除</button></div></div>
            <label v-else class="formula-label">{{ field.name || '当前字段' }} · 计算表达式<input :value="field.formula || ''" :disabled="field.approverInput" placeholder="留空表示由业务系统提交" @input="updateField(index, 'formula', ($event.target as HTMLInputElement).value)" /><span>{{ field.approverInput ? '审批人填写的字段不能同时配置计算表达式。' : '由服务端安全计算，不执行脚本；例如 SUM(${items.amount})。' }}</span></label>
          </td></tr>
        </template></tbody>
      </table>
    </div>
    <div v-if="problems.length" class="data-errors" role="status"><strong>请完善字段定义</strong><ul><li v-for="(problem, index) in problems" :key="index">{{ problem }}</li></ul></div>
    <div class="data-bottom"><details class="data-panel"><summary>摘要字段 <span class="badge">{{ data.summaryFields.length }} 个</span></summary><p>建议选 3～5 个关键字段，用于待办列表和通知。</p><div class="summary-chips"><span v-for="key in data.summaryFields" :key="key" class="badge">{{ data.fields.find(field => field.key === key)?.name || key }}</span><span v-if="!data.summaryFields.length" class="muted">尚未选择</span></div></details><details class="data-panel"><summary>提交数据 JSON 样例</summary><p>给业务系统对接使用；计算字段和审批人填写字段无需提交。</p><pre>{{ sampleJson }}</pre></details></div>
  </div>
</template>

<style scoped>
.data-editor { display: grid; gap: 12px; padding: 0 0 16px; }
.data-toolbar, .children-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.data-toolbar h2 { margin: 0; font-size: 15px; }
.field-count { margin-left: 6px; color: var(--muted); font-size: 12px; font-weight: 400; }
.data-toolbar p { margin-top: 3px; color: var(--muted); font-size: 12px; }
.data-toolbar .btn { flex: none; min-height: 32px; padding: 5px 10px; font-size: 12px; }
.data-empty { padding: 32px; text-align: center; border: 1px dashed var(--line); color: var(--muted); }
.field-table-scroll { overflow-x: auto; border: 1px solid var(--line); border-radius: 6px; background: #fff; }
.field-table { width: 100%; min-width: 940px; border-collapse: collapse; table-layout: fixed; font-size: 12px; }
.number-column { width: 38px; }.key-column { width: 23%; }.name-column { width: 23%; }.type-column { width: 130px; }.check-column { width: 54px; }.input-column { width: 90px; }.summary-column { width: 80px; }.config-column { width: 100px; }.action-column { width: 124px; }
.field-table th { height: 36px; padding: 6px 8px; background: #f5f8fd; text-align: left; font-weight: 500; color: var(--muted); white-space: nowrap; border-bottom: 1px solid var(--line); }
.field-table th:nth-child(1), .field-table th:nth-child(5), .field-table th:nth-child(6), .field-table th:nth-child(7) { text-align: center; }
.field-row td { height: 46px; padding: 6px 8px; border-bottom: 1px solid #edf1f6; }
.field-row:last-child td { border-bottom: 0; }.field-row:hover, .field-row.expanded { background: #f6f9ff; }
.field-number, .field-check { text-align: center; color: var(--muted); }
.field-row input:not([type=checkbox]), .field-row select, .child-row input:not([type=checkbox]), .child-row select, .formula-label input { width: 100%; min-height: 30px; padding: 4px 7px; border: 1px solid #d9e1ec; border-radius: 4px; color: var(--ink); background: #fff; font-size: 12px; }
.field-row input:focus, .field-row select:focus { border-color: var(--blue); outline: 2px solid #dbe8ff; outline-offset: 0; }
.field-check input { width: 14px; height: 14px; cursor: pointer; accent-color: var(--blue); }
.field-actions { display: flex; gap: 4px; }.field-actions .btn { min-height: 28px; min-width: 24px; padding: 3px 6px; font-size: 12px; }
.field-danger { color: var(--danger); }.field-config { display: inline-flex; align-items: center; gap: 6px; padding: 4px 0; border: 0; background: none; color: var(--muted); cursor: pointer; white-space: nowrap; font-size: 12px; }.field-config.configured, .field-config:hover { color: var(--blue); }
.field-expansion > td { padding: 14px 46px; border-bottom: 1px solid var(--line); background: #f7faff; }
.formula-label, .child-row label { display: grid; gap: 6px; color: var(--ink); font-size: 12px; }.formula-label { max-width: 1000px; }.formula-label span { color: var(--muted); font-size: 11px; }
.children { max-width: 1100px; }.children-head { margin-bottom: 10px; }.children-head .btn { min-height: 28px; padding: 3px 8px; font-size: 12px; }
.child-row { display: grid; grid-template-columns: 1fr 1fr 130px 65px auto; gap: 8px; align-items: end; margin-top: 8px; }.child-required { display: flex!important; align-items: center; min-height: 30px; }.child-row .btn { min-height: 30px; padding: 4px 8px; font-size: 12px; }
.data-bottom { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }.data-panel { border: 1px solid var(--line); border-radius: 5px; background: #fff; }.data-panel summary { padding: 10px 12px; cursor: pointer; font-size: 12px; font-weight: 600; }.data-panel summary .badge { margin-left: 6px; }.data-panel p, .data-panel pre, .summary-chips { margin: 10px 12px; font-size: 12px; }.data-panel p { color: var(--muted); }.data-panel pre { overflow: auto; max-height: 240px; background: #f6f8fb; padding: 12px; }.summary-chips { display: flex; gap: 6px; flex-wrap: wrap; }
.data-errors { padding: 10px 12px; border: 1px solid #efc7cd; background: #fff6f7; color: #9a3340; border-radius: 5px; }.data-errors ul { margin: 6px 0 0; padding-left: 18px; }
@media (max-width: 760px) { .data-bottom { grid-template-columns: 1fr; }.data-toolbar { align-items: start; }.field-expansion > td { padding: 12px; } }
</style>

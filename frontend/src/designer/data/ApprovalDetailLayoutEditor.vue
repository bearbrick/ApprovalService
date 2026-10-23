<script setup lang="ts">
import { computed, ref } from 'vue'
import type { FlowDataDefinition, FlowDetailLayout, FlowFieldDefinition } from '../../model/flow.ts'
import { copyLayout, createLayoutGroup, createLayoutItem, previewGroups } from './data-model.ts'

/**
 * 审批只读详情排版编辑器的输入。
 */
const props = defineProps<{
  /**
   * 可展示的审批字段。
   */
  data: FlowDataDefinition
  /**
   * 当前流程草稿的详情排版。
   */
  detailLayout: FlowDetailLayout
}>()

/**
 * 排版变化时传回新的配置。
 */
const emit = defineEmits<{
  /**
   * 详情排版变化。
   */
  'update:detailLayout': [value: FlowDetailLayout]
}>()

/**
 * 拖拽中的顶层字段 key。
 */
const draggingField = ref<string | null>(null)

/**
 * 预览展示用的分组。
 */
const groups = computed(() => previewGroups(props.data, props.detailLayout))

/**
 * 尚未放入自定义布局的字段。
 */
const availableFields = computed(() => {
  const placed = new Set(props.detailLayout.groups.flatMap(group => group.items.map(item => item.fieldKey)))
  return props.data.fields.filter(field => !placed.has(field.key))
})

/**
 * 当前展示模式。
 * @param mode auto、custom 或 embed。
 */
function setMode(mode: string): void {
  const next = copyLayout(props.detailLayout)
  next.mode = mode
  if (mode === 'custom' && !next.groups.length) next.groups = [createLayoutGroup('基本信息')]
  emit('update:detailLayout', next)
}

/**
 * 修改嵌入只读原单的页面编码。
 * @param code 接入应用登记的页面编码。
 */
function setEmbedCode(code: string): void {
  const next = copyLayout(props.detailLayout)
  next.embedPageCode = code
  emit('update:detailLayout', next)
}

/**
 * 增加详情分组。
 */
function addGroup(): void {
  const next = copyLayout(props.detailLayout)
  next.groups.push(createLayoutGroup(`分组 ${next.groups.length + 1}`))
  emit('update:detailLayout', next)
}

/**
 * 更新分组属性。
 * @param index 分组序号。
 * @param property 标题或列数。
 * @param value 属性值。
 */
function updateGroup(index: number, property: 'title' | 'columns', value: string): void {
  const next = copyLayout(props.detailLayout)
  const group = next.groups[index]
  if (!group) return
  if (property === 'title') group.title = value
  else group.columns = Number(value)
  group.items.forEach(item => { item.columnSpan = Math.min(item.columnSpan, group.columns) })
  emit('update:detailLayout', next)
}

/**
 * 删除分组；字段仅移出布局，不从审批数据定义中删除。
 * @param index 分组序号。
 */
function removeGroup(index: number): void {
  const next = copyLayout(props.detailLayout)
  next.groups.splice(index, 1)
  emit('update:detailLayout', next)
}

/**
 * 调整分组顺序。
 * @param index 当前序号。
 * @param delta 移动方向。
 */
function moveGroup(index: number, delta: number): void {
  const next = copyLayout(props.detailLayout)
  const target = index + delta
  if (target < 0 || target >= next.groups.length) return
  const [group] = next.groups.splice(index, 1)
  if (group) next.groups.splice(target, 0, group)
  emit('update:detailLayout', next)
}

/**
 * 把字段加入指定分组，防止同一字段重复展示。
 * @param groupIndex 目标分组序号。
 * @param fieldKey 顶层字段 key。
 */
function addItem(groupIndex: number, fieldKey: string): void {
  if (!props.data.fields.some(field => field.key === fieldKey)) return
  const next = copyLayout(props.detailLayout)
  if (next.groups.some(group => group.items.some(item => item.fieldKey === fieldKey))) return
  next.groups[groupIndex]?.items.push(createLayoutItem(fieldKey))
  emit('update:detailLayout', next)
}

/**
 * 从指定分组移除字段。
 * @param groupIndex 分组序号。
 * @param itemIndex 字段序号。
 */
function removeItem(groupIndex: number, itemIndex: number): void {
  const next = copyLayout(props.detailLayout)
  next.groups[groupIndex]?.items.splice(itemIndex, 1)
  emit('update:detailLayout', next)
}

/**
 * 在分组内调整字段顺序。
 * @param groupIndex 分组序号。
 * @param itemIndex 当前字段序号。
 * @param delta 移动方向。
 */
function moveItem(groupIndex: number, itemIndex: number, delta: number): void {
  const next = copyLayout(props.detailLayout)
  const items = next.groups[groupIndex]?.items
  if (!items || itemIndex + delta < 0 || itemIndex + delta >= items.length) return
  const [item] = items.splice(itemIndex, 1)
  if (item) items.splice(itemIndex + delta, 0, item)
  emit('update:detailLayout', next)
}

/**
 * 修改字段跨度、大写金额或明细列宽。
 * @param groupIndex 分组序号。
 * @param itemIndex 字段序号。
 * @param property 要修改的设置。
 * @param value 新值。
 * @param childKey 明细子字段 key，仅修改列宽时使用。
 */
function updateItem(groupIndex: number, itemIndex: number, property: 'columnSpan' | 'showMoneyUppercase' | 'detailColumnWidths', value: number | boolean, childKey?: string): void {
  const next = copyLayout(props.detailLayout)
  const item = next.groups[groupIndex]?.items[itemIndex]
  if (!item) return
  if (property === 'columnSpan') item.columnSpan = Math.max(1, Math.min(next.groups[groupIndex]?.columns ?? 1, Number(value)))
  else if (property === 'showMoneyUppercase') item.showMoneyUppercase = Boolean(value)
  else if (childKey) item.detailColumnWidths[childKey] = Math.max(1, Math.min(100, Number(value) || 1))
  emit('update:detailLayout', next)
}

/**
 * 开始拖拽左侧字段卡片。
 * @param fieldKey 字段 key。
 * @param event 浏览器拖拽事件。
 */
function startDrag(fieldKey: string, event: DragEvent): void {
  draggingField.value = fieldKey
  event.dataTransfer?.setData('text/plain', fieldKey)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy'
}

/**
 * 放下字段；键盘用户也可使用分组的下拉框。
 * @param groupIndex 目标分组序号。
 * @param event 浏览器拖拽事件。
 */
function dropField(groupIndex: number, event: DragEvent): void {
  event.preventDefault()
  const key = event.dataTransfer?.getData('text/plain') || draggingField.value
  if (key) addItem(groupIndex, key)
  draggingField.value = null
}

/**
 * 找到字段对应的类型定义。
 * @param key 顶层字段 key。
 * @returns 字段，或空值。
 */
function findField(key: string): FlowFieldDefinition | undefined {
  return props.data.fields.find(field => field.key === key)
}

/**
 * 样例预览只展示字段形态，不使用或伪造真实业务数据。
 * @param field 字段定义。
 * @returns 类型提示。
 */
function previewText(field: FlowFieldDefinition): string {
  if (field.formula) return '按表达式计算'
  if (field.approverInput) return '由审批人在获授权节点填写'
  if (field.type === 'money') return '¥ 0.00'
  if (field.type === 'number') return '0'
  if (field.type === 'date') return '日期'
  if (field.type === 'attachment') return '附件列表'
  if (field.type === 'person') return '人员'
  if (field.type === 'department') return '部门'
  return '业务系统传入'
}
</script>

<template>
  <div class="layout-editor">
    <div class="layout-top"><div><h2>审批详情排版</h2><p>预览只显示只读结构。审批意见、流程进度和可执行操作由详情页另行提供。</p></div><label>展示方式 <select :value="detailLayout.mode" @change="setMode(($event.target as HTMLSelectElement).value)"><option value="auto">自动排版</option><option value="custom">自定义排版</option><option value="embed">嵌入业务原单</option></select></label></div>
    <div v-if="detailLayout.mode === 'embed'" class="embed-panel"><h3>业务系统只读原单</h3><p>填写接入应用登记的页面编码。实际地址与短时身份由业务系统提供；这里不保存任意外部 URL。</p><label>页面编码<input :value="detailLayout.embedPageCode || ''" placeholder="例如 purchase-detail" @input="setEmbedCode(($event.target as HTMLInputElement).value)" /></label><div class="embed-preview">详情页会在通过身份校验后显示业务系统的只读原单。</div></div>
    <div v-else class="layout-columns">
      <div v-if="detailLayout.mode === 'custom'" class="layout-controls">
        <section class="panel"><div class="panel-heading"><h3>可用字段</h3><span class="badge">{{ availableFields.length }}</span></div><div class="available-list"><p>拖到下方分组，或在分组中选择添加。</p><div v-for="field in availableFields" :key="field.key" class="available-field" draggable="true" @dragstart="startDrag(field.key, $event)" @dragend="draggingField = null"><span>⋮⋮ {{ field.name }}</span><small>{{ field.key }}</small></div><div v-if="!availableFields.length" class="muted">所有字段已加入布局</div></div></section>
        <div class="group-header"><h3>排版分组</h3><button class="btn btn-primary" type="button" @click="addGroup">＋ 添加分组</button></div>
        <section v-for="(group, groupIndex) in detailLayout.groups" :key="groupIndex" class="group-card" @dragover.prevent @drop="dropField(groupIndex, $event)"><div class="group-bar"><strong>分组 {{ groupIndex + 1 }}</strong><span></span><button class="btn" type="button" :disabled="groupIndex === 0" @click="moveGroup(groupIndex, -1)">↑</button><button class="btn" type="button" :disabled="groupIndex === detailLayout.groups.length - 1" @click="moveGroup(groupIndex, 1)">↓</button><button class="btn" type="button" @click="removeGroup(groupIndex)">删除</button></div><div class="group-settings"><label>标题<input :value="group.title" @input="updateGroup(groupIndex, 'title', ($event.target as HTMLInputElement).value)" /></label><label>每行列数<select :value="group.columns" @change="updateGroup(groupIndex, 'columns', ($event.target as HTMLSelectElement).value)"><option :value="1">1 列</option><option :value="2">2 列</option><option :value="3">3 列</option></select></label></div><div class="group-items"><div v-for="(item, itemIndex) in group.items" :key="itemIndex" class="group-item"><div class="item-head"><strong>{{ findField(item.fieldKey)?.name || item.fieldKey }}</strong><small>{{ item.fieldKey }}</small><span></span><button class="btn" type="button" :disabled="itemIndex === 0" @click="moveItem(groupIndex, itemIndex, -1)">↑</button><button class="btn" type="button" :disabled="itemIndex === group.items.length - 1" @click="moveItem(groupIndex, itemIndex, 1)">↓</button><button class="btn" type="button" @click="removeItem(groupIndex, itemIndex)">移除</button></div><div class="item-settings"><label>跨列 <select :value="item.columnSpan" @change="updateItem(groupIndex, itemIndex, 'columnSpan', Number(($event.target as HTMLSelectElement).value))"><option v-for="count in group.columns" :key="count" :value="count">{{ count }} 列</option></select></label><label v-if="findField(item.fieldKey)?.type === 'money'"><input type="checkbox" :checked="item.showMoneyUppercase" @change="updateItem(groupIndex, itemIndex, 'showMoneyUppercase', ($event.target as HTMLInputElement).checked)" /> 显示人民币大写</label></div><div v-if="findField(item.fieldKey)?.type === 'detailTable'" class="width-editor"><label v-for="child in findField(item.fieldKey)?.children" :key="child.key">{{ child.name }} 列宽 %<input type="number" min="1" max="100" :value="item.detailColumnWidths[child.key] || 100 / (findField(item.fieldKey)?.children.length || 1)" @change="updateItem(groupIndex, itemIndex, 'detailColumnWidths', Number(($event.target as HTMLInputElement).value), child.key)" /></label></div></div><div v-if="!group.items.length" class="drop-hint">将字段拖到此处</div></div><label class="add-select">添加字段 <select :value="''" @change="addItem(groupIndex, ($event.target as HTMLSelectElement).value); ($event.target as HTMLSelectElement).value = ''"><option value="">选择字段…</option><option v-for="field in availableFields" :key="field.key" :value="field.key">{{ field.name }}</option></select></label></section>
      </div>
      <section class="preview-panel"><div class="preview-heading"><strong>只读详情预览</strong><span class="badge">{{ detailLayout.mode === 'auto' ? '自动排版' : '自定义排版' }}</span></div><div v-if="!data.fields.length" class="preview-empty">先定义审批字段，再预览详情结构。</div><div v-for="(group, groupIndex) in groups" :key="groupIndex" class="preview-group"><h3>{{ group.title || '未命名分组' }}</h3><div class="preview-grid" :style="{ gridTemplateColumns: `repeat(${group.columns}, minmax(0, 1fr))` }"><div v-for="item in group.items" :key="item.field.key" class="preview-cell" :style="{ gridColumn: `span ${Math.min(item.layout.columnSpan, group.columns)}` }"><span class="preview-label">{{ item.field.name }}</span><template v-if="item.field.type === 'detailTable'"><table><thead><tr><th v-for="child in item.field.children" :key="child.key" :style="{ width: item.layout.detailColumnWidths[child.key] ? `${item.layout.detailColumnWidths[child.key]}%` : undefined }">{{ child.name }}</th></tr></thead><tbody><tr><td v-for="child in item.field.children" :key="child.key">{{ previewText(child) }}</td></tr></tbody></table></template><div v-else>{{ previewText(item.field) }} <span v-if="item.layout.showMoneyUppercase && item.field.type === 'money'" class="uppercase">（人民币大写）</span></div></div></div></div><p class="preview-foot">此处不包含真实单据数据，发布后按流程版本固定详情排版。</p></section>
    </div>
  </div>
</template>

<style scoped>
.layout-editor{padding:20px 0 28px}.layout-top,.group-header,.group-bar,.item-head,.preview-heading{display:flex;align-items:center;justify-content:space-between;gap:10px}.layout-top{margin-bottom:20px}.layout-top h2{font-size:16px}.layout-top p,.available-list p,.embed-panel p{color:var(--muted);margin-top:5px}.layout-top label,.group-settings label,.item-settings label,.width-editor label,.add-select,.embed-panel label{display:grid;gap:5px;color:var(--muted);font-size:12px}.layout-editor input,.layout-editor select{min-height:34px;padding:6px 8px;border:1px solid #cbd5e1;border-radius:4px;background:#fff;color:var(--ink)}.layout-columns{display:grid;grid-template-columns:minmax(340px,43%) minmax(0,1fr);align-items:start;gap:18px}.layout-columns:not(:has(.layout-controls)){grid-template-columns:1fr}.layout-controls{display:grid;gap:16px}.available-list{padding:12px 15px}.available-field{display:flex;justify-content:space-between;gap:8px;padding:8px 10px;margin-top:8px;border:1px solid var(--line);background:#f9fbff;cursor:grab}.available-field small{color:var(--muted)}.group-header h3{font-size:14px}.group-card{padding:13px;border:1px solid var(--line);border-radius:5px;background:#fff}.group-bar{margin-bottom:12px}.group-bar span,.item-head span{flex:1}.group-bar .btn,.item-head .btn{min-height:28px;padding:3px 8px}.group-settings{display:grid;grid-template-columns:1fr 125px;gap:9px;margin-bottom:12px}.group-items{display:grid;gap:8px}.group-item{padding:9px;border:1px solid var(--line);background:#fbfcff}.item-head{flex-wrap:wrap}.item-head small{font:11px Consolas,monospace;color:var(--muted)}.item-settings{display:flex;gap:15px;align-items:center;margin-top:9px}.item-settings label:has(input[type=checkbox]){display:flex;align-items:center}.width-editor{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px;margin-top:10px}.drop-hint{padding:20px;text-align:center;border:1px dashed #b9c9e5;color:var(--muted)}.add-select{margin-top:11px}.preview-panel{min-height:350px;border:1px solid var(--line);border-radius:5px;background:#fff}.preview-heading{padding:14px 18px;border-bottom:1px solid var(--line);background:#f8faff}.preview-group{padding:18px 20px}.preview-group h3{font-size:15px;margin-bottom:13px}.preview-grid{display:grid;gap:10px}.preview-cell{min-width:0;padding:10px;border:1px solid var(--line);border-radius:4px;overflow:auto;color:var(--ink)}.preview-label{display:block;margin-bottom:5px;color:var(--muted);font-size:11px}.preview-cell table{border-collapse:collapse;width:100%;min-width:250px}.preview-cell th,.preview-cell td{border:1px solid var(--line);padding:7px;text-align:left;font-weight:normal}.preview-cell th{background:#f5f8fd}.uppercase{color:var(--muted)}.preview-empty,.preview-foot{padding:18px;color:var(--muted)}.preview-foot{font-size:12px}.embed-panel{border:1px solid var(--line);padding:20px;display:grid;gap:14px}.embed-panel h3{font-size:15px}.embed-panel label{max-width:350px}.embed-preview{padding:35px;text-align:center;border:1px dashed #b9c9e5;background:#fafcff;color:var(--muted)}@media(max-width:900px){.layout-columns{grid-template-columns:1fr}}@media(max-width:560px){.layout-top{align-items:start;flex-direction:column}.group-settings,.width-editor{grid-template-columns:1fr}.preview-grid{grid-template-columns:1fr!important}.preview-cell{grid-column:span 1!important}}
</style>

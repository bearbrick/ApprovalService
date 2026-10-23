<script setup lang="ts">
/**
 * 使用 bpmn-js 绘制和编辑标准 BPMN 图，审批配置仍由完整流程定义持有。
 */
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import BpmnModeler from 'bpmn-js/lib/Modeler'
import type { FlowDefinition, FlowNodeType } from '../../model/flow.ts'
import { canvasPalette, copyCanvasFlow, nextCanvasId } from './canvas-model.ts'
import { canvasFromBpmn, bpmnTypeFor, flowTypeFor } from './bpmn-modeler-adapter.ts'
import type { BpmnDiagramElement } from './bpmn-modeler-adapter.ts'
import { emptyDiagram, nodeColors } from './bpmn-modeler-support.ts'
import type { ModelerServices } from './bpmn-modeler-support.ts'
import { validateCanvasDefinition } from './canvas-validation.ts'
import type { CanvasIssue } from './canvas-validation.ts'
import { downloadCanvasBpmn, readCanvasBpmn } from './canvas-file-actions.ts'
import 'bpmn-js/dist/assets/diagram-js.css'
import 'bpmn-js/dist/assets/bpmn-js.css'
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn.css'
import './canvas.css'
import './bpmn-modeler.css'

/**
 * 组件输入属性。
 */
const props = withDefaults(defineProps<{
  /**
   * 完整审批流程定义。
   */
  modelValue: FlowDefinition
  /**
   * 发布校验问题。
   */
  issues?: CanvasIssue[]
  /**
   * 是否只允许查看。
   */
  readonly?: boolean
  /**
   * 撤销历史的持有者。
   */
  historyMode?: 'internal' | 'external'
}>(), { readonly: false, historyMode: 'external' })

/**
 * 与工作台保持现有编辑接口。
 */
const emit = defineEmits<{
  /**
   * 完整流程定义更新。
   */
  'update:modelValue': [value: FlowDefinition]
  /**
   * 图元选择变化。
   */
  select: [selection: { kind: 'node' | 'edge'; id: string } | null]
  /**
   * 本地校验完成。
   */
  validate: [issues: CanvasIssue[]]
  /**
   * 完成一次可撤销编辑。
   */
  editCommitted: [previousDefinition: FlowDefinition]
  /**
   * 请求工作台撤销。
   */
  undo: []
  /**
   * 请求工作台重做。
   */
  redo: []
}>()

/**
 * 标准建模器挂载位置。
 */
const stage = ref<HTMLDivElement | null>(null)
/**
 * BPMN 文件选择控件。
 */
const importInput = ref<HTMLInputElement | null>(null)
/**
 * 当前选中元素。
 */
const selected = ref<{ kind: 'node' | 'edge'; id: string } | null>(null)
/**
 * 等待点击目标的连线起点。
 */
const connectFrom = ref<string | null>(null)
/**
 * 导入或操作的可见消息。
 */
const message = ref('')
/**
 * 当前缩放百分比。
 */
const zoomPercent = ref(100)
/**
 * 本地发布检查结果。
 */
const checkedIssues = ref<CanvasIssue[] | null>(null)
/**
 * 工具箱新建节点的审批细分类型。
 */
const requestedTypes = new Map<string, FlowNodeType>()
/**
 * 建模器实例不交给 Vue 代理。
 */
let modeler: BpmnModeler | null = null
/**
 * 重建图形时禁止把中间命令写回工作台。
 */
let loading = false
/**
 * 最近已同步的图形序列化值。
 */
let lastCanvasJson = ''
/**
 * 顺序执行异步图形重建，避免导入交错。
 */
let rebuildQueue: Promise<void> = Promise.resolve()

/**
 * 当前流程节点。
 */
const nodes = computed(() => props.modelValue.canvas.nodes)
/**
 * 当前流程连线。
 */
const edges = computed(() => props.modelValue.canvas.edges)
/**
 * 选中节点的完整审批配置。
 */
const selectedNode = computed(() => selected.value?.kind === 'node' ? nodes.value.find(node => node.id === selected.value?.id) ?? null : null)
/**
 * 选中连线的完整审批配置。
 */
const selectedEdge = computed(() => selected.value?.kind === 'edge' ? edges.value.find(edge => edge.id === selected.value?.id) ?? null : null)
/**
 * 当前需要展示的问题。
 */
const visibleIssues = computed(() => props.issues ?? checkedIssues.value ?? [])

/**
 * 从建模器读取一项服务。
 * @param name 服务名。
 * @returns 指定的建模器服务。
 */
function service<K extends keyof ModelerServices>(name: K): ModelerServices[K] {
  if (!modeler) throw new Error('BPMN 建模器尚未就绪。')
  return modeler.get(name) as ModelerServices[K]
}

/**
 * 把现有审批流程重建成标准 BPMN 图，连线交由建模器自动布线。
 * @param flow 待显示的流程。
 */
async function rebuild(flow: FlowDefinition): Promise<void> {
  if (!modeler) return
  loading = true
  const previousSelection = selected.value
  const canvas = service('canvas')
  const oldViewbox = lastCanvasJson ? canvas.viewbox() : null
  try {
    await modeler.importXML(emptyDiagram())
    const root = canvas.getRootElement()
    const modeling = service('modeling')
    const factory = service('elementFactory')
    const bpmnFactory = service('bpmnFactory')
    const shapes = new Map<string, BpmnDiagramElement>()
    for (const node of flow.canvas.nodes) {
      const type = bpmnTypeFor(node.type)
      const businessObject = bpmnFactory.create(type, { id: node.id, name: node.name })
      const shape = factory.createShape({ type, businessObject })
      const element = modeling.createShape(shape, { x: node.x, y: node.y }, root)
      modeling.setColor([element], nodeColors(node.type))
      shapes.set(node.id, element)
    }
    for (const edge of flow.canvas.edges) {
      const source = shapes.get(edge.sourceId)
      const target = shapes.get(edge.targetId)
      if (!source || !target) continue
      const name = edge.isDefault ? '其他情况' : edge.condition?.trim() ? edge.condition.trim() : ''
      const businessObject = bpmnFactory.create('bpmn:SequenceFlow', { id: edge.id, name })
      const connection = modeling.connect(source, target, { type: 'bpmn:SequenceFlow', businessObject })
      if (connection) modeling.setColor([connection], { stroke: '#8294ae' })
    }
    // BPMN 默认把网关名称放在右侧，容易压到水平条件线；用建模器移动标签位置。
    for (const node of flow.canvas.nodes.filter(item => item.type.endsWith('Gateway'))) {
      const label = service('elementRegistry').get(`${node.id}_label`)
      if (!label) continue
      const desiredX = node.gatewayDirection === 'join' ? node.x + 40 : node.x - (label.width ?? 90) - 45
      const desiredY = node.y - 60
      modeling.moveShape(label, { x: desiredX - (label.x ?? 0), y: desiredY - (label.y ?? 0) })
    }
    // 属性面板改值会触发重建；保留同一图元选择，避免编辑途中面板消失。
    const restored = previousSelection ? service('elementRegistry').get(previousSelection.id) : undefined
    service('selection').select(restored ?? null)
    if (previousSelection && !restored) {
      selected.value = null
      emit('select', null)
    }
    lastCanvasJson = JSON.stringify(flow.canvas)
    if (oldViewbox) canvas.viewbox(oldViewbox)
    else canvas.zoom('fit-viewport')
    zoomPercent.value = Math.round(canvas.zoom() * 100)
    syncIssueMarkers()
  } catch (error) {
    message.value = error instanceof Error ? `BPMN 图加载失败：${error.message}` : 'BPMN 图加载失败。'
  } finally {
    loading = false
  }
}

/**
 * 串行重建最新流程，避免属性面板与画布操作互相覆盖。
 */
function queueRebuild(): void {
  const flow = copyCanvasFlow(props.modelValue)
  rebuildQueue = rebuildQueue.then(() => rebuild(flow))
}

/**
 * 将建模器的一次用户操作同步到现有流程模型与工作台历史。
 */
function captureChange(): void {
  if (loading || !modeler) return
  const previous = copyCanvasFlow(props.modelValue)
  const canvas = canvasFromBpmn(service('elementRegistry').getAll(), previous.canvas, requestedTypes)
  for (const id of requestedTypes.keys()) if (!canvas.nodes.some(node => node.id === id)) requestedTypes.delete(id)
  const next = { ...previous, canvas }
  const serialized = JSON.stringify(canvas)
  if (serialized === lastCanvasJson) return
  lastCanvasJson = serialized
  emit('update:modelValue', next)
  emit('editCommitted', previous)
}

/**
 * 将标准建模器的选择映射到审批属性面板。
 * @param event 建模器选择事件。
 */
function captureSelection(event: { newSelection?: BpmnDiagramElement[] }): void {
  if (loading) return
  const element = event.newSelection?.[0]?.labelTarget ?? event.newSelection?.[0]
  if (!element || element.type !== 'bpmn:SequenceFlow' && !flowTypeFor(element.type)) {
    selected.value = null
    emit('select', null)
    return
  }
  if (connectFrom.value && element.type !== 'bpmn:SequenceFlow' && element.id !== connectFrom.value) {
    const source = service('elementRegistry').get(connectFrom.value)
    if (source && !service('modeling').connect(source, element)) message.value = '这些节点不能用 BPMN 顺序流直接相连。'
    connectFrom.value = null
  }
  selected.value = { kind: element.type === 'bpmn:SequenceFlow' ? 'edge' : 'node', id: element.id }
  emit('select', selected.value)
}

/**
 * 更新问题图元的高亮标记。
 */
function syncIssueMarkers(): void {
  if (!modeler) return
  const registry = service('elementRegistry')
  const canvas = service('canvas')
  for (const element of registry.getAll()) canvas.removeMarker(element.id, 'approval-invalid')
  for (const issue of visibleIssues.value) if (issue.elementId && registry.get(issue.elementId)) canvas.addMarker(issue.elementId, 'approval-invalid')
}

/**
 * 在画布中央或给定位置添加一个审批节点。
 * @param type 审批节点种类。
 * @param x 可选横坐标。
 * @param y 可选纵坐标。
 */
function addNode(type: FlowNodeType, x?: number, y?: number): void {
  if (props.readonly || !modeler) return
  const canvas = service('canvas')
  const viewport = canvas.viewbox()
  const id = nextCanvasId('node', nodes.value.map(node => node.id))
  const palette = canvasPalette.find(item => item.type === type)
  const bpmnType = bpmnTypeFor(type)
  const businessObject = service('bpmnFactory').create(bpmnType, { id, name: palette?.name ?? '节点' })
  const shape = service('elementFactory').createShape({ type: bpmnType, businessObject })
  requestedTypes.set(id, type)
  const element = service('modeling').createShape(shape, { x: x ?? viewport.x + viewport.width / 2, y: y ?? viewport.y + viewport.height / 2 }, canvas.getRootElement())
  service('selection').select(element)
}

/**
 * 拖动工具箱节点时记录其审批类型。
 * @param event 浏览器拖拽事件。
 * @param type 审批节点种类。
 */
function startPaletteDrag(event: DragEvent, type: FlowNodeType): void {
  event.dataTransfer?.setData('application/x-approval-node', type)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy'
}

/**
 * 按 BPMN 视口坐标放置拖入节点。
 * @param event 浏览器拖拽事件。
 */
function dropNode(event: DragEvent): void {
  event.preventDefault()
  const type = event.dataTransfer?.getData('application/x-approval-node') as FlowNodeType
  if (!canvasPalette.some(item => item.type === type) || !stage.value || !modeler) return
  const bounds = stage.value.getBoundingClientRect()
  const viewport = service('canvas').viewbox()
  addNode(type, viewport.x + (event.clientX - bounds.left) * viewport.width / bounds.width, viewport.y + (event.clientY - bounds.top) * viewport.height / bounds.height)
}

/**
 * 删除当前选中的 BPMN 图元。
 */
function removeSelected(): void {
  if (props.readonly || !selected.value) return
  const element = service('elementRegistry').get(selected.value.id)
  if (element) service('modeling').removeElements([element])
  selected.value = null
  emit('select', null)
}

/**
 * 适配所有 BPMN 图元到当前视口。
 */
function fit(): void {
  if (!modeler) return
  const canvas = service('canvas')
  canvas.zoom('fit-viewport')
  zoomPercent.value = Math.round(canvas.zoom() * 100)
}

/**
 * 按倍率缩放画布。
 * @param factor 本次缩放系数。
 */
function changeZoom(factor: number): void {
  if (!modeler) return
  const canvas = service('canvas')
  canvas.zoom(Math.min(2.5, Math.max(.25, canvas.zoom() * factor)))
  zoomPercent.value = Math.round(canvas.zoom() * 100)
}

/**
 * 校验现有审批定义。
 * @returns 发布问题列表。
 */
function check(): CanvasIssue[] {
  const result = validateCanvasDefinition(props.modelValue)
  checkedIssues.value = result
  emit('validate', result)
  message.value = result.length ? `发现 ${result.length} 处需要处理的问题。` : '画布静态检查通过；发布时服务端会再次检查。'
  syncIssueMarkers()
  return result
}

/**
 * 下载与后端兼容的审批 BPMN 文件。
 */
async function exportFile(): Promise<void> {
  try { await downloadCanvasBpmn(props.modelValue); message.value = 'BPMN 文件已导出。' }
  catch (error) { message.value = error instanceof Error ? error.message : '导出失败。' }
}

/**
 * 导入审批服务支持的 BPMN 文件。
 * @param event 文件控件事件。
 */
async function importFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  try {
    const imported = await readCanvasBpmn(file)
    emit('update:modelValue', imported)
    emit('editCommitted', copyCanvasFlow(props.modelValue))
    selected.value = null
    emit('select', null)
    message.value = 'BPMN 文件已导入，请检查流程配置后保存或发布。'
  } catch (error) { message.value = error instanceof Error ? error.message : '导入失败。' }
  input.value = ''
}

/**
 * 定位节点或连线并同步右侧属性面板。
 * @param id 流程元素标识。
 */
function focusElement(id: string): void {
  if (!modeler) return
  const element = service('elementRegistry').get(id)
  if (!element) return
  service('canvas').scrollToElement(element)
  service('selection').select(element)
}

/**
 * 在编辑器聚焦时接管删除和撤销快捷键。
 * @param event 键盘事件。
 */
function onKeyDown(event: KeyboardEvent): void {
  if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) return
  if (event.key === 'Delete' || event.key === 'Backspace') { event.preventDefault(); removeSelected() }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') { event.preventDefault(); if (event.shiftKey) emit('redo'); else emit('undo') }
}

defineExpose({ check, exportFile, openImport: () => importInput.value?.click(), focusElement, fit })

watch(() => JSON.stringify(props.modelValue.canvas), signature => { if (signature !== lastCanvasJson) queueRebuild() })
watch(visibleIssues, syncIssueMarkers, { deep: true })
onMounted(() => {
  if (!stage.value) return
  modeler = new BpmnModeler({ container: stage.value })
  modeler.on('commandStack.changed', captureChange)
  modeler.on('selection.changed', captureSelection)
  queueRebuild()
})
onBeforeUnmount(() => { modeler?.destroy(); modeler = null })
</script>

<template>
  <section class="flow-canvas-editor bpmn-editor" aria-label="BPMN 自由画布设计器" @keydown="onKeyDown">
    <div class="flow-canvas-toolbar">
      <span class="flow-canvas-title"><span class="flow-canvas-title-icon">◇</span> BPMN 流程图</span>
      <span class="flow-canvas-separator" aria-hidden="true"></span>
      <button type="button" class="btn" :disabled="readonly || !selectedNode" @click="connectFrom = selectedNode?.id ?? null">添加连线</button>
      <button type="button" class="btn" :disabled="readonly || !selected" @click="removeSelected">删除</button>
      <button type="button" class="btn" :disabled="readonly" @click="emit('undo')">撤销</button>
      <button type="button" class="btn" :disabled="readonly" @click="emit('redo')">重做</button>
      <span class="flow-canvas-spacer"></span>
      <button type="button" class="btn" @click="fit">适应画布</button>
      <button type="button" class="btn" @click="exportFile">导出 BPMN</button>
      <button type="button" class="btn" :disabled="readonly" @click="importInput?.click()">导入</button>
      <button type="button" class="btn btn-primary" @click="check">检查流程</button>
      <input ref="importInput" class="flow-canvas-hidden" type="file" accept=".bpmn,.xml,application/xml" aria-label="选择 BPMN 文件" @change="importFile">
    </div>
    <div class="flow-canvas-layout">
      <aside class="flow-canvas-palette" aria-label="节点工具箱">
        <div class="flow-canvas-pane-heading">节点工具箱 <small>拖入画布</small></div>
        <div v-for="group in ['事件', '人工节点', '网关', '系统节点']" :key="group" class="flow-canvas-palette-group">
          <h3>{{ group }}</h3>
          <button v-for="item in canvasPalette.filter(entry => entry.group === group)" :key="item.type" type="button" class="flow-canvas-palette-item" :disabled="readonly" draggable="true" @dragstart="startPaletteDrag($event, item.type)" @click="addNode(item.type)"><span class="flow-canvas-palette-symbol" :class="item.type">{{ item.type === 'exclusiveGateway' ? '×' : item.type === 'parallelGateway' ? '+' : item.type === 'inclusiveGateway' ? '○' : item.type === 'start' ? '●' : item.type === 'end' ? '◎' : '▣' }}</span>{{ item.name }}</button>
        </div>
      </aside>
      <div class="flow-canvas-viewport" @dragover.prevent @drop.stop.prevent="dropNode">
        <div class="flow-canvas-help">{{ connectFrom ? '点击目标节点完成连线；再次点击“添加连线”取消。' : '拖入节点 · 拖动节点和连线 · 点击连线编辑条件 · 滚轮缩放' }}</div>
        <div ref="stage" class="bpmn-stage" :class="{ 'is-readonly': readonly }" aria-label="审批流程 BPMN 画布"></div>
        <div class="flow-canvas-controls"><button type="button" class="btn" aria-label="缩小" @click="changeZoom(.8)">−</button><span>{{ zoomPercent }}%</span><button type="button" class="btn" aria-label="放大" @click="changeZoom(1.25)">＋</button></div>
      </div>
      <aside class="flow-canvas-inspector" aria-label="画布属性">
        <slot name="inspector" :node="selectedNode" :edge="selectedEdge" :issues="visibleIssues">
          <div class="flow-canvas-pane-heading">{{ selectedNode ? '节点设置' : selectedEdge ? '连线设置' : '画布说明' }}</div>
          <div class="flow-canvas-inspector-body"><p>{{ selectedNode ? `${selectedNode.name} · ${selectedNode.type}` : selectedEdge ? `${selectedEdge.sourceId} → ${selectedEdge.targetId}` : '从左侧拖入 BPMN 节点，选中后在右侧配置审批规则。' }}</p></div>
        </slot>
      </aside>
    </div>
    <div class="flow-canvas-footer"><span>{{ nodes.length }} 个节点 · {{ edges.length }} 条连线</span><span v-if="message" role="status">{{ message }}</span><span v-else>标准 BPMN 建模器 · 审批服务规则扩展</span></div>
  </section>
</template>

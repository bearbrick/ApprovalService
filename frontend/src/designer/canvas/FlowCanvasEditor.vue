<script setup lang="ts">
/**
 * 正式流程定义的 SVG 自由画布；属性工作台可通过 select 事件和 inspector 插槽接入。
 */
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import type { FlowDefinition, FlowNode, FlowNodeType } from '../../model/flow.ts'
import { addCanvasNode, canvasBounds, canvasPalette, connectCanvasNodes, createCanvasNode, moveCanvasNode, nextCanvasId, removeCanvasElement, updateCanvasEdge } from './canvas-model.ts'
import { edgeGeometry, screenToCanvas } from './canvas-geometry.ts'
import { validateCanvasDefinition } from './canvas-validation.ts'
import type { CanvasIssue } from './canvas-validation.ts'
import { exportBpmn, importBpmn } from './bpmn-codec.ts'
import './canvas.css'

/**
 * 画布输入属性，工作台持有完整流程定义并负责持久化。
 */
const props = withDefaults(defineProps<{
  /**
   * 完整流程定义。
   */
  modelValue: FlowDefinition
  /**
   * 服务端发布校验问题；未传时使用本地检查。
   */
  issues?: CanvasIssue[]
  /**
   * 是否允许编辑。
   */
  readonly?: boolean
}>(), { readonly: false })

/**
 * 向工作台报告新定义、当前选择和校验问题。
 */
const emit = defineEmits<{
  /**
   * 完整流程定义已更新。
   */
  'update:modelValue': [value: FlowDefinition]
  /**
   * 当前选中的节点或连线变化。
   */
  select: [selection: { kind: 'node' | 'edge'; id: string } | null]
  /**
   * 用户执行本地发布检查。
   */
  validate: [issues: CanvasIssue[]]
}>()

/**
 * SVG 画布根元素。
 */
const stage = ref<SVGSVGElement | null>(null)
/**
 * 隐藏的 BPMN 文件选择控件。
 */
const importInput = ref<HTMLInputElement | null>(null)
/**
 * 当前选中的元素。
 */
const selected = ref<{ kind: 'node' | 'edge'; id: string } | null>(null)
/**
 * 等待选择目标节点的连线起点。
 */
const connectFrom = ref<string | null>(null)
/**
 * 画布缩放比例。
 */
const zoom = ref(1)
/**
 * 画布水平平移。
 */
const offsetX = ref(120)
/**
 * 画布垂直平移。
 */
const offsetY = ref(100)
/**
 * 当前指针拖动。
 */
const drag = ref<{ kind: 'node' | 'pan'; id?: string; startX: number; startY: number; nodeX?: number; nodeY?: number; panX: number; panY: number } | null>(null)
/**
 * 撤销栈，只保存由本组件发出的画布编辑。
 */
const undo = ref<FlowDefinition[]>([])
/**
 * 重做栈。
 */
const redo = ref<FlowDefinition[]>([])
/**
 * 本地检查结果。
 */
const checkedIssues = ref<CanvasIssue[] | null>(null)
/**
 * 导入、导出和操作的可见错误。
 */
const message = ref('')

/**
 * 当前画布节点。
 */
const nodes = computed(() => props.modelValue.canvas.nodes)
/**
 * 当前画布连线。
 */
const edges = computed(() => props.modelValue.canvas.edges)
/**
 * 当前节点。
 */
const selectedNode = computed(() => selected.value?.kind === 'node' ? nodes.value.find(node => node.id === selected.value?.id) ?? null : null)
/**
 * 当前连线。
 */
const selectedEdge = computed(() => selected.value?.kind === 'edge' ? edges.value.find(edge => edge.id === selected.value?.id) ?? null : null)
/**
 * 画布当前显示的问题。
 */
const visibleIssues = computed(() => props.issues ?? checkedIssues.value ?? [])
/**
 * 带路径的可见连线。
 */
const renderedEdges = computed(() => edges.value.flatMap(edge => {
  const source = nodes.value.find(node => node.id === edge.sourceId)
  const target = nodes.value.find(node => node.id === edge.targetId)
  return source && target ? [{ edge, geometry: edgeGeometry(edge, source, target) }] : []
}))
/**
 * 画布平移与缩放变换。
 */
const sceneTransform = computed(() => `translate(${offsetX.value} ${offsetY.value}) scale(${zoom.value})`)
/**
 * 节点类型标题。
 * @param type 节点类型。
 * @returns 中文标题。
 */
const typeLabel = (type: FlowNodeType): string => canvasPalette.find(item => item.type === type)?.name ?? '节点'
/**
 * 节点是否存在校验问题。
 * @param id 元素 ID。
 * @returns 有问题时为真。
 */
const hasIssue = (id: string): boolean => visibleIssues.value.some(issue => issue.elementId === id)

/**
 * 向外发布新定义并登记撤销历史。
 * @param value 新定义。
 * @param remember 是否写入撤销历史。
 */
function publish(value: FlowDefinition, remember = true): void {
  if (props.readonly) return
  if (remember) { undo.value.push(structuredClone(props.modelValue)); if (undo.value.length > 80) undo.value.shift(); redo.value = [] }
  checkedIssues.value = null
  emit('update:modelValue', value)
}

/**
 * 选择元素并通知工作台属性面板。
 * @param kind 元素种类。
 * @param id 元素 ID。
 */
function choose(kind: 'node' | 'edge', id: string): void {
  selected.value = { kind, id }
  emit('select', selected.value)
}

/**
 * 将新节点放到指定的画布坐标。
 * @param type 节点种类。
 * @param x 横坐标。
 * @param y 纵坐标。
 */
function addNode(type: FlowNodeType, x?: number, y?: number): void {
  if (props.readonly) return
  const id = nextCanvasId('node', nodes.value.map(node => node.id))
  const node = createCanvasNode(type, id, x ?? (stage.value?.clientWidth ?? 800) / 2 / zoom.value - offsetX.value / zoom.value, y ?? (stage.value?.clientHeight ?? 540) / 2 / zoom.value - offsetY.value / zoom.value)
  publish({ ...props.modelValue, canvas: addCanvasNode(props.modelValue.canvas, node) })
  choose('node', id)
}

/**
 * 工具箱拖拽开始时把节点类型放进数据传输区。
 * @param event 原生拖拽事件。
 * @param type 节点种类。
 */
function onPaletteDrag(event: DragEvent, type: FlowNodeType): void {
  event.dataTransfer?.setData('application/x-approval-node', type)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy'
}

/**
 * 将拖拽的工具箱节点放到鼠标位置。
 * @param event 原生拖拽事件。
 */
function onDrop(event: DragEvent): void {
  event.preventDefault()
  const type = event.dataTransfer?.getData('application/x-approval-node') as FlowNodeType
  if (!canvasPalette.some(item => item.type === type) || !stage.value) return
  const bounds = stage.value.getBoundingClientRect()
  const point = screenToCanvas(event.clientX, event.clientY, bounds.left, bounds.top, offsetX.value, offsetY.value, zoom.value)
  addNode(type, point.x, point.y)
}

/**
 * 在节点上按下指针时开始拖动，或在连线模式下完成连接。
 * @param event 指针事件。
 * @param node 被点中的节点。
 */
function onNodeDown(event: PointerEvent, node: FlowNode): void {
  event.stopPropagation()
  if (connectFrom.value) {
    if (connectFrom.value !== node.id) {
      try {
        const result = connectCanvasNodes(props.modelValue.canvas, connectFrom.value, node.id)
        publish({ ...props.modelValue, canvas: result.canvas })
        choose('edge', result.edgeId)
        message.value = ''
      } catch (error) { message.value = error instanceof Error ? error.message : '连线失败。' }
    }
    connectFrom.value = null
    return
  }
  choose('node', node.id)
  if (props.readonly || event.button !== 0) return
  drag.value = { kind: 'node', id: node.id, startX: event.clientX, startY: event.clientY, nodeX: node.x, nodeY: node.y, panX: offsetX.value, panY: offsetY.value }
  stage.value?.setPointerCapture(event.pointerId)
}

/**
 * 空白区域按下指针时开始平移，并清除选择。
 * @param event 指针事件。
 */
function onStageDown(event: PointerEvent): void {
  if (event.button !== 0 && event.button !== 1) return
  selected.value = null
  emit('select', null)
  drag.value = { kind: 'pan', startX: event.clientX, startY: event.clientY, panX: offsetX.value, panY: offsetY.value }
  stage.value?.setPointerCapture(event.pointerId)
}

/**
 * 根据当前指针更新平移或节点位置。
 * @param event 指针事件。
 */
function onPointerMove(event: PointerEvent): void {
  const current = drag.value
  if (!current) return
  if (current.kind === 'pan') { offsetX.value = current.panX + event.clientX - current.startX; offsetY.value = current.panY + event.clientY - current.startY; return }
  if (!current.id || current.nodeX === undefined || current.nodeY === undefined) return
  const x = current.nodeX + (event.clientX - current.startX) / zoom.value
  const y = current.nodeY + (event.clientY - current.startY) / zoom.value
  // 每次指针移动不入历史；松开时再记录一次可撤销操作。
  emit('update:modelValue', { ...props.modelValue, canvas: moveCanvasNode(props.modelValue.canvas, current.id, x, y) })
}

/**
 * 结束指针操作，记录完成后的节点拖动历史。
 * @param event 指针事件。
 */
function onPointerUp(event: PointerEvent): void {
  const current = drag.value
  drag.value = null
  if (stage.value?.hasPointerCapture(event.pointerId)) stage.value.releasePointerCapture(event.pointerId)
  if (current?.kind === 'node' && current.id && current.nodeX !== undefined && current.nodeY !== undefined) {
    const node = nodes.value.find(item => item.id === current.id)
    if (node && (node.x !== current.nodeX || node.y !== current.nodeY)) {
      const previous = structuredClone(props.modelValue)
      previous.canvas = moveCanvasNode(previous.canvas, current.id, current.nodeX, current.nodeY)
      undo.value.push(previous); redo.value = []
    }
  }
}

/**
 * 鼠标滚轮以指针为中心缩放画布。
 * @param event 滚轮事件。
 */
function onWheel(event: WheelEvent): void {
  if (!stage.value) return
  event.preventDefault()
  const rect = stage.value.getBoundingClientRect()
  const before = screenToCanvas(event.clientX, event.clientY, rect.left, rect.top, offsetX.value, offsetY.value, zoom.value)
  zoom.value = Math.max(.35, Math.min(2, zoom.value * (event.deltaY > 0 ? .9 : 1.1)))
  offsetX.value = event.clientX - rect.left - before.x * zoom.value
  offsetY.value = event.clientY - rect.top - before.y * zoom.value
}

/**
 * 使用控制按钮改变缩放。
 * @param factor 缩放倍数。
 */
function changeZoom(factor: number): void { zoom.value = Math.max(.35, Math.min(2, zoom.value * factor)) }

/**
 * 让当前所有节点适配视口。
 */
function fit(): void {
  if (!stage.value) return
  const bounds = canvasBounds(props.modelValue.canvas)
  zoom.value = Math.max(.35, Math.min(1.35, Math.min(stage.value.clientWidth / bounds.width, stage.value.clientHeight / bounds.height)))
  offsetX.value = (stage.value.clientWidth - bounds.width * zoom.value) / 2 - bounds.x * zoom.value
  offsetY.value = (stage.value.clientHeight - bounds.height * zoom.value) / 2 - bounds.y * zoom.value
}

/**
 * 删除当前选中元素。
 */
function removeSelected(): void {
  if (!selected.value || props.readonly) return
  publish({ ...props.modelValue, canvas: removeCanvasElement(props.modelValue.canvas, selected.value.id) })
  selected.value = null
  emit('select', null)
}

/**
 * 还原上一步本组件内的编辑。
 */
function undoChange(): void {
  const previous = undo.value.pop()
  if (!previous || props.readonly) return
  redo.value.push(structuredClone(props.modelValue))
  emit('update:modelValue', previous)
}

/**
 * 重做刚刚撤销的编辑。
 */
function redoChange(): void {
  const next = redo.value.pop()
  if (!next || props.readonly) return
  undo.value.push(structuredClone(props.modelValue))
  emit('update:modelValue', next)
}

/**
 * 从键盘触发微调、删除或取消连线。
 * @param event 键盘事件。
 */
function onKeyDown(event: KeyboardEvent): void {
  if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) return
  if (event.key === 'Escape') { connectFrom.value = null; selected.value = null; emit('select', null); return }
  if (event.key === 'Delete' || event.key === 'Backspace') { removeSelected(); return }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') { event.preventDefault(); event.shiftKey ? redoChange() : undoChange(); return }
  if (!selectedNode.value || props.readonly) return
  const movement: Record<string, [number, number]> = { ArrowLeft: [-16, 0], ArrowRight: [16, 0], ArrowUp: [0, -16], ArrowDown: [0, 16] }
  const delta = movement[event.key]
  if (delta) { event.preventDefault(); publish({ ...props.modelValue, canvas: moveCanvasNode(props.modelValue.canvas, selectedNode.value.id, selectedNode.value.x + delta[0]!, selectedNode.value.y + delta[1]!) }) }
}

/**
 * 修改选中节点的名称或网关属性。
 * @param patch 待更新的节点字段。
 */
function patchNode(patch: Partial<FlowNode>): void {
  if (!selectedNode.value) return
  publish({ ...props.modelValue, canvas: { ...props.modelValue.canvas, nodes: nodes.value.map(node => node.id === selectedNode.value?.id ? { ...node, ...patch } : node) } })
}

/**
 * 修改选中连线的条件、兜底标记和优先级。
 * @param condition 条件文本。
 * @param isDefault 兜底标记。
 * @param priority 分支优先级。
 */
function patchEdge(condition: string, isDefault: boolean, priority: number): void {
  if (!selectedEdge.value) return
  publish({ ...props.modelValue, canvas: updateCanvasEdge(props.modelValue.canvas, selectedEdge.value.id, condition, isDefault, priority) })
}

/**
 * 运行本地发布校验并展示所有问题。
 * @returns 全部问题。
 */
function check(): CanvasIssue[] {
  const result = validateCanvasDefinition(props.modelValue)
  checkedIssues.value = result
  emit('validate', result)
  message.value = result.length ? `发现 ${result.length} 处需要处理的问题。` : '画布静态检查通过；发布时服务端会再次检查。'
  return result
}

/**
 * 下载当前流程的正式 BPMN 文件。
 */
async function exportFile(): Promise<void> {
  try {
    const xml = await exportBpmn(props.modelValue)
    const url = URL.createObjectURL(new Blob([xml], { type: 'application/xml;charset=utf-8' }))
    const link = document.createElement('a')
    link.href = url; link.download = `${props.modelValue.code || 'flow'}.bpmn`; link.click()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
    message.value = 'BPMN 文件已导出。'
  } catch (error) { message.value = error instanceof Error ? error.message : '导出失败。' }
}

/**
 * 读取选中的 BPMN 文件并替换当前工作台定义。
 * @param event 文件选择事件。
 */
async function onImport(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  try {
    const imported = await importBpmn(await file.text())
    publish(imported)
    selected.value = null; emit('select', null); await nextTick(); fit()
    message.value = 'BPMN 文件已导入，请检查流程配置后保存或发布。'
  } catch (error) { message.value = error instanceof Error ? error.message : '导入失败。' }
  input.value = ''
}

/**
 * 将指定元素移到视口中央，供校验列表或工作台定位使用。
 * @param id 节点或连线 ID。
 */
function focusElement(id: string): void {
  const edge = edges.value.find(item => item.id === id)
  const node = nodes.value.find(item => item.id === id || item.id === edge?.targetId)
  if (!node || !stage.value) return
  offsetX.value = stage.value.clientWidth / 2 - node.x * zoom.value
  offsetY.value = stage.value.clientHeight / 2 - node.y * zoom.value
  choose(edge ? 'edge' : 'node', id)
}

/**
 * 向工作台公开校验、导出、导入入口及定位能力。
 */
defineExpose({ check, exportFile, openImport: () => importInput.value?.click(), focusElement, fit })

watch(() => props.modelValue.canvas, () => {
  if (selected.value && !nodes.value.some(node => node.id === selected.value?.id) && !edges.value.some(edge => edge.id === selected.value?.id)) { selected.value = null; emit('select', null) }
})
onMounted(fit)
</script>

<template>
  <section class="flow-canvas-editor" aria-label="自由画布设计器" @keydown="onKeyDown">
    <div class="flow-canvas-toolbar">
      <span class="flow-canvas-title"><span class="flow-canvas-title-icon">◇</span> 自由画布</span>
      <span class="flow-canvas-separator" aria-hidden="true"></span>
      <button type="button" class="btn" :disabled="readonly || !selectedNode" @click="connectFrom = selectedNode?.id ?? null">添加连线</button>
      <button type="button" class="btn" :disabled="readonly || !selected" @click="removeSelected">删除</button>
      <button type="button" class="btn" :disabled="readonly || !undo.length" @click="undoChange">撤销</button>
      <button type="button" class="btn" :disabled="readonly || !redo.length" @click="redoChange">重做</button>
      <span class="flow-canvas-spacer"></span>
      <button type="button" class="btn" @click="fit">适应画布</button>
      <button type="button" class="btn" @click="exportFile">导出 BPMN</button>
      <button type="button" class="btn" :disabled="readonly" @click="importInput?.click()">导入</button>
      <button type="button" class="btn btn-primary" @click="check">检查流程</button>
      <input ref="importInput" class="flow-canvas-hidden" type="file" accept=".bpmn,.xml,application/xml" aria-label="选择 BPMN 文件" @change="onImport">
    </div>
    <div class="flow-canvas-layout">
      <aside class="flow-canvas-palette" aria-label="节点工具箱">
        <div class="flow-canvas-pane-heading">节点工具箱 <small>拖入画布</small></div>
        <div v-for="group in ['事件', '人工节点', '网关', '系统节点']" :key="group" class="flow-canvas-palette-group">
          <h3>{{ group }}</h3>
          <button v-for="item in canvasPalette.filter(entry => entry.group === group)" :key="item.type" type="button" class="flow-canvas-palette-item" :disabled="readonly" draggable="true" @dragstart="onPaletteDrag($event, item.type)" @click="addNode(item.type)"><span class="flow-canvas-palette-symbol" :class="item.type">{{ item.type === 'exclusiveGateway' ? '×' : item.type === 'parallelGateway' ? '+' : item.type === 'inclusiveGateway' ? '○' : item.type === 'start' ? '●' : item.type === 'end' ? '◎' : '▣' }}</span>{{ item.name }}</button>
        </div>
      </aside>
      <div class="flow-canvas-viewport">
        <div class="flow-canvas-help">{{ connectFrom ? '点击目标节点完成连线；按 Esc 取消。' : '拖入节点 · 点击连线编辑条件 · 拖动画布平移 · 滚轮缩放' }}</div>
        <svg ref="stage" class="flow-canvas-stage" tabindex="0" role="img" aria-label="审批流程自由画布" @dragover.prevent @drop="onDrop" @pointerdown="onStageDown" @pointermove="onPointerMove" @pointerup="onPointerUp" @pointercancel="onPointerUp" @wheel="onWheel">
          <defs>
            <pattern id="approval-canvas-grid" width="16" height="16" patternUnits="userSpaceOnUse"><circle cx="1" cy="1" r="1" fill="#cbd8eb" /></pattern>
            <marker id="approval-canvas-arrow" markerWidth="9" markerHeight="9" refX="8" refY="4.5" orient="auto"><path d="M 0 0 L 9 4.5 L 0 9 z" fill="#8ba0bb" /></marker>
          </defs>
          <rect width="100%" height="100%" fill="#f8faff" />
          <g :transform="sceneTransform">
            <rect x="-5000" y="-5000" width="10000" height="10000" fill="url(#approval-canvas-grid)" />
            <g v-for="item in renderedEdges" :key="item.edge.id" class="flow-canvas-edge" :class="{ selected: selected?.id === item.edge.id, invalid: hasIssue(item.edge.id) }" @pointerdown.stop="choose('edge', item.edge.id)">
              <path :d="item.geometry.path" class="flow-canvas-edge-hit" />
              <path :d="item.geometry.path" class="flow-canvas-edge-path" marker-end="url(#approval-canvas-arrow)" />
              <g v-if="item.edge.condition || item.edge.isDefault" :transform="`translate(${item.geometry.labelX} ${item.geometry.labelY})`" class="flow-canvas-edge-label"><title>{{ item.edge.isDefault ? '其他情况' : item.edge.condition }}</title><rect x="-53" y="-13" width="106" height="22" rx="4" /><text text-anchor="middle" y="2">{{ item.edge.isDefault ? '其他情况' : (item.edge.condition?.length ?? 0) > 16 ? item.edge.condition?.slice(0, 16) + '…' : item.edge.condition }}</text></g>
            </g>
            <g v-for="node in nodes" :key="node.id" class="flow-canvas-node" :class="{ selected: selected?.id === node.id, invalid: hasIssue(node.id), connecting: connectFrom === node.id }" :transform="`translate(${node.x} ${node.y})`" role="button" tabindex="0" :aria-label="`${node.name}，${typeLabel(node.type)}`" @pointerdown="onNodeDown($event, node)" @keydown.enter.stop="choose('node', node.id)">
              <template v-if="node.type === 'start' || node.type === 'end'"><circle r="25" class="flow-canvas-event" /><circle v-if="node.type === 'end'" r="19" class="flow-canvas-event-inner" /><text class="flow-canvas-event-text" text-anchor="middle" y="5">{{ node.type === 'start' ? '始' : '终' }}</text></template>
              <template v-else-if="node.type.endsWith('Gateway')"><path d="M 0 -35 L 42 0 L 0 35 L -42 0 Z" class="flow-canvas-gateway" /><text class="flow-canvas-gateway-sign" text-anchor="middle" y="7">{{ node.type === 'exclusiveGateway' ? '×' : node.type === 'parallelGateway' ? '+' : '○' }}</text><text class="flow-canvas-gateway-name" text-anchor="middle" y="56">{{ node.name }}</text></template>
              <template v-else><rect x="-76" y="-29" width="152" height="58" rx="7" class="flow-canvas-card" /><rect x="-76" y="-29" width="152" height="17" rx="7" class="flow-canvas-card-top" /><text x="-64" y="-17" class="flow-canvas-card-type">{{ typeLabel(node.type) }}</text><text x="-64" y="10" class="flow-canvas-card-name">{{ node.name.length > 11 ? node.name.slice(0, 11) + '…' : node.name }}</text></template>
              <circle v-if="selected?.id === node.id && !readonly" cx="76" cy="0" r="8" class="flow-canvas-handle" @pointerdown.stop="connectFrom = node.id" />
            </g>
          </g>
        </svg>
        <div class="flow-canvas-controls"><button type="button" class="btn" aria-label="缩小" @click="changeZoom(.8)">−</button><span>{{ Math.round(zoom * 100) }}%</span><button type="button" class="btn" aria-label="放大" @click="changeZoom(1.25)">＋</button></div>
      </div>
      <aside class="flow-canvas-inspector" aria-label="画布属性">
        <slot name="inspector" :node="selectedNode" :edge="selectedEdge" :issues="visibleIssues">
          <div class="flow-canvas-pane-heading">{{ selectedNode ? '节点设置' : selectedEdge ? '连线设置' : '画布说明' }}</div>
          <div v-if="selectedNode" class="flow-canvas-inspector-body"><div class="flow-canvas-inspector-id">{{ selectedNode.id }} · {{ typeLabel(selectedNode.type) }}</div><label>节点名称<input :value="selectedNode.name" :disabled="readonly" @change="patchNode({ name: ($event.target as HTMLInputElement).value })"></label><template v-if="selectedNode.type.endsWith('Gateway')"><label>网关方向<select :value="selectedNode.gatewayDirection ?? 'split'" :disabled="readonly" @change="patchNode({ gatewayDirection: ($event.target as HTMLSelectElement).value as 'split' | 'join' })"><option value="split">分叉</option><option value="join">汇合</option></select></label><label>配对 ID<input :value="selectedNode.gatewayPairId ?? ''" :disabled="readonly" placeholder="分叉和汇合填写同一个 ID" @change="patchNode({ gatewayPairId: ($event.target as HTMLInputElement).value || null })"></label></template><p>完整审批规则、人员来源和字段权限可在工作台属性面板配置。</p></div>
          <div v-else-if="selectedEdge" class="flow-canvas-inspector-body"><div class="flow-canvas-inspector-id">{{ selectedEdge.sourceId }} → {{ selectedEdge.targetId }}</div><label>条件表达式<textarea :value="selectedEdge.condition ?? ''" :disabled="readonly || selectedEdge.isDefault" rows="4" placeholder="例如 ${amount} > 50000" @change="patchEdge(($event.target as HTMLTextAreaElement).value, selectedEdge!.isDefault, selectedEdge!.priority)"></textarea></label><label class="flow-canvas-check"><input type="checkbox" :checked="selectedEdge.isDefault" :disabled="readonly" @change="patchEdge(selectedEdge!.condition ?? '', ($event.target as HTMLInputElement).checked, selectedEdge!.priority)"> 其他情况（兜底）</label><label>优先级<input type="number" min="0" :value="selectedEdge.priority" :disabled="readonly" @change="patchEdge(selectedEdge!.condition ?? '', selectedEdge!.isDefault, Number(($event.target as HTMLInputElement).value))"></label><p>排他网关按数字从小到大尝试；包容网关会走所有命中的分支。</p></div>
          <div v-else class="flow-canvas-inspector-body"><p>从左侧拖入节点；选中节点后点击右侧连接点，再点目标节点即可连线。允许返工回路和跨分支连接。</p><p>流程数据由工作台保存；导入文件不会自动发布。</p></div>
          <div v-if="visibleIssues.length" class="flow-canvas-issues"><h3>检查问题 · {{ visibleIssues.length }}</h3><button v-for="(issue, index) in visibleIssues" :key="index" type="button" @click="issue.elementId && focusElement(issue.elementId)">{{ issue.message }}</button></div>
        </slot>
      </aside>
    </div>
    <div class="flow-canvas-footer"><span>{{ nodes.length }} 个节点 · {{ edges.length }} 条连线</span><span v-if="message" role="status">{{ message }}</span><span v-else>统一自由画布 · BPMN 常用子集</span></div>
  </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import BpmnViewer from 'bpmn-js/lib/Viewer'
import type { CenterProgressGraph } from '../../model/center.ts'
import { centerProgressXml } from './center-progress-diagram.ts'
import 'bpmn-js/dist/assets/diagram-js.css'
import 'bpmn-js/dist/assets/bpmn-js.css'

/**
 * 只接受服务端裁剪后的已到达路径，不读取完整流程定义。
 */
const props = defineProps<{
  /**
   * 已授权的进度图。
   */
  graph: CenterProgressGraph
}>()
/**
 * bpmn-js Viewer 的只读容器。
 */
const stage = ref<HTMLElement | null>(null)
/**
 * 标准 BPMN 只读查看器。
 */
let viewer: BpmnViewer | null = null
/**
 * 图形导入错误；不影响旁边的文字时间线。
 */
const error = ref('')
/**
 * 异步加载序号，避免旧图覆盖更新后的进度。
 */
let revision = 0
/**
 * 跟随详情面板尺寸变化重新适配完整图。
 */
let resizeObserver: ResizeObserver | null = null

/**
 * 把完整 BPMN 图居中并预留标识和标签所需空间。
 */
function fit(): void {
  if (!viewer || !stage.value || !props.graph.nodes.length) return
  const canvas = viewer.get('canvas') as {
    /** 缩放并以指定视口位置居中。 */
    zoom(value: string | number, center: { x: number; y: number }): number
  }
  const center = { x: stage.value.clientWidth / 2, y: stage.value.clientHeight / 2 }
  if (!center.x || !center.y) return
  const scale = canvas.zoom('fit-viewport', center)
  canvas.zoom(scale * .85, center)
}

/**
 * 使用 bpmn-js 导入安全图，并高亮服务端确认的当前节点。
 * @returns 导入完成。
 */
async function render(): Promise<void> {
  const current = ++revision
  if (!viewer) return
  if (!props.graph.nodes.length) { viewer.clear(); return }
  error.value = ''
  try {
    await viewer.importXML(centerProgressXml(props.graph))
    if (current !== revision || !viewer) return
    const canvas = viewer.get('canvas') as {
      /** 为一个 BPMN 图元添加高亮样式。 */
      addMarker(id: string, marker: string): void
    }
    fit()
    props.graph.nodes.forEach((node, index) => {
      if (node.isActive) canvas.addMarker(`Node_${index + 1}`, 'approval-current')
    })
  } catch (cause) {
    if (current !== revision || !viewer) return
    error.value = cause instanceof Error ? cause.message : '流程图暂时无法显示。'
  }
}

onMounted(() => {
  if (!stage.value) return
  viewer = new BpmnViewer({ container: stage.value })
  resizeObserver = new ResizeObserver(() => { fit() })
  resizeObserver.observe(stage.value)
  void render()
})
watch(() => props.graph, () => { void render() })
onBeforeUnmount(() => { revision++; resizeObserver?.disconnect(); viewer?.destroy(); viewer = null })
</script>

<template>
  <section class="center-progress" aria-label="已到达流程图">
    <p v-if="!graph.nodes.length" class="center-blank">暂无可显示的流程节点。</p>
    <p v-if="error" role="status" class="center-blank">流程图无法显示，以下时间线仍可查看：{{ error }}</p>
    <div ref="stage" class="center-progress-stage"></div>
    <p v-if="graph.nodes.length" class="center-progress-caption">只显示已到达的路径；蓝色节点为当前环节。</p>
  </section>
</template>

<style scoped>
.center-progress-stage { width: 100%; height: 340px; border: 1px solid var(--line); border-radius: 6px; background: #fbfcff; }
.center-progress-caption { margin: 8px 0 0; color: var(--muted); font-size: 12px; }
.center-progress-stage :deep(.djs-element.approval-current .djs-visual > :first-child) { stroke: #2563bc !important; fill: #e8f0ff !important; stroke-width: 3px !important; }
.center-progress-stage :deep(.bjs-powered-by) { display: block !important; }
</style>

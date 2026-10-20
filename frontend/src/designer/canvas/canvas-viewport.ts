/**
 * 自由画布视口的缩放与整图适配。
 */
import type { Ref } from 'vue'
import type { FlowCanvas } from '../../model/flow.ts'
import { canvasBounds } from './canvas-model.ts'
import { screenToCanvas } from './canvas-geometry.ts'

/**
 * 将视口操作绑定到组件持有的响应式状态。
 * @param stage SVG 视口。
 * @param zoom 缩放比例。
 * @param offsetX 水平偏移。
 * @param offsetY 垂直偏移。
 * @param canvas 当前流程画布。
 * @returns 滚轮、按钮和适配视图操作。
 */
export function useCanvasViewport(stage: Ref<SVGSVGElement | null>, zoom: Ref<number>, offsetX: Ref<number>, offsetY: Ref<number>, canvas: () => FlowCanvas) {
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
    const bounds = canvasBounds(canvas())
    zoom.value = Math.max(.35, Math.min(1.35, Math.min(stage.value.clientWidth / bounds.width, stage.value.clientHeight / bounds.height)))
    offsetX.value = (stage.value.clientWidth - bounds.width * zoom.value) / 2 - bounds.x * zoom.value
    offsetY.value = (stage.value.clientHeight - bounds.height * zoom.value) / 2 - bounds.y * zoom.value
  }

  return { onWheel, changeZoom, fit }
}

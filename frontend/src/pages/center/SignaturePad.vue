<script setup lang="ts">
import { ref } from 'vue'
import { uploadCenterSignature } from '../../api/center-signatures.ts'

/**
 * 本次上传是否可作为本人的个人签名重复选择。
 */
const props = defineProps<{
  /**
   * 个人设置上传为真，任务中的单次签名为假。
   */
  personal: boolean
}>()

/**
 * 保存后把新资产 ID 交给父组件，由父组件要求用户明确选用。
 */
const emit = defineEmits<{
  /**
   * 上传已完成。
   */
  saved: [id: string]
}>()

/**
 * 固定像素的 PNG 画布。
 */
const canvas = ref<HTMLCanvasElement | null>(null)
/**
 * 当前是否按住指针绘制。
 */
const drawing = ref(false)
/**
 * 当前是否已有笔迹。
 */
const hasInk = ref(false)
/**
 * 上传进行中。
 */
const busy = ref(false)
/**
 * 可修正的上传错误。
 */
const error = ref('')

/**
 * 把指针位置换算为画布固定像素坐标。
 * @param event 浏览器指针事件。
 * @returns 画布坐标。
 */
function point(event: PointerEvent): { x: number, y: number } {
  const surface = canvas.value!
  const bounds = surface.getBoundingClientRect()
  return {
    x: (event.clientX - bounds.left) * surface.width / bounds.width,
    y: (event.clientY - bounds.top) * surface.height / bounds.height,
  }
}

/**
 * 开始一笔并捕获指针，触屏拖动不会滚动页面。
 * @param event 指针事件。
 */
function start(event: PointerEvent): void {
  if (busy.value || !canvas.value) return
  const surface = canvas.value
  surface.setPointerCapture(event.pointerId)
  const context = surface.getContext('2d')!
  const position = point(event)
  context.lineCap = 'round'
  context.lineJoin = 'round'
  context.lineWidth = 3
  context.strokeStyle = '#172b4d'
  context.beginPath()
  context.moveTo(position.x, position.y)
  drawing.value = true
}

/**
 * 延续当前笔迹。
 * @param event 指针事件。
 */
function move(event: PointerEvent): void {
  if (!drawing.value || !canvas.value) return
  const position = point(event)
  const context = canvas.value.getContext('2d')!
  context.lineTo(position.x, position.y)
  context.stroke()
  hasInk.value = true
}

/**
 * 结束当前笔迹。
 */
function stop(): void {
  drawing.value = false
}

/**
 * 清空本地笔迹；已上传的资产保持不可变。
 */
function clear(): void {
  const surface = canvas.value
  if (!surface) return
  surface.getContext('2d')?.clearRect(0, 0, surface.width, surface.height)
  drawing.value = false
  hasInk.value = false
  error.value = ''
}

/**
 * 导出 PNG 并上传，上传后仍需在审批表单中明确选用。
 * @returns 上传完成。
 */
async function save(): Promise<void> {
  if (!canvas.value || !hasInk.value || busy.value) return
  busy.value = true
  error.value = ''
  try {
    const surface = canvas.value
    const png = await new Promise<Blob>((resolve, reject) => surface.toBlob(blob =>
      blob ? resolve(blob) : reject(new Error('签名图片生成失败。')), 'image/png'))
    const asset = await uploadCenterSignature(png, props.personal)
    emit('saved', asset.id)
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '签名保存失败，请重试。'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="signature-pad">
    <p>请在下方手写签名，保存后再明确选择用于本次审批。</p>
    <canvas ref="canvas" width="600" height="220" aria-label="手写签名画布"
      @pointerdown="start" @pointermove="move" @pointerup="stop" @pointercancel="stop" />
    <div class="signature-pad-actions">
      <button class="btn" type="button" :disabled="busy" @click="clear">清空重写</button>
      <button class="btn" type="button" :disabled="busy || !hasInk" @click="save">{{ busy ? '保存中…' : personal ? '保存为个人签名' : '保存本次签名' }}</button>
    </div>
    <p v-if="error" role="alert" class="center-login-error">{{ error }}</p>
  </div>
</template>

<style scoped>
.signature-pad canvas { display: block; width: 100%; max-width: 600px; height: 220px; border: 1px solid #cad5e2; border-radius: 8px; background: #fff; touch-action: none; cursor: crosshair; }
.signature-pad-actions { display: flex; gap: 8px; margin-top: 8px; }
.signature-pad p { margin: 5px 0 8px; font-size: 13px; color: #53647b; }
</style>

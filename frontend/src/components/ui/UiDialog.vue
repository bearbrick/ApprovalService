<script setup lang="ts">
/**
 * 使用原生模态对话框管理焦点和键盘范围，开关状态由调用方持有。
 */
import { onBeforeUnmount, onMounted, ref, useId, watch, type VNode } from 'vue'
import UiIcon from './UiIcon.vue'

/**
 * 对话框受控属性。
 */
const props = defineProps<{
  /**
   * 是否显示对话框。
   */
  open: boolean
  /**
   * 对话框标题，同时作为无障碍名称。
   */
  title: string
}>()

/**
 * 关闭请求事件；调用方收到后应将 open 设为 false。
 */
const emit = defineEmits<{
  /**
   * 用户通过关闭按钮、遮罩或 Escape 请求关闭。
   */
  close: []
}>()

/**
 * 对话框内容和可选底部操作区域。
 */
defineSlots<{
  /**
   * 正文区域，可包含表单等交互内容。
   * @returns 对话框正文节点。
   */
  default?(): VNode[]
  /**
   * 底部操作区域，由调用方决定保存和取消等操作。
   * @returns 底部按钮节点。
   */
  footer?(): VNode[]
}>()

/**
 * 当前组件实例唯一的标题编号。
 */
const titleId = useId()

/**
 * 原生对话框元素。
 */
const dialog = ref<HTMLDialogElement | null>(null)

/**
 * 打开前拥有焦点的元素，关闭时恢复。
 */
let previousFocus: HTMLElement | null = null

/**
 * 当前打开周期是否已经通知关闭，避免多个原生事件重复发出请求。
 */
let closeReported = false

/**
 * 按下鼠标或触屏的位置是否位于遮罩，避免从正文拖出后误关闭。
 */
let pointerStartedOutside = false

/**
 * 关闭后恢复原来的焦点；原元素已卸载时不干预新页面。
 */
function restoreFocus(): void {
  const target = previousFocus
  previousFocus = null
  if (target?.isConnected) {
    target.focus({ preventScroll: true })
  }
}

/**
 * 将受控属性同步到原生对话框。
 */
function syncDialog(): void {
  const element = dialog.value
  if (!element) return
  if (props.open && !element.open) {
    previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    closeReported = false
    pointerStartedOutside = false
    element.showModal()
  } else if (!props.open && element.open) {
    // 父组件主动关闭时不反向通知，只释放原生模态状态。
    closeReported = true
    element.close()
    restoreFocus()
  }
}

/**
 * 请求关闭并立即释放模态状态，调用方随后同步受控属性。
 */
function requestClose(): void {
  if (closeReported) return
  closeReported = true
  dialog.value?.close()
  restoreFocus()
  emit('close')
}

/**
 * 接收原生关闭事件，包括正文中 method="dialog" 表单产生的关闭。
 */
function onNativeClose(): void {
  if (dialog.value?.open) return
  restoreFocus()
  if (props.open && !closeReported) {
    closeReported = true
    emit('close')
  }
}

/**
 * 判断指针是否落在对话框外部遮罩。
 * @param event 当前指针或鼠标事件。
 * @returns 是否在元素边界之外。
 */
function isOutside(event: MouseEvent): boolean {
  const element = dialog.value
  if (!element || event.target !== element) return false
  const bounds = element.getBoundingClientRect()
  return event.clientX < bounds.left || event.clientX > bounds.right
    || event.clientY < bounds.top || event.clientY > bounds.bottom
}

/**
 * 记录按下位置，避免正文内拖选到遮罩时误触关闭。
 * @param event 原生指针按下事件。
 */
function onPointerDown(event: PointerEvent): void {
  pointerStartedOutside = isOutside(event)
}

/**
 * 只有按下和释放都在遮罩时才请求关闭。
 * @param event 原生单击事件。
 */
function onBackdropClick(event: MouseEvent): void {
  if (pointerStartedOutside && isOutside(event)) requestClose()
  pointerStartedOutside = false
}

watch(() => props.open, syncDialog, { flush: 'post' })
onMounted(syncDialog)
onBeforeUnmount(() => {
  // 组件从页面卸载时也要解除原生 top layer，恢复仍存在的触发元素。
  closeReported = true
  dialog.value?.close()
  restoreFocus()
})
</script>

<template>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="ui-dialog"
      :aria-labelledby="titleId"
      @cancel.prevent="requestClose"
      @close="onNativeClose"
      @pointerdown="onPointerDown"
      @click="onBackdropClick"
    >
      <header class="ui-dialog__header">
        <h2 :id="titleId">{{ title }}</h2>
        <button class="icon-btn" type="button" aria-label="关闭对话框" @click="requestClose">
          <UiIcon name="close" />
        </button>
      </header>
      <div class="ui-dialog__body"><slot /></div>
      <footer v-if="$slots.footer" class="ui-dialog__footer"><slot name="footer" /></footer>
    </dialog>
  </Teleport>
</template>

<script setup lang="ts">
/**
 * 子页面失败时显示可恢复的边界，避免整个公共外壳白屏。
 */
import { onErrorCaptured, ref, type ComponentPublicInstance, type VNode } from 'vue'
import UiEmptyState from './UiEmptyState.vue'
import UiIcon from './UiIcon.vue'

/**
 * 需要隔离错误的页面内容。
 */
defineSlots<{
  /**
   * 错误恢复后重新挂载的子组件树。
   * @returns 被保护的页面节点。
   */
  default?(): VNode[]
}>()

/**
 * 是否捕获到当前子树错误。
 */
const failed = ref(false)

/**
 * 重试版本，每次重试都创建全新的子组件实例。
 */
const revision = ref(0)

/**
 * 捕获子组件渲染和生命周期异常，界面不暴露异常内容。
 * @param error 原始异常，仅开发环境输出诊断信息。
 * @param instance 发生异常的子组件实例。
 * @param info Vue 提供的异常阶段。
 * @returns false 表示错误已由当前边界处理。
 */
function captureError(error: unknown, instance: ComponentPublicInstance | null, info: string): false {
  failed.value = true
  // 保留开发诊断信息，但不把错误对象、堆栈或内部路径放到用户界面。
  if (import.meta.env.DEV) {
    console.error('页面组件执行失败。', error, instance?.$options.name, info)
  }
  return false
}

/**
 * 清除错误状态并重新挂载子树，不自动重发任何业务写请求。
 */
function retry(): void {
  revision.value += 1
  failed.value = false
}

onErrorCaptured(captureError)
</script>

<template>
  <div v-if="failed" class="ui-error-boundary" role="alert">
    <UiEmptyState
      title="页面暂时无法显示"
      description="请重试加载当前页面。如果仍无法恢复，请刷新页面或联系管理员。"
      icon="alert"
    >
      <template #action>
        <button class="btn btn-primary" type="button" @click="retry">
          <UiIcon name="refresh" />重试加载
        </button>
      </template>
    </UiEmptyState>
  </div>
  <div v-else :key="revision" class="ui-error-boundary__content"><slot /></div>
</template>

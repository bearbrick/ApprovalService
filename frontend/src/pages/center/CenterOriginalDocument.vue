<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { fetchCenterDocumentAddress } from '../../api/center-document.ts'

/**
 * 一张已授权审批单的标识。
 */
const props = defineProps<{
  /**
   * 当前详情中的审批单 ID。
   */
  instanceId: string
}>()

/**
 * 原单加载状态向父详情同步，以决定是否显示普通详情。
 */
const emit = defineEmits<{
  /**
   * 当前 iframe 是否已加载。
   */
  ready: [value: boolean]
}>()

/**
 * 仅在本组件内存中持有临时地址，卸载即丢弃。
 */
const url = ref('')
/**
 * 回退普通详情时的简短提示。
 */
const notice = ref('')
/**
 * 过期或网络异常时放弃嵌入。
 */
let timer: ReturnType<typeof setTimeout> | undefined
/**
 * 使旧响应和旧 iframe 事件不能覆盖新审批单。
 */
let revision = 0

/**
 * 清理地址并回退安全普通详情。
 * @param message 回退原因。
 * @returns 无返回值。
 */
function fallback(message: string): void {
  if (timer) clearTimeout(timer)
  timer = undefined
  url.value = ''
  notice.value = message
  emit('ready', false)
}

/**
 * 在已授权详情显示后才向服务端请求当前人的临时地址。
 * @returns 请求完成。
 */
async function load(): Promise<void> {
  const current = ++revision
  fallback('')
  try {
    const result = await fetchCenterDocumentAddress(props.instanceId)
    if (current !== revision) return
    const parsed = new URL(result.url)
    if (parsed.protocol !== 'https:' || Date.parse(result.expiresAt) <= Date.now()) {
      fallback('业务原单地址已失效，以下显示已授权的审批数据。')
      return
    }
    url.value = result.url
    timer = setTimeout(() => {
      if (current === revision) fallback('业务原单加载超时，以下显示已授权的审批数据。')
    }, 10000)
  } catch {
    if (current === revision) fallback('暂时无法显示业务原单，以下显示已授权的审批数据。')
  }
}

/**
 * iframe 成功加载后移除普通数据展示。
 * @returns 无返回值。
 */
function loaded(): void {
  if (timer) clearTimeout(timer)
  timer = undefined
  if (url.value) emit('ready', true)
}

watch(() => props.instanceId, () => { void load() }, { immediate: true })
onBeforeUnmount(() => {
  revision++
  fallback('')
})
</script>

<template>
  <div class="center-original-document">
    <p v-if="notice" class="center-blank" role="status">{{ notice }}</p>
    <p v-else-if="!url" class="center-blank">正在读取业务原单…</p>
    <iframe v-if="url" :src="url" title="业务原单（只读）" referrerpolicy="no-referrer"
      sandbox="allow-scripts allow-same-origin" class="center-original-frame" @load="loaded"
      @error="fallback('业务原单加载失败，以下显示已授权的审批数据。')"></iframe>
  </div>
</template>

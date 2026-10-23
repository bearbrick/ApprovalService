<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { AppRoute } from '../model/routes.ts'
import type { CenterDetail, CenterItem } from '../model/center.ts'
import { centerAdapter, CenterAdapterUnavailableError } from '../api/center-adapter.ts'
import { mobileCenterSection } from '../model/mobile.ts'
import MobileList from './mobile/MobileList.vue'
import MobileDetail from './mobile/MobileDetail.vue'
import MobileMe from './mobile/MobileMe.vue'
import '../styles/mobile-approval.css'

/**
 * 当前手机路由，只读数据来自与电脑端相同的授权适配器。
 */
const props = defineProps<{
  /**
   * 手机审批中心路由。
   */
  route: AppRoute
}>()

/**
 * 手机当前页面的读取状态。
 */
const state = ref<'loading' | 'ready' | 'unavailable' | 'offline' | 'error'>('loading')
/**
 * 读取失败的中文信息。
 */
const error = ref('')
/**
 * 仅由已授权适配器返回的列表。
 */
const items = ref<readonly CenterItem[]>([])
/**
 * 仅由已授权适配器返回的详情。
 */
const detail = ref<CenterDetail | null>(null)
/**
 * 当前手机路由对应的审批列表分组。
 */
const section = computed(() => mobileCenterSection(props.route.section))
/**
 * 浏览器报告的联机状态，仅用于解释读取失败。
 */
const online = ref(typeof navigator === 'undefined' || navigator.onLine)
/**
 * 上次读取使用的取消控制器。
 */
let request: AbortController | null = null

/**
 * 读取当前页的真实授权数据；旧路由的响应不得覆盖新路由。
 * @returns 本次读取结束。
 */
async function load(): Promise<void> {
  request?.abort()
  items.value = []
  detail.value = null
  error.value = ''
  if (props.route.section === 'me' && !props.route.resourceId) { state.value = 'unavailable'; return }
  if (!online.value) { state.value = 'offline'; return }
  const current = new AbortController()
  request = current
  state.value = 'loading'
  try {
    if (props.route.resourceId) detail.value = await centerAdapter.loadDetail(props.route.resourceId, current.signal)
    else if (section.value) items.value = await centerAdapter.loadList(section.value, current.signal)
    if (request === current && !current.signal.aborted) state.value = 'ready'
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    state.value = !online.value ? 'offline' : cause instanceof CenterAdapterUnavailableError ? 'unavailable' : 'error'
    error.value = cause instanceof Error ? cause.message : '手机审批数据暂时无法读取。'
  }
}

/**
 * 在网络恢复时同步状态并重新读取当前页面。
 */
function handleOnline(): void {
  online.value = true
  void load()
}

/**
 * 断网时取消在途请求并展示离线状态。
 */
function handleOffline(): void {
  online.value = false
  request?.abort()
  items.value = []
  detail.value = null
  if (props.route.section !== 'me') state.value = 'offline'
}

watch(() => [props.route.section, props.route.resourceId], () => { void load() }, { immediate: true })
onMounted(() => { window.addEventListener('online', handleOnline); window.addEventListener('offline', handleOffline) })
onBeforeUnmount(() => { request?.abort(); window.removeEventListener('online', handleOnline); window.removeEventListener('offline', handleOffline) })
</script>

<template>
  <div class="mobile-approval-page">
    <MobileDetail v-if="route.resourceId" :detail="detail" :state="state" :error="error" @reload="load" />
    <MobileMe v-else-if="route.section === 'me'" />
    <MobileList v-else-if="section" :section="section" :items="items" :state="state" :error="error" @reload="load" />
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import type { AppRoute } from '../model/routes.ts'
import type { CenterDetail, CenterItem, CenterSection } from '../model/center.ts'
import { centerAdapter, CenterAdapterUnavailableError } from '../api/center-adapter.ts'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import CenterList from './center/CenterList.vue'
import CenterDetailView from './center/CenterDetailView.vue'
import '../styles/center.css'

/**
 * 公共路由解析后的审批中心页面。
 */
const props = defineProps<{
  /**
   * 当前桌面审批中心地址。
   */
  route: AppRoute
}>()

/**
 * 四类列表入口，详情页由路径参数识别。
 */
const sections = [
  { key: 'todo', title: '待我处理', hash: '#/' },
  { key: 'done', title: '我已处理', hash: '#/center/done' },
  { key: 'cc', title: '抄送我的', hash: '#/center/cc' },
  { key: 'started', title: '我发起的', hash: '#/center/started' },
] as const
/**
 * 当前列表分组。
 */
const section = computed<CenterSection>(() => sections.find(item => item.key === props.route.section)?.key ?? 'todo')
/**
 * 列表或详情查询状态。
 */
const state = ref<'loading' | 'ready' | 'error' | 'unavailable'>('loading')
/**
 * 失败时的中文说明。
 */
const error = ref('')
/**
 * 当前用户有权查看的列表数据。
 */
const items = ref<readonly CenterItem[]>([])
/**
 * 当前用户有权查看的详情。
 */
const detail = ref<CenterDetail | null>(null)
/**
 * 正在进行的请求，用于避免切换页面后的旧响应覆盖新页面。
 */
let request: AbortController | null = null

/**
 * 读取当前路由对应的列表或详情。
 * @returns 请求完成。
 */
async function load(): Promise<void> {
  request?.abort()
  items.value = []
  detail.value = null
  error.value = ''
  if (props.route.section === 'settings') { state.value = 'unavailable'; return }
  const current = new AbortController()
  request = current
  state.value = 'loading'
  try {
    if (props.route.resourceId) detail.value = await centerAdapter.loadDetail(props.route.resourceId, current.signal)
    else items.value = await centerAdapter.loadList(section.value, current.signal)
    if (request === current && !current.signal.aborted) state.value = 'ready'
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    state.value = cause instanceof CenterAdapterUnavailableError ? 'unavailable' : 'error'
    error.value = cause instanceof Error ? cause.message : '审批中心暂时无法读取数据。'
  }
}

watch(() => [props.route.section, props.route.resourceId], () => { void load() }, { immediate: true })
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <header class="page-header center-page-header"><div class="breadcrumb"><a href="#/">审批服务</a><span>/</span><span>{{ route.resourceId ? '审批详情' : route.title }}</span></div><div class="page-title-row"><h1 id="page-title" tabindex="-1">{{ route.resourceId ? '审批详情' : '审批中心' }}</h1><span class="badge">数据接口待接入</span></div><p>统一查看待办与审批进度，业务单据由来源系统维护。</p></header>
  <div class="page-body center-page-body">
    <nav v-if="!route.resourceId" class="center-tabs" aria-label="审批列表"><a v-for="item in sections" :key="item.key" :href="item.hash" :aria-current="route.section === item.key ? 'page' : undefined">{{ item.title }}</a><a href="#/center/settings" :aria-current="route.section === 'settings' ? 'page' : undefined">个人设置</a></nav>
    <CenterDetailView v-if="route.resourceId" :requested-id="route.resourceId" :detail="detail" :state="state" :error="error" @reload="load" />
    <section v-else-if="route.section === 'settings'" class="center-card center-settings-empty"><UiEmptyState title="个人设置尚未接入" description="委托、签名、常用语和提醒偏好将在账号与审批中心接口接入后开放。当前不会保存任何设置。" icon="info" /></section>
    <CenterList v-else :section="section" :items="items" :state="state" :error="error" @reload="load" />
    <p class="workspace-note">审批中心只处理审批与查看进度，不创建或修改业务单据。</p>
  </div>
</template>

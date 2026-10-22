<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import type { AppRoute } from '../model/routes.ts'
import { ADMIN_TABS, TAB_TITLES } from '../model/navigation.ts'
import { resolveAdminView } from '../model/admin.ts'
import type { AdminViewData } from '../model/admin.ts'
import { adminAdapter, AdminAdapterUnavailableError } from '../api/admin-adapter.ts'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import AdminOverview from './admin/AdminOverview.vue'
import AdminTable from './admin/AdminTable.vue'
import AdminIntervention from './admin/AdminIntervention.vue'
import '../styles/admin.css'

/**
 * 管理后台当前已解析的路由。
 */
const props = defineProps<{
  /**
   * 管理一级模块和二级页签。
   */
  route: AppRoute
}>()

/**
 * 由路由决定的当前正式页面定义。
 */
const view = computed(() => resolveAdminView(props.route.section, props.route.tab))
/**
 * 当前模块的二级页签，不涉及权限判断。
 */
const tabs = computed(() => (ADMIN_TABS[props.route.section] ?? []).map(key => ({ key, title: TAB_TITLES[key], hash: `#/admin/${props.route.section}/${key}` })))
/**
 * 管理数据查询状态。
 */
const state = ref<'loading' | 'ready' | 'error' | 'unavailable'>('loading')
/**
 * 授权后得到的只读行与指标。
 */
const data = ref<AdminViewData | null>(null)
/**
 * 当前查询失败时的中文说明。
 */
const error = ref('')
/**
 * 路由切换时取消旧请求，防止旧数据覆盖新页面。
 */
let request: AbortController | null = null

/**
 * 只读取当前管理员有权查看的页面数据。
 * @returns 查询完成。
 */
async function load(): Promise<void> {
  request?.abort()
  data.value = null
  error.value = ''
  if (!view.value) { state.value = 'error'; error.value = '管理页面不存在。'; return }
  const current = new AbortController()
  request = current
  state.value = 'loading'
  try {
    const result = await adminAdapter.loadView(view.value.key, current.signal)
    if (request === current && !current.signal.aborted) { data.value = result; state.value = 'ready' }
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    state.value = cause instanceof AdminAdapterUnavailableError ? 'unavailable' : 'error'
    error.value = cause instanceof Error ? cause.message : '暂时无法读取管理数据。'
  }
}

watch(() => [props.route.section, props.route.tab], () => { void load() }, { immediate: true })
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <header class="page-header admin-page-header"><div class="breadcrumb"><a href="#/">审批服务</a><span>/</span><span>管理后台</span><template v-if="view && view.section !== 'overview'"><span>/</span><span>{{ view.title }}</span></template></div><div class="page-title-row"><h1 id="page-title" tabindex="-1">{{ view?.title ?? '管理后台' }}</h1><span class="badge">权限接口待接入</span></div><p>{{ view?.description ?? '查看管理模块。' }}</p></header>
  <div class="page-body admin-page-body">
    <div class="admin-guard-note" role="status"><strong>管理员权限尚未接入</strong><span>页面仅呈现结构；当前不会读取真实配置、展示密钥或执行管理操作。</span></div>
    <nav v-if="tabs.length" class="admin-tabs" aria-label="管理二级页签"><a v-for="tab in tabs" :key="tab.key" :href="tab.hash" :aria-current="route.tab === tab.key ? 'page' : undefined">{{ tab.title }}</a></nav>
    <template v-if="view">
      <AdminOverview v-if="view.kind === 'overview'" :view="view" :rows="data?.rows ?? []" :metrics="data?.metrics ?? []" :state="state" :error="error" @reload="load" />
      <template v-else><div v-if="view.section === 'flows'" class="admin-flow-link">画布与流程规则在<a href="#/designer">流程设计</a>中维护；发布与版本管理待管理权限接口接入。</div><AdminTable :view="view" :rows="data?.rows ?? []" :state="state" :error="error" @reload="load" /><AdminIntervention v-if="view.kind === 'runtime'" /></template>
    </template>
    <section v-else class="admin-panel"><UiEmptyState title="没有找到这个管理页面" description="请从管理导航重新选择。" icon="search" /></section>
    <p class="workspace-note">审批服务只管理流程与审批记录；业务单据由来源业务系统维护。</p>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import { filterCenterItems } from '../../model/center.ts'
import type { CenterFilters, CenterItem, CenterSection } from '../../model/center.ts'
import { mobileDetailHash, shouldRefreshOnPull } from '../../model/mobile.ts'

/**
 * 只读列表和数据读取状态。
 */
const props = defineProps<{
  /**
   * 当前列表分组。
   */
  section: CenterSection
  /**
   * 当前用户有权查看的审批摘要。
   */
  items: readonly CenterItem[]
  /**
   * 页面读取状态。
   */
  state: 'loading' | 'ready' | 'unavailable' | 'offline' | 'error'
  /**
   * 失败时的中文说明。
   */
  error: string
}>()

/**
 * 用户主动请求重新读取。
 */
const emit = defineEmits<{
  /**
   * 点击刷新或下拉刷新。
   */
  reload: []
}>()

/**
 * 只在当前授权记录内筛选。
 */
const filters = ref<CenterFilters>({ search: '', flowCode: '', status: '', dateFrom: '', dateTo: '' })
/**
 * 搜索和流程筛选后的记录。
 */
const visibleItems = computed(() => filterCenterItems(props.items, props.section, filters.value))
/**
 * 当前授权列表包含的流程。
 */
const flows = computed(() => [...new Map(props.items.map(item => [item.flowCode, item.flowName])).entries()])
/**
 * 触摸开始位置，非触摸状态为空。
 */
let touchStartY: number | null = null
/**
 * 触摸开始时页面滚动位置。
 */
let touchScrollTop = 0
/**
 * 手势是否从输入区域开始。
 */
let touchWithinInput = false

watch(() => props.section, () => { filters.value = { search: '', flowCode: '', status: '', dateFrom: '', dateTo: '' } })

/**
 * 记录触摸起点，不阻止浏览器原生滚动。
 * @param event 原生触摸事件。
 */
function onTouchStart(event: TouchEvent): void {
  touchStartY = event.touches[0]?.clientY ?? null
  touchScrollTop = window.scrollY
  touchWithinInput = event.target instanceof Element && !!event.target.closest('input, select, textarea, button, a')
}

/**
 * 达到安全阈值时发起一次只读刷新。
 * @param event 原生触摸事件。
 */
function onTouchEnd(event: TouchEvent): void {
  if (touchStartY !== null && props.state !== 'loading' && shouldRefreshOnPull(touchStartY, event.changedTouches[0]?.clientY ?? touchStartY, touchScrollTop, touchWithinInput)) emit('reload')
  touchStartY = null
}
</script>

<template>
  <div class="mobile-list" @touchstart.passive="onTouchStart" @touchend.passive="onTouchEnd">
    <div class="mobile-list-heading"><div><p class="mobile-eyebrow">审批中心</p><h1 id="page-title" tabindex="-1">{{ section === 'todo' ? '待办审批' : section === 'done' ? '已办审批' : '抄送我的' }}</h1><p>只展示当前账号有权查看的审批记录。</p></div><button class="mobile-refresh" type="button" :disabled="state === 'loading'" aria-label="刷新审批列表" @click="emit('reload')"><UiIcon name="refresh" /></button></div>
    <div class="mobile-list-search"><UiIcon name="search" /><input v-model="filters.search" type="search" placeholder="搜索标题、编号、业务单号" aria-label="搜索审批记录" :disabled="state !== 'ready'"></div>
    <div class="mobile-filter-row"><label><span class="sr-only">按流程筛选</span><select v-model="filters.flowCode" :disabled="state !== 'ready'"><option value="">全部流程</option><option v-for="[code, name] in flows" :key="code" :value="code">{{ name }}</option></select></label><span v-if="state === 'ready'">{{ visibleItems.length }} 项</span><span v-else>下拉可刷新</span></div>
    <template v-if="state === 'ready'"><a v-for="item in visibleItems" :key="item.id" class="mobile-approval-card" :href="mobileDetailHash(item.id)"><div class="mobile-card-top"><span>{{ item.flowName }}</span><em v-if="item.urgent">加急</em><span v-else class="mobile-status">{{ item.status }}</span></div><h2><i v-if="item.unread" aria-label="未读"></i>{{ item.title }}</h2><div class="mobile-card-meta"><span>{{ item.initiatorName }}</span><span>{{ item.updatedAt }}</span></div><div v-if="item.summary.length" class="mobile-card-summary"><span>{{ item.summary[0]!.label }}</span><strong>{{ item.summary[0]!.value }}</strong></div><div class="mobile-card-foot"><span>{{ item.currentStep }}</span><span>{{ item.serialNo || item.businessKey }} ›</span></div></a><UiEmptyState v-if="!visibleItems.length" title="没有匹配的审批记录" description="调整搜索词或流程筛选，或稍后刷新。" icon="search" /></template>
    <UiEmptyState v-else-if="state === 'loading'" title="正在读取审批记录" description="请稍候。" icon="refresh" />
    <UiEmptyState v-else-if="state === 'unavailable'" title="手机审批尚未接入" description="手机列表页面已就绪，真实登录与审批中心接口接入后才能读取待办。" icon="info" />
    <UiEmptyState v-else-if="state === 'offline'" title="当前没有网络" description="联网后会自动重试，也可以手动刷新。" icon="alert"><template #action><button class="btn" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
    <UiEmptyState v-else title="读取审批记录失败" :description="error" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
    <p class="mobile-list-note">业务单据由来源系统创建和维护。</p>
  </div>
</template>

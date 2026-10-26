<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NAV_ITEMS } from '../../model/routes.ts'
import type { AppRoute } from '../../model/routes.ts'
import { SECTION_ITEMS } from '../../model/navigation.ts'
import UiIcon from '../ui/UiIcon.vue'
import UiDialog from '../ui/UiDialog.vue'

/**
 * 桌面布局所需的路由与真实服务状态。
 */
const props = defineProps<{
  /**
   * 当前页面路由。
   */
  route: AppRoute
  /**
   * 服务状态的中文说明。
   */
  serviceLabel: string
}>()
/**
 * 公共服务信息入口。
 */
const emit = defineEmits<{
  /**
   * 请求打开服务连接信息。
   */
  service: []
}>()
/**
 * 导航筛选关键字。
 */
const search = ref('')
/**
 * 窄屏导航对话框开关。
 */
const menuOpen = ref(false)
/**
 * 当前工作区的全部入口。
 */
const sections = computed(() => SECTION_ITEMS[props.route.page === 'not-found' ? 'center' : props.route.page])
/**
 * 当前筛选结果，仅筛选导航名称。
 */
const filtered = computed(() => sections.value.filter(item => item.title.includes(search.value.trim())))
/**
 * 当前工作区名称。
 */
const workspaceTitle = computed(() => NAV_ITEMS.find(item => item.key === props.route.page)?.title ?? '工作区')
/**
 * 将键盘焦点移到正文，不改变用于路由的 hash。
 */
function focusContent(): void {
  document.querySelector<HTMLElement>('#workspace-content')?.focus({ preventScroll: true })
  window.scrollTo(0, 0)
}
watch(() => props.route.hash, () => { menuOpen.value = false; search.value = '' })
</script>

<template>
  <a class="skip-link" href="#workspace-content" @click.prevent="focusContent">跳至内容</a>
  <header class="topbar">
    <button class="icon-btn menu-toggle" aria-label="打开工作区导航" :aria-expanded="menuOpen" @click="menuOpen = true"><UiIcon name="menu" /></button>
    <a class="brand" href="#/" aria-label="审批服务首页"><img src="/branding/company-logo.png" alt="智云科技"><strong>审批服务</strong></a>
    <span class="brand-subtitle">流程设计与审批工作台</span>
    <nav class="top-navigation" aria-label="主导航">
      <a v-for="item in NAV_ITEMS" :key="item.key" :href="item.hash" :aria-label="item.title" :aria-current="route.page === item.key ? 'page' : undefined"><UiIcon :name="item.key" /><span>{{ item.title }}</span></a>
      <a href="#/m" aria-label="手机审批"><UiIcon name="mobile" /><span>手机审批</span></a>
    </nav>
    <button class="service-trigger" aria-label="查看服务连接状态" @click="emit('service')"><UiIcon name="info" /><span>{{ serviceLabel }}</span></button>
  </header>
  <aside v-if="route.page !== 'designer'" class="desktop-sidebar" aria-label="工作区导航">
    <div class="sidebar-title"><h2>{{ workspaceTitle }}</h2></div>
    <label class="nav-search"><UiIcon name="search" /><input v-model="search" aria-label="搜索工作区导航" placeholder="搜索功能"></label>
    <nav class="side-navigation" :aria-label="workspaceTitle + '功能'">
      <a v-for="item in filtered" :key="item.key" :href="item.hash" :aria-current="route.section === item.key ? 'page' : undefined"><span>{{ item.title }}</span><UiIcon name="chevron" /></a>
      <p v-if="!filtered.length" class="muted search-empty" role="status">没有匹配的功能</p>
    </nav>
    <div class="sidebar-footer"><span>审批服务 · 独立工作区</span><button class="btn" @click="emit('service')"><UiIcon name="info" />服务连接</button></div>
  </aside>
  <main id="workspace-content" class="workspace" :class="{ 'designer-workspace-shell': route.page === 'designer' }" tabindex="-1"><slot /></main>
  <UiDialog :open="menuOpen" title="工作区导航" @close="menuOpen = false">
    <nav class="drawer-navigation" aria-label="窄屏主导航"><a v-for="item in NAV_ITEMS" :key="item.key" :href="item.hash" @click="menuOpen = false">{{ item.title }}</a><a href="#/m" @click="menuOpen = false">手机审批</a></nav>
    <nav class="side-navigation" :aria-label="workspaceTitle + '功能'"><a v-for="item in sections" :key="item.key" :href="item.hash" :aria-current="route.section === item.key ? 'page' : undefined" @click="menuOpen = false">{{ item.title }}</a></nav>
  </UiDialog>
</template>

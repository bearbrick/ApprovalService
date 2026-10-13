<script setup lang="ts">
import { computed } from 'vue'
import type { AppRoute } from '../model/routes.ts'
import { ADMIN_TABS, DESIGNER_TABS, SECTION_ITEMS, TAB_TITLES } from '../model/navigation.ts'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiIcon from '../components/ui/UiIcon.vue'

/**
 * 工作区页面的路由元数据。
 */
const props = defineProps<{
  /**
   * 由公共路由解析出的页面。
   */
  route: AppRoute
}>()
/**
 * 页面级服务连接入口。
 */
defineEmits<{
  /**
   * 打开服务状态对话框。
   */
  service: []
}>()
/**
 * 当前工作区的页签。
 */
const tabs = computed(() => {
  if (props.route.page === 'center' && !props.route.resourceId) return SECTION_ITEMS.center
  const keys = props.route.page === 'admin' ? ADMIN_TABS[props.route.section] ?? [] : props.route.page === 'designer' && props.route.resourceId ? DESIGNER_TABS : []
  const prefix = props.route.page === 'designer' ? `#/designer/${encodeURIComponent(props.route.resourceId)}` : `#/admin/${props.route.section}`
  return keys.map(key => ({ key, title: TAB_TITLES[key], hash: `${prefix}/${key}` }))
})
</script>

<template>
  <header class="page-header">
    <div class="breadcrumb"><a :href="route.mobile ? '#/m' : '#/'">审批服务</a><span>/</span><span>{{ route.title }}</span></div>
    <div class="page-title-row"><h1 id="page-title" tabindex="-1">{{ route.title }}</h1><span v-if="route.page !== 'not-found'" class="badge">功能待建设</span></div>
    <p>{{ route.description }}</p>
  </header>
  <div class="page-body">
    <nav v-if="tabs.length" class="page-tabs" aria-label="页面导航"><a v-for="tab in tabs" :key="tab.key" :href="tab.hash" :aria-current="(route.tab || route.section) === tab.key ? 'page' : undefined">{{ tab.title }}</a></nav>
    <section class="panel">
      <div class="panel-heading"><h2>{{ route.page === 'not-found' ? '检查页面地址' : route.resourceId ? '工作区已就绪' : route.title }}</h2><span v-if="route.resourceId" class="resource-id">{{ route.resourceId }}</span></div>
      <UiEmptyState v-if="route.page === 'not-found'" title="没有找到这个页面" description="请检查链接，或返回工作区重新选择。" icon="search"><template #action><a class="btn btn-primary" :href="route.mobile ? '#/m' : '#/'"><UiIcon name="arrow" />返回工作区</a></template></UiEmptyState>
      <UiEmptyState v-else title="功能尚未接入" description="页面框架已就绪，业务功能将在后续阶段接入。当前页面尚未查询业务数据。" :icon="route.mobile ? 'mobile' : route.page"><template #action><button class="btn" @click="$emit('service')"><UiIcon name="info" />查看服务连接</button></template></UiEmptyState>
    </section>
    <p class="workspace-note">业务单据由业务系统创建与维护，审批服务负责流程流转和审批记录。</p>
  </div>
</template>

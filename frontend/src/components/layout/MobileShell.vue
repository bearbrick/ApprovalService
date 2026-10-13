<script setup lang="ts">
import type { AppRoute } from '../../model/routes.ts'
import { SECTION_ITEMS } from '../../model/navigation.ts'
import UiIcon from '../ui/UiIcon.vue'

/**
 * 手机独立外壳的当前路由。
 */
defineProps<{
  /**
   * 当前手机路由。
   */
  route: AppRoute
}>()
/**
 * 服务状态入口。
 */
defineEmits<{
  /**
   * 打开真实服务连接状态。
   */
  service: []
}>()
</script>

<template>
  <div class="mobile-shell">
    <header class="mobile-header"><a v-if="route.resourceId" href="#/m" class="icon-btn" aria-label="返回待办"><UiIcon name="arrow" /></a><strong>审批服务</strong><button class="icon-btn" aria-label="查看服务连接状态" @click="$emit('service')"><UiIcon name="info" /></button><a href="#/">电脑版</a></header>
    <main id="workspace-content" class="mobile-workspace" tabindex="-1"><slot /></main>
    <nav class="mobile-navigation" aria-label="手机导航">
      <a v-for="(item, index) in SECTION_ITEMS.mobile" :key="item.key" :href="item.hash" :aria-current="route.section === item.key ? 'page' : undefined"><UiIcon :name="index === 0 ? 'center' : index === 1 ? 'check' : index === 2 ? 'info' : 'admin'" /><span>{{ item.title }}</span></a>
    </nav>
  </div>
</template>

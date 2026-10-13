<script setup lang="ts">
/**
 * 应用外壳：按地址的 # 部分切换页面；电脑端有顶栏（导航和服务连接状态），手机端（#/m）用自己的布局。
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getServiceInfo } from './api/http.ts'
import { NAV_ITEMS, parseRoute } from './model/routes.ts'
import AdminPage from './pages/AdminPage.vue'
import CenterPage from './pages/CenterPage.vue'
import DesignerPage from './pages/DesignerPage.vue'
import MobilePage from './pages/MobilePage.vue'

/**
 * 当前地址的 # 部分。
 */
const hash = ref(location.hash)

/**
 * 当前页面。
 */
const page = computed(() => parseRoute(hash.value))

/**
 * 顶栏右侧的连接状态文字。
 */
const status = ref('正在连接服务…')

/**
 * 是否连上了服务。
 */
const connected = ref(false)

/**
 * 地址的 # 部分变化时切换页面。
 */
function onHashChange(): void {
  hash.value = location.hash
}

onMounted(async () => {
  window.addEventListener('hashchange', onHashChange)
  try {
    const info = await getServiceInfo()
    status.value = `服务 ${info.version} · 接口 ${info.apiVersion}`
    connected.value = true
  } catch (error) {
    status.value = '连不上服务：' + (error instanceof Error ? error.message : String(error))
  }
})

onBeforeUnmount(() => window.removeEventListener('hashchange', onHashChange))
</script>

<template>
  <MobilePage v-if="page === 'mobile'" />
  <template v-else>
    <header class="topbar">
      <span class="brand">审批流服务</span>
      <nav>
        <a v-for="item in NAV_ITEMS" :key="item.key" :href="item.hash" :class="{ active: page === item.key }">{{ item.title }}</a>
      </nav>
      <span class="status" :class="connected ? 'ok' : 'bad'">{{ status }}</span>
    </header>
    <main class="page">
      <DesignerPage v-if="page === 'designer'" />
      <AdminPage v-else-if="page === 'admin'" />
      <CenterPage v-else />
    </main>
  </template>
</template>

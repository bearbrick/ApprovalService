<script setup lang="ts">
import { computed, ref } from 'vue'
import DesktopShell from './components/layout/DesktopShell.vue'
import MobileShell from './components/layout/MobileShell.vue'
import UiDialog from './components/ui/UiDialog.vue'
import UiErrorBoundary from './components/ui/UiErrorBoundary.vue'
import UiIcon from './components/ui/UiIcon.vue'
import WorkspacePage from './pages/WorkspacePage.vue'
import DesignerPage from './pages/DesignerPage.vue'
import CenterPage from './pages/CenterPage.vue'
import { useRouteNavigation } from './composables/useRouteNavigation.ts'
import { useServiceConnection } from './composables/useServiceConnection.ts'

/**
 * 公共路由和浏览器历史的单一状态来源。
 */
const route = useRouteNavigation()
/**
 * 服务连接独立于业务页面，不因导航重复请求。
 */
const { state, info, error, refresh } = useServiceConnection()
/**
 * 服务信息对话框是否打开。
 */
const serviceOpen = ref(false)
/**
 * 简短的连接状态，不推断任何业务模块是否就绪。
 */
const serviceLabel = computed(() => state.value === 'loading' ? '正在连接' : state.value === 'connected' ? '服务已连接' : '连接失败')
</script>

<template>
  <component :is="route.mobile ? MobileShell : DesktopShell" :route="route" :service-label="serviceLabel" @service="serviceOpen = true">
    <UiErrorBoundary :key="route.page === 'designer' ? `designer:${route.resourceId}` : route.hash"><DesignerPage v-if="route.page === 'designer'" :route="route" /><CenterPage v-else-if="route.page === 'center'" :route="route" /><WorkspacePage v-else :route="route" @service="serviceOpen = true" /></UiErrorBoundary>
  </component>
  <UiDialog :open="serviceOpen" title="服务连接" @close="serviceOpen = false">
    <div class="connection-status" :class="state" role="status" aria-live="polite"><UiIcon :name="state === 'connected' ? 'check' : state === 'error' ? 'alert' : 'refresh'" /><strong>{{ serviceLabel }}</strong></div>
    <p v-if="state === 'loading'" class="connection-description">正在读取服务信息，请稍候。</p>
    <template v-else-if="info">
      <p class="connection-description">已读取服务信息。业务模块的可用状态将在后续接入时单独检查。</p>
      <dl class="service-details"><dt>服务名称</dt><dd>{{ info.name }}</dd><dt>程序版本</dt><dd>{{ info.version }}</dd><dt>接口版本</dt><dd>{{ info.apiVersion }}</dd></dl>
    </template>
    <div v-else-if="error" class="connection-error" role="alert"><p>{{ error.message }}</p><dl class="service-details"><dt>错误标记</dt><dd>{{ error.code }}</dd><template v-if="error.traceId"><dt>链路编号</dt><dd>{{ error.traceId }}</dd></template></dl><p class="muted">请确认审批服务已启动且网络可用，然后重新连接。</p></div>
    <template #footer><button class="btn" @click="serviceOpen = false">关闭</button><button class="btn btn-primary" :disabled="state === 'loading'" @click="refresh"><UiIcon name="refresh" />{{ state === 'loading' ? '连接中…' : '重新连接' }}</button></template>
  </UiDialog>
</template>

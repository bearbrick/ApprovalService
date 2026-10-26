<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { loadCenterPreferences, notificationCategories, saveCenterPreference } from '../../api/center-settings.ts'
import type { CenterNotificationPreference } from '../../api/center-settings.ts'
import { ApiError } from '../../api/http-types.ts'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import CenterCommonOpinions from './CenterCommonOpinions.vue'
import SignatureSettings from './SignatureSettings.vue'
import CenterDelegations from './CenterDelegations.vue'

/**
 * 当前人员会话失效时由父页面退出登录。
 */
const emit = defineEmits<{
  /**
   * 已验证身份失效。
   */
  unauthorized: []
}>()

/**
 * 当前页面的提醒设置状态。
 */
const state = ref<'loading' | 'ready' | 'error'>('loading')
/**
 * 服务端返回的本人持久设置。
 */
const preferences = ref<CenterNotificationPreference[]>([])
/**
 * 正在提交的类别，提交期间禁止重复切换。
 */
const busyCategory = ref('')
/**
 * 加载或保存失败的说明。
 */
const error = ref('')
/**
 * 仅在服务端确认后显示的保存提示。
 */
const saved = ref('')
/**
 * 页面内请求取消信号，切换路由后不写入已卸载页面。
 */
let request: AbortController | null = null

/**
 * 每次进入页面重新读取服务端保存值。
 * @returns 设置读取完成。
 */
async function reload(): Promise<void> {
  request?.abort()
  const current = new AbortController()
  request = current
  state.value = 'loading'
  error.value = ''
  saved.value = ''
  try {
    const result = await loadCenterPreferences(current.signal)
    if (request !== current || current.signal.aborted) return
    preferences.value = result
    state.value = 'ready'
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '提醒设置读取失败。'
    state.value = 'error'
  }
}

/**
 * 只在服务器确认后替换当前类别，失败时保持原勾选值。
 * @param category 提醒类别编码。
 * @param channel 邮件或站内渠道。
 * @param enabled 用户选中的值。
 * @param input 当前复选框，提交前恢复已持久化的选中状态。
 * @returns 设置提交完成。
 */
async function change(category: string, channel: 'emailEnabled' | 'inAppEnabled', enabled: boolean,
  input: HTMLInputElement): Promise<void> {
  if (busyCategory.value || state.value !== 'ready') return
  const previous = preferences.value.find(item => item.category === category)
  if (!previous || (category === 'dailyDigest' && channel === 'inAppEnabled')) return
  // 原生复选框会先切换 DOM；在服务器确认前恢复旧值，避免失败时误显示已保存。
  input.checked = previous[channel]
  busyCategory.value = category
  error.value = ''
  saved.value = ''
  const current = new AbortController()
  request = current
  try {
    const next = await saveCenterPreference({ ...previous, [channel]: enabled }, current.signal)
    if (request !== current || current.signal.aborted) return
    preferences.value = preferences.value.map(item => item.category === category ? next : item)
    saved.value = '提醒设置已保存。'
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '提醒设置保存失败。'
  } finally {
    if (request === current) busyCategory.value = ''
  }
}

onMounted(() => { void reload() })
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <section class="center-card center-settings">
    <header><h2>提醒偏好</h2><p>按提醒类别选择邮件和站内消息。设置由审批服务保存，下次打开仍会生效。</p></header>
    <UiEmptyState v-if="state === 'loading'" title="正在读取提醒设置" description="请稍候。" icon="refresh" />
    <div v-else-if="state === 'error'" class="center-settings-feedback"><p role="alert">{{ error }}</p><button class="btn btn-primary" type="button" @click="reload">重试</button></div>
    <template v-else>
      <div class="center-settings-table" role="group" aria-label="提醒类别渠道设置">
        <div class="center-settings-heading"><strong>提醒类别</strong><strong>邮件</strong><strong>站内消息</strong></div>
        <div v-for="(category, index) in notificationCategories" :key="category.code" class="center-settings-row">
          <span>{{ category.label }}</span>
          <label><input type="checkbox" :checked="preferences[index]?.emailEnabled ?? false" :disabled="Boolean(busyCategory)" :aria-label="`${category.label}邮件`" @change="change(category.code, 'emailEnabled', ($event.target as HTMLInputElement).checked, $event.target as HTMLInputElement)"></label>
          <label v-if="category.code !== 'dailyDigest'"><input type="checkbox" :checked="preferences[index]?.inAppEnabled ?? false" :disabled="Boolean(busyCategory)" :aria-label="`${category.label}站内消息`" @change="change(category.code, 'inAppEnabled', ($event.target as HTMLInputElement).checked, $event.target as HTMLInputElement)"></label>
          <span v-else class="center-settings-muted">仅邮件</span>
        </div>
      </div>
      <p v-if="error" role="alert" class="center-login-error">{{ error }}</p><p v-if="saved" role="status" class="center-settings-saved">{{ saved }}</p>
    </template>
  </section>
  <CenterCommonOpinions @unauthorized="emit('unauthorized')" />
  <SignatureSettings />
  <CenterDelegations @unauthorized="emit('unauthorized')" />
</template>

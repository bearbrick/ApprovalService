<script setup lang="ts">
import { ref } from 'vue'
import { retryAdminCallback } from '../../api/admin-adapter.ts'

/**
 * 可供快速填写的最近回调事件 ID。
 */
const props = defineProps<{
  /**
   * 当前队列页中的回调事件 ID。
   */
  eventIds: readonly string[]
}>()

/**
 * 页面保存或关闭通知。
 */
const emit = defineEmits<{
  /**
   * 已重排原回调事件。
   */
  saved: []
  /**
   * 关闭编辑面板。
   */
  close: []
}>()

/**
 * 需要原地重排的事件 ID。
 */
const eventId = ref(props.eventIds[0] ?? '')
/**
 * 管理员填写的重发原因。
 */
const reason = ref('')
/**
 * 当前提交状态。
 */
const busy = ref(false)
/**
 * 保存后的反馈。
 */
const message = ref('')

/**
 * 使用现有事件身份请求后端事务内重排并审计。
 * @returns 提交完成。
 */
async function save(): Promise<void> {
  if (busy.value || !eventId.value.trim() || reason.value.trim().length < 2) return
  busy.value = true
  message.value = ''
  try {
    await retryAdminCallback(eventId.value.trim(), reason.value.trim())
    message.value = '回调已按原事件 ID 重排，稍后由待发任务发送。'
    reason.value = ''
    emit('saved')
  } catch (error) {
    message.value = error instanceof Error ? error.message : '重发回调失败。'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="admin-panel admin-retry-panel" aria-label="手动重发回调">
    <div class="admin-panel-head"><div><h2>手动重发回调</h2><p>保留原事件 ID 和正文，接入应用应按事件 ID 去重。</p></div><button class="btn" type="button" @click="emit('close')">关闭</button></div>
    <form class="admin-retry-content" @submit.prevent="save"><label>回调事件 ID<input v-model="eventId" list="admin-callback-events" required maxlength="128" :disabled="busy" placeholder="输入或选择原事件 ID"><datalist id="admin-callback-events"><option v-for="id in eventIds" :key="id" :value="id" /></datalist></label><label>重发原因<input v-model="reason" required minlength="2" maxlength="200" :disabled="busy" placeholder="说明重新投递的原因"></label><div class="admin-import-actions"><button class="btn btn-primary" type="submit" :disabled="busy || reason.trim().length < 2 || !eventId.trim()">重新投递</button></div><p v-if="message" role="status">{{ message }}</p></form>
  </section>
</template>

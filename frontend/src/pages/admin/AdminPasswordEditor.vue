<script setup lang="ts">
import { computed, ref } from 'vue'
import { adminSession, changeAdminPassword } from '../../api/admin-session.ts'
import type { AdminSession } from '../../api/admin-session.ts'

/**
 * 修改密码后交回新登录态。
 */
const emit = defineEmits<{
  /**
   * 旧令牌已失效，父页应换用新会话。
   */
  changed: [session: AdminSession]
  /**
   * 密码已改变但重新登录失败，旧会话不可再使用。
   */
  lost: []
}>()

/**
 * 当前管理员密码。
 */
const currentPassword = ref('')
/**
 * 新密码。
 */
const newPassword = ref('')
/**
 * 再次输入的新密码。
 */
const confirmation = ref('')
/**
 * 改密与重新登录请求正在进行。
 */
const busy = ref(false)
/**
 * 操作结果或错误说明。
 */
const message = ref('')
/**
 * 当前表单是否允许提交。
 */
const canSubmit = computed(() => !busy.value && currentPassword.value.length > 0
  && newPassword.value.length >= 10 && newPassword.value.length <= 128
  && confirmation.value === newPassword.value)

/**
 * 修改自己的密码并使用新密码重新建立管理员会话。
 * @returns 操作完成。
 */
async function save(): Promise<void> {
  if (!canSubmit.value) return
  busy.value = true
  message.value = ''
  try {
    const session = await changeAdminPassword(currentPassword.value, newPassword.value)
    currentPassword.value = ''
    newPassword.value = ''
    confirmation.value = ''
    message.value = '密码已修改，旧访问令牌和刷新令牌已失效。'
    emit('changed', session)
  } catch (error) {
    message.value = error instanceof Error ? error.message : '修改密码失败。'
    if (!adminSession()) emit('lost')
  } finally {
    // 无论成功与否，表单不继续保留旧密码。
    currentPassword.value = ''
    busy.value = false
  }
}
</script>

<template>
  <section class="admin-panel admin-password-panel" aria-label="修改当前管理员密码">
    <div class="admin-panel-head"><div><h2>修改我的密码</h2><p>提交后旧登录会话立即失效，本页会使用新密码重新登录。</p></div></div>
    <form class="admin-password-content" @submit.prevent="save">
      <label>当前密码<input v-model="currentPassword" type="password" autocomplete="current-password" required :disabled="busy"></label>
      <label>新密码<input v-model="newPassword" type="password" autocomplete="new-password" required minlength="10" maxlength="128" :disabled="busy" placeholder="10–128 位"></label>
      <label>确认新密码<input v-model="confirmation" type="password" autocomplete="new-password" required :disabled="busy"></label>
      <p v-if="confirmation && confirmation !== newPassword" class="admin-password-error" role="alert">两次输入的新密码不一致。</p>
      <div class="admin-import-actions"><button class="btn btn-primary" type="submit" :disabled="!canSubmit">{{ busy ? '正在修改…' : '修改密码' }}</button></div>
      <p v-if="message" role="status">{{ message }}</p>
    </form>
  </section>
</template>

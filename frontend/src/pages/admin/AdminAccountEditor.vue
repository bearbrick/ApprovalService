<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { loadAdminAccountPeople, provisionAdminAccount, revokeAdminSessions } from '../../api/admin-adapter.ts'
import type { AdminPerson } from '../../model/admin-data.ts'

/**
 * 账号管理保存及关闭通知。
 */
const emit = defineEmits<{
  /**
   * 账号操作成功，父页应刷新目录。
   */
  saved: []
  /**
   * 关闭账号管理面板。
   */
  close: []
}>()

/**
 * 管理员有权查看的人员摘要。
 */
const people = ref<AdminPerson[]>([])
/**
 * 当前操作：开通账号或作废全部现有会话。
 */
const mode = ref<'provision' | 'revoke'>('provision')
/**
 * 当前选中的内部人员 ID。
 */
const personId = ref('')
/**
 * 临时密码只保存在当前面板内存中。
 */
const temporaryPassword = ref('')
/**
 * 作废会话前的显式确认。
 */
const revokeConfirmed = ref(false)
/**
 * 请求进行中状态。
 */
const busy = ref(false)
/**
 * 操作结果或错误说明。
 */
const message = ref('')
/**
 * 卸载后忽略迟到的人员目录响应。
 */
let active = true

/**
 * 当前操作可选择的目标人员。
 */
const options = computed(() => mode.value === 'provision'
  ? people.value.filter(person => person.isActive) : people.value)
/**
 * 当前选中的人员安全摘要。
 */
const selected = computed(() => people.value.find(person => person.id === personId.value))
/**
 * 所需输入完整且无请求正在进行。
 */
const canSubmit = computed(() => !busy.value && !!selected.value
  && (mode.value === 'provision'
    ? selected.value.isActive && temporaryPassword.value.length >= 10 && temporaryPassword.value.length <= 128
    : revokeConfirmed.value))

/**
 * 从组织管理员接口读取可操作的目标。
 * @returns 加载完成。
 */
async function load(): Promise<void> {
  busy.value = true
  message.value = ''
  try {
    const result = await loadAdminAccountPeople()
    if (!active) return
    people.value = result
    personId.value = result.find(person => person.isActive)?.id ?? ''
  } catch (error) {
    if (active) message.value = error instanceof Error ? error.message : '读取组织人员失败。'
  } finally {
    if (active) busy.value = false
  }
}

/**
 * 切换操作时清除密码、确认和旧反馈。
 * @param next 要执行的账号操作。
 */
function selectMode(next: 'provision' | 'revoke'): void {
  mode.value = next
  temporaryPassword.value = ''
  revokeConfirmed.value = false
  message.value = ''
  if (!options.value.some(person => person.id === personId.value)) personId.value = options.value[0]?.id ?? ''
}

/**
 * 提交到已授权的账号接口，并立即清除临时密码。
 * @returns 操作完成。
 */
async function save(): Promise<void> {
  if (!canSubmit.value) return
  busy.value = true
  message.value = ''
  try {
    if (mode.value === 'provision') {
      await provisionAdminAccount(personId.value, temporaryPassword.value)
      message.value = '账号已开通。请通过安全渠道告知本人临时密码；首次登录必须改密。'
    } else {
      await revokeAdminSessions(personId.value)
      message.value = '该人员的现有访问与刷新令牌已作废，可重新使用原密码登录。'
      revokeConfirmed.value = false
    }
    emit('saved')
  } catch (error) {
    message.value = error instanceof Error ? error.message : '账号操作失败。'
  } finally {
    temporaryPassword.value = ''
    busy.value = false
  }
}

onBeforeUnmount(() => { active = false; temporaryPassword.value = '' })
void load()
</script>

<template>
  <section class="admin-panel admin-account-panel" aria-label="人员账号管理">
    <div class="admin-panel-head"><div><h2>人员账号管理</h2><p>只对当前组织目录中的人员操作；权限由服务端再次核对。</p></div><button class="btn" type="button" @click="emit('close')">关闭</button></div>
    <form class="admin-account-content" @submit.prevent="save">
      <label>操作<select :value="mode" :disabled="busy" @change="selectMode(($event.target as HTMLSelectElement).value as 'provision' | 'revoke')"><option value="provision">开通本地账号</option><option value="revoke">作废全部现有会话</option></select></label>
      <label>人员<select v-model="personId" required :disabled="busy"><option value="" disabled>请选择人员</option><option v-for="person in options" :key="person.id" :value="person.id">{{ person.name }}（{{ person.employeeNumber }}）</option></select></label>
      <template v-if="mode === 'provision'"><p class="admin-account-note">仅能为在职且尚未开通账号的人员设置临时密码。密码不会在管理列表中显示。</p><label>临时密码<input v-model="temporaryPassword" type="password" autocomplete="new-password" required minlength="10" maxlength="128" :disabled="busy" placeholder="10–128 位，首次登录后必须修改"></label></template>
      <template v-else><p class="admin-account-note">立即让该人员现有访问令牌和刷新令牌失效；账号与密码保留，之后仍可重新登录。</p><label class="admin-account-confirm"><input v-model="revokeConfirmed" type="checkbox" :disabled="busy">确认作废 {{ selected?.name ?? '所选人员' }} 的全部现有会话</label></template>
      <div class="admin-import-actions"><button class="btn btn-primary" type="submit" :disabled="!canSubmit">{{ mode === 'provision' ? '开通账号' : '确认作废会话' }}</button></div>
      <p v-if="message" role="status">{{ message }}</p>
    </form>
  </section>
</template>

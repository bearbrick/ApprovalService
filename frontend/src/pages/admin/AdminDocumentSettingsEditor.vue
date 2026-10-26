<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { getAdminDocumentSettings, saveAdminDocumentSettings } from '../../api/admin-document.ts'

/**
 * 管理列表中可配置的接入应用。
 */
interface AppOption {
  /**
   * 应用编码。
   */
  code: string
  /**
   * 显示名称。
   */
  name: string
}

/**
 * 管理页面传入的可选应用。
 */
const props = defineProps<{
  /**
   * 已授权可见的应用列表。
   */
  apps: readonly AppOption[]
}>()

/**
 * 关闭当前编辑面板。
 */
const emit = defineEmits<{
  /**
   * 用户关闭面板。
   */
  close: []
}>()

/**
 * 当前应用编码。
 */
const code = ref(props.apps[0]?.code ?? '')
/**
 * 完整签名钩子地址。
 */
const documentUrl = ref('')
/**
 * 每行一个原单 HTTPS 来源。
 */
const originsText = ref('')
/**
 * 必填的变更原因。
 */
const reason = ref('')
/**
 * 保存或读取中的状态。
 */
const busy = ref(false)
/**
 * 页面反馈。
 */
const message = ref('')
/**
 * 取消旧请求的逻辑版本。
 */
let revision = 0

/**
 * 读取当前应用配置，旧响应不得覆盖新选择。
 * @returns 读取完成。
 */
async function load(): Promise<void> {
  const current = ++revision
  documentUrl.value = ''
  originsText.value = ''
  reason.value = ''
  message.value = ''
  if (!code.value) return
  busy.value = true
  try {
    const result = await getAdminDocumentSettings(code.value)
    if (current !== revision) return
    documentUrl.value = result.documentUrl ?? ''
    originsText.value = result.allowedDocumentOrigins.join('\n')
  } catch (error) {
    if (current === revision) message.value = error instanceof Error ? error.message : '读取原单配置失败。'
  } finally {
    if (current === revision) busy.value = false
  }
}

/**
 * 保存完整原单配置，服务端再校验 HTTPS 与审计原因。
 * @returns 保存完成。
 */
async function save(): Promise<void> {
  if (!code.value || busy.value) return
  const current = ++revision
  busy.value = true
  message.value = ''
  try {
    const origins = originsText.value.split(/\r?\n/).map(value => value.trim()).filter(Boolean)
    const result = await saveAdminDocumentSettings(code.value, documentUrl.value.trim() || null,
      origins, reason.value.trim())
    if (current !== revision) return
    documentUrl.value = result.documentUrl ?? ''
    originsText.value = result.allowedDocumentOrigins.join('\n')
    reason.value = ''
    message.value = '业务原单设置已保存。'
  } catch (error) {
    if (current === revision) message.value = error instanceof Error ? error.message : '保存原单配置失败。'
  } finally {
    if (current === revision) busy.value = false
  }
}

watch(code, () => { void load() }, { immediate: true })
watch(() => props.apps, apps => {
  if (!apps.some(app => app.code === code.value)) code.value = apps[0]?.code ?? ''
})
onBeforeUnmount(() => { revision++ })
</script>

<template>
  <section class="admin-panel admin-origins-panel" aria-label="业务原单设置">
    <div class="admin-panel-head"><div><h2>业务原单嵌入</h2><p>为当前查看人向业务系统申请一次性只读页面；失败时显示审批数据。</p></div><button class="btn" type="button" @click="emit('close')">关闭</button></div>
    <form class="admin-origins-content" @submit.prevent="save">
      <label>接入应用<select v-model="code" :disabled="busy"><option v-for="app in apps" :key="app.code" :value="app.code">{{ app.name }}（{{ app.code }}）</option></select></label>
      <label>原单签名钩子<input v-model="documentUrl" :disabled="busy" maxlength="2048" placeholder="https://erp.example.com/api/approval/document-url"></label>
      <label>允许的原单页面来源<textarea v-model="originsText" rows="5" :disabled="busy" placeholder="https://erp.example.com"></textarea></label>
      <p class="admin-origins-hint">页面来源仅允许完整 HTTPS origin，最多 20 项。钩子可在部署时显式开启内网 HTTP，返回页面始终必须是 HTTPS。</p>
      <label>变更原因<input v-model="reason" :disabled="busy" required minlength="2" maxlength="200" placeholder="说明本次配置用途"></label>
      <div class="admin-import-actions"><button class="btn" type="button" :disabled="busy || !code" @click="load">重新读取</button><button class="btn btn-primary" type="submit" :disabled="busy || !code || reason.trim().length < 2">保存设置</button></div>
      <p v-if="message" role="status">{{ message }}</p>
    </form>
  </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { getAdminEmbedOrigins, saveAdminEmbedOrigins } from '../../api/admin-adapter.ts'

/**
 * 管理列表中可配置的接入应用。
 */
interface AppOption {
  /**
   * 应用编码。
   */
  code: string
  /**
   * 应用显示名称。
   */
  name: string
}

/**
 * 当前已授权可见的接入应用。
 */
const props = defineProps<{
  /**
   * 可选择的应用摘要。
   */
  apps: readonly AppOption[]
}>()

/**
 * 关闭来源编辑面板。
 */
const emit = defineEmits<{
  /**
   * 关闭编辑面板。
   */
  close: []
}>()

/**
 * 当前编辑的应用编码。
 */
const code = ref(props.apps[0]?.code ?? '')
/**
 * 每行一个来源的编辑文本。
 */
const originsText = ref('')
/**
 * 本次变更原因。
 */
const reason = ref('')
/**
 * 请求是否进行中。
 */
const busy = ref(false)
/**
 * 页面反馈。
 */
const message = ref('')
/**
 * 当前读取请求的序号，防止旧响应覆盖新应用。
 */
let revision = 0

/**
 * 读取已选择应用的服务器设置。
 * @returns 读取操作完成。
 */
async function load(): Promise<void> {
  const currentCode = code.value
  const currentRevision = ++revision
  originsText.value = ''
  reason.value = ''
  message.value = ''
  if (!currentCode) return
  busy.value = true
  try {
    const result = await getAdminEmbedOrigins(currentCode)
    if (currentRevision !== revision) return
    originsText.value = result.origins.join('\n')
  } catch (error) {
    if (currentRevision === revision) message.value = error instanceof Error ? error.message : '读取允许嵌入来源失败。'
  } finally {
    if (currentRevision === revision) busy.value = false
  }
}

/**
 * 保存所有来源，具体地址合法性由服务端再次校验。
 * @returns 保存操作完成。
 */
async function save(): Promise<void> {
  if (!code.value || busy.value) return
  const currentRevision = ++revision
  busy.value = true
  message.value = ''
  try {
    const origins = originsText.value.split(/\r?\n/).map(value => value.trim()).filter(Boolean)
    const result = await saveAdminEmbedOrigins(code.value, origins, reason.value.trim())
    if (currentRevision !== revision) return
    originsText.value = result.origins.join('\n')
    reason.value = ''
    message.value = '允许嵌入来源已保存，新的页面响应会使用此配置。'
  } catch (error) {
    if (currentRevision === revision) message.value = error instanceof Error ? error.message : '保存来源失败。'
  } finally {
    if (currentRevision === revision) busy.value = false
  }
}

watch(code, () => { void load() }, { immediate: true })
watch(() => props.apps, apps => {
  if (!apps.some(app => app.code === code.value)) code.value = apps[0]?.code ?? ''
})
onBeforeUnmount(() => { revision++ })
</script>

<template>
  <section class="admin-panel admin-origins-panel" aria-label="接入应用允许嵌入来源">
    <div class="admin-panel-head"><div><h2>允许嵌入的网站</h2><p>只有登记的网站可以把审批中心嵌入页面；来源按一行一个完整地址填写。</p></div><button class="btn" type="button" @click="emit('close')">关闭</button></div>
    <form class="admin-origins-content" @submit.prevent="save"><label>接入应用<select v-model="code" :disabled="busy"><option v-for="app in apps" :key="app.code" :value="app.code">{{ app.name }}（{{ app.code }}）</option></select></label><label>允许来源<textarea v-model="originsText" rows="5" :disabled="busy" placeholder="https://erp.example.com"></textarea></label><p class="admin-origins-hint">仅允许完整 HTTPS 来源，不填写路径、查询参数或通配符；本机开发可使用 HTTP localhost。最多 20 项。</p><label>变更原因<input v-model="reason" :disabled="busy" required minlength="2" maxlength="200" placeholder="说明为何新增或移除这些网站"></label><div class="admin-import-actions"><button class="btn" type="button" :disabled="busy || !code" @click="load">重新读取</button><button class="btn btn-primary" type="submit" :disabled="busy || !code || reason.trim().length < 2">保存设置</button></div><p v-if="message" role="status">{{ message }}</p></form>
  </section>
</template>

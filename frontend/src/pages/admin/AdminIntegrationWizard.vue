<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { buildCSharpStartSnippet } from '../../designer/workbench/integration-snippet.ts'
import { getAppAdministrators, getWizardApps, queueTestCallback, saveAppAdministrators } from '../../api/admin-integration-wizard.ts'
import type { WizardApp } from '../../api/admin-integration-wizard.ts'
import AdminClientAppEditor from './AdminClientAppEditor.vue'

/**
 * 完整向导或应用管理员的受限测试入口。
 */
const props = defineProps<{
  /**
   * 仅测试模式不读取应用列表、名单或组织待发队列。
   */
  restricted: boolean
}>()

/**
 * 页面交给父级刷新应用列表或关闭。
 */
const emit = defineEmits<{
  /**
   * 应用已创建，父页重新读取列表。
   */
  saved: []
  /**
   * 关闭向导。
   */
  close: []
}>()

/**
 * 已选应用或应用管理员自己输入的已知编码。
 */
const code = ref('')
/**
 * 组织管理员重新读取的原始应用摘要。
 */
const apps = ref<WizardApp[]>([])
/**
 * 应用列表读取状态与错误。
 */
const appsMessage = ref('')
/**
 * 展开已有的接入应用创建表单。
 */
const creating = ref(false)
/**
 * 旧管理员名单，用于并发条件更新。
 */
const expectedIds = ref<string[]>([])
/**
 * 管理员编辑的内部人员 ID 文本。
 */
const idsText = ref('')
/**
 * 名单变更原因。
 */
const adminReason = ref('')
/**
 * 名单读取或写入状态。
 */
const adminsBusy = ref(false)
/**
 * 名单的结果提示。
 */
const adminsMessage = ref('')
/**
 * 管理员名单是否已经从服务端读取。
 */
const adminsLoaded = ref(false)
/**
 * 已授权流程中选中的示例流程。
 */
const flowCode = ref('')
/**
 * 测试回调的可审计原因。
 */
const testReason = ref('')
/**
 * 发送测试中的状态。
 */
const testBusy = ref(false)
/**
 * 测试结果或错误。
 */
const testMessage = ref('')
/**
 * 当前测试产生的持久事件 ID。
 */
const eventId = ref('')

/**
 * 当前列表中的应用，受限模式不会得到该列表。
 */
const selectedApp = computed(() => apps.value.find(item => item.code === code.value))
/**
 * 仅从当前应用已授权流程中生成示例。
 */
const snippet = computed(() => flowCode.value && selectedApp.value?.flowCodes.includes(flowCode.value)
  ? buildCSharpStartSnippet(flowCode.value) : '')

/**
 * 解析管理员输入，保留服务端名单限制的最终裁定。
 * @param value 使用逗号、空格或换行分隔的内部 ID。
 * @returns 去空后的 ID 顺序列表。
 */
function parseIds(value: string): string[] {
  return value.split(/[，,\s]+/).map(item => item.trim()).filter(Boolean)
}

/**
 * 组织管理员读取应用摘要；应用管理员不会请求此接口。
 * @returns 应用列表刷新完成。
 */
async function loadApps(): Promise<void> {
  if (props.restricted) return
  appsMessage.value = ''
  try {
    apps.value = await getWizardApps()
    if (!apps.value.some(item => item.code === code.value)) code.value = apps.value[0]?.code ?? ''
  } catch (error) {
    apps.value = []
    appsMessage.value = error instanceof Error ? error.message : '无法读取接入应用列表。'
  }
}

/**
 * 应用创建后保留一次性凭据面板，同时刷新向导可选应用。
 * @returns 列表刷新完成。
 */
async function createdApp(): Promise<void> {
  emit('saved')
  await loadApps()
}

/**
 * 组织管理员读取当前应用的名单，失败时不展示旧应用名单。
 * @returns 名单读取完成。
 */
async function loadAdmins(): Promise<void> {
  const target = code.value
  if (props.restricted || !target) return
  adminsBusy.value = true
  adminsLoaded.value = false
  adminsMessage.value = ''
  expectedIds.value = []
  idsText.value = ''
  try {
    const result = await getAppAdministrators(target)
    if (code.value !== target) return
    expectedIds.value = result.personIds
    idsText.value = result.personIds.join('\n')
    adminsLoaded.value = true
  } catch (error) {
    if (code.value === target) adminsMessage.value = error instanceof Error ? error.message : '无法读取应用管理员名单。'
  } finally {
    if (code.value === target) adminsBusy.value = false
  }
}

/**
 * 使用旧名单条件保存当前应用的内部人员管理员。
 * @returns 保存完成。
 */
async function saveAdmins(): Promise<void> {
  if (adminsBusy.value || !adminsLoaded.value || adminReason.value.trim().length < 2) return
  const target = code.value
  const personIds = parseIds(idsText.value)
  if (new Set(personIds).size !== personIds.length) {
    adminsMessage.value = '内部人员 ID 不能重复。'
    return
  }
  adminsBusy.value = true
  adminsMessage.value = ''
  try {
    const result = await saveAppAdministrators(target, expectedIds.value, personIds, adminReason.value.trim())
    if (code.value !== target) return
    expectedIds.value = result.personIds
    idsText.value = result.personIds.join('\n')
    adminReason.value = ''
    adminsMessage.value = '应用管理员名单已保存并留痕。'
  } catch (error) {
    if (code.value === target) adminsMessage.value = error instanceof Error ? error.message : '保存名单失败，请刷新后重试。'
  } finally {
    if (code.value === target) adminsBusy.value = false
  }
}

/**
 * 排入独立的签名测试事件，实际送达由持久队列处理。
 * @returns 入队请求完成。
 */
async function sendTest(): Promise<void> {
  if (testBusy.value || !code.value.trim() || testReason.value.trim().length < 2) return
  const target = code.value.trim()
  testBusy.value = true
  testMessage.value = ''
  eventId.value = ''
  try {
    const result = await queueTestCallback(target, testReason.value.trim())
    if (code.value.trim() !== target) return
    eventId.value = result.eventId
    testReason.value = ''
    testMessage.value = '测试回调已排队。接收方应只验签确认，不更新业务单据。'
  } catch (error) {
    if (code.value.trim() === target) testMessage.value = error instanceof Error ? error.message : '测试回调入队失败。'
  } finally {
    testBusy.value = false
  }
}

watch(code, () => {
  eventId.value = ''
  testMessage.value = ''
  flowCode.value = selectedApp.value?.flowCodes[0] ?? ''
  void loadAdmins()
}, { immediate: true })
void loadApps()
</script>

<template>
  <section class="admin-panel admin-integration-wizard" aria-label="接入向导">
    <div class="admin-panel-head"><div><h2>{{ restricted ? '测试应用回调' : '接入向导' }}</h2><p>{{ restricted ? '填写你管理的应用编码和原因，发送时服务端会重新核对权限。' : '创建应用、保存凭据、配置管理员、选择已授权流程并测试回调。' }}</p></div><button v-if="!restricted" class="btn" type="button" @click="emit('close')">关闭</button></div>
    <div v-if="!restricted" class="admin-integration-step"><h3>1. 登记接入应用</h3><p>新应用在创建响应中只显示一次 API Key 和签名密钥。请先安全保存，再继续后面的步骤。</p><button class="btn" type="button" @click="creating = !creating">{{ creating ? '收起创建表单' : '新建应用' }}</button><AdminClientAppEditor v-if="creating" mode="create" :apps="apps" @saved="createdApp" @close="creating = false" /></div>
    <div class="admin-integration-step"><h3>{{ restricted ? '测试目标' : '2. 选择应用' }}</h3><label v-if="restricted">已知应用编码<input v-model="code" required maxlength="128" pattern="[A-Za-z0-9_-]+" autocomplete="off" :disabled="testBusy" placeholder="由组织管理员提供的应用编码"></label><label v-else>接入应用<select v-model="code" :disabled="adminsBusy || testBusy"><option v-for="app in apps" :key="app.code" :value="app.code">{{ app.name }}（{{ app.code }}）{{ app.isEnabled ? '' : '·已停用' }}</option></select></label><p v-if="!restricted && !apps.length">请先登记一个接入应用。</p><p v-if="appsMessage" role="alert">{{ appsMessage }}</p></div>
    <div v-if="!restricted && selectedApp" class="admin-integration-step"><h3>3. 应用管理员</h3><p>填写当前在职人员的内部 ID。名单仅由组织管理员维护；并发修改会要求刷新。</p><form @submit.prevent="saveAdmins"><label>内部人员 ID，每行一个<textarea v-model="idsText" rows="3" :disabled="adminsBusy || !adminsLoaded" autocomplete="off" /></label><label>变更原因<input v-model="adminReason" required minlength="2" maxlength="200" :disabled="adminsBusy || !adminsLoaded"></label><div class="admin-import-actions"><button class="btn btn-primary" type="submit" :disabled="adminsBusy || !adminsLoaded || adminReason.trim().length < 2">保存名单</button><button class="btn" type="button" :disabled="adminsBusy" @click="loadAdmins">刷新名单</button></div><p v-if="adminsMessage" role="status">{{ adminsMessage }}</p></form></div>
    <div v-if="!restricted && selectedApp" class="admin-integration-step"><h3>4. 生成 C# 调用代码</h3><label>已授权流程<select v-model="flowCode"><option value="">请选择流程</option><option v-for="item in selectedApp.flowCodes" :key="item" :value="item">{{ item }}</option></select></label><p v-if="!selectedApp.flowCodes.length">此应用尚未授权流程，需由组织管理员在登记应用时配置。</p><textarea v-if="snippet" :value="snippet" rows="12" readonly spellcheck="false" aria-label="C# SDK 调用代码" /><p v-if="snippet">示例从部署环境读取服务地址和 API Key；业务系统需保存自己的单据、绑定发起人员，并在发起前确认流程已发布。</p></div>
    <div class="admin-integration-step"><h3>{{ restricted ? '发送签名测试回调' : '5. 发送签名测试回调' }}</h3><p>应用必须启用并登记回调地址。测试使用合成事件，不创建审批单。</p><form @submit.prevent="sendTest"><label>测试原因<input v-model="testReason" required minlength="2" maxlength="200" :disabled="testBusy" placeholder="说明本次接入测试的原因"></label><button class="btn btn-primary" type="submit" :disabled="testBusy || !code.trim() || testReason.trim().length < 2">{{ testBusy ? '正在排队…' : '发送测试回调' }}</button><p v-if="testMessage" role="status">{{ testMessage }}</p><p v-if="eventId">事件 ID：<code>{{ eventId }}</code><template v-if="!restricted"> · <a href="#/admin/outbound/queue">查看待发队列状态</a></template></p></form></div>
  </section>
</template>

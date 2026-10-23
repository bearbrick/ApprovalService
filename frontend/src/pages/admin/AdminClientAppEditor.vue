<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { createAdminClientApp, rotateAdminClientAppSecrets, setAdminClientAppEnabled } from '../../api/admin-adapter.ts'
import type { AdminClientAppSecrets } from '../../api/admin-adapter.ts'

/**
 * 接入应用管理列表中的安全摘要。
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
  /**
   * 当前启用状态。
   */
  isEnabled: boolean
}

/**
 * 编辑模式及已授权列表中的应用选项。
 */
const props = defineProps<{
  /**
   * 新建、密钥轮换或启停状态。
   */
  mode: 'create' | 'rotate' | 'status'
  /**
   * 当前管理员可见的应用摘要。
   */
  apps: readonly AppOption[]
}>()

/**
 * 保存完成或关闭编辑器的事件。
 */
const emit = defineEmits<{
  /**
   * 应用或密钥状态已变更，父页应重新读取列表。
   */
  saved: []
  /**
   * 关闭编辑面板。
   */
  close: []
}>()

/**
 * 当前选中的已登记应用编码，或新应用编码。
 */
const code = ref(props.mode === 'create' ? '' : props.apps[0]?.code ?? '')
/**
 * 新应用显示名称。
 */
const name = ref('')
/**
 * 逗号或换行分隔的流程编码。
 */
const flowCodes = ref('')
/**
 * 是否允许跨应用查看待办统计。
 */
const canViewAllTasks = ref(false)
/**
 * 审批结果回调地址。
 */
const callbackUrl = ref('')
/**
 * 业务数据获取地址。
 */
const dataUrl = ref('')
/**
 * 业务规则校验地址。
 */
const validationUrl = ref('')
/**
 * 业务人员获取地址。
 */
const assigneeUrl = ref('')
/**
 * 业务动作执行地址。
 */
const actionUrl = ref('')
/**
 * 当前目标启用状态。
 */
const enabled = ref(false)
/**
 * 管理员填写的变更原因。
 */
const reason = ref('')
/**
 * 本次响应的一次性凭据；仅留在页面内存中。
 */
const secrets = ref<AdminClientAppSecrets | null>(null)
/**
 * 提交中的状态。
 */
const busy = ref(false)
/**
 * 保存结果或错误提示。
 */
const message = ref('')

/**
 * 当前应用的安全摘要。
 */
const selectedApp = computed(() => props.apps.find(app => app.code === code.value))
/**
 * 面板标题。
 */
const title = computed(() => ({ create: '新建接入应用', rotate: '轮换应用密钥', status: '启停接入应用' })[props.mode])

/**
 * 把可选地址规范成空值或完整输入。
 * @param value 管理员填写的地址。
 * @returns 空值或去空格后的地址。
 */
function optionalUrl(value: string): string | null { return value.trim() || null }

/**
 * 执行当前管理操作，密钥只在创建或轮换成功响应中展示。
 * @returns 保存完成。
 */
async function save(): Promise<void> {
  if (busy.value || reason.value.trim().length < 2) return
  busy.value = true
  message.value = ''
  secrets.value = null
  try {
    if (props.mode === 'create') {
      const allowedFlowCodes = flowCodes.value.split(/[，,\s]+/).map(value => value.trim()).filter(Boolean)
      secrets.value = await createAdminClientApp({
        code: code.value.trim(), name: name.value.trim(), allowedFlowCodes, canViewAllTasks: canViewAllTasks.value,
        callbackUrl: optionalUrl(callbackUrl.value), dataUrl: optionalUrl(dataUrl.value),
        validationUrl: optionalUrl(validationUrl.value), assigneeUrl: optionalUrl(assigneeUrl.value),
        actionUrl: optionalUrl(actionUrl.value), reason: reason.value.trim(),
      })
      message.value = '应用已创建。请立即保存下面的凭据，关闭后无法再次读取。'
    } else if (props.mode === 'rotate') {
      secrets.value = await rotateAdminClientAppSecrets(code.value, reason.value.trim())
      message.value = '密钥已轮换，旧 API Key 和嵌入会话已失效。请立即保存新凭据。'
    } else {
      await setAdminClientAppEnabled(code.value, enabled.value, reason.value.trim())
      message.value = enabled.value ? '应用已启用。' : '应用已停用。'
    }
    reason.value = ''
    emit('saved')
  } catch (error) {
    message.value = error instanceof Error ? error.message : '保存应用设置失败。'
  } finally {
    busy.value = false
  }
}

watch(selectedApp, app => { if (app) enabled.value = !app.isEnabled }, { immediate: true })
</script>

<template>
  <section class="admin-panel admin-app-editor" :aria-label="title">
    <div class="admin-panel-head"><div><h2>{{ title }}</h2><p v-if="mode === 'rotate'">轮换后旧 API Key 与嵌入会话立即失效，新密钥只显示一次。</p><p v-else-if="mode === 'status'">停用后开放接口和已有嵌入会话立即失效，重新启用不会恢复旧会话。</p><p v-else>由服务端生成 API Key 与签名密钥，登记时配置授权流程和回调地址。</p></div><button class="btn" type="button" @click="emit('close')">关闭</button></div>
    <form class="admin-app-editor-content" @submit.prevent="save"><template v-if="mode === 'create'"><label>应用编码<input v-model="code" required maxlength="128" pattern="[A-Za-z0-9_-]+" :disabled="busy || !!secrets" placeholder="如 erp-production"></label><label>应用名称<input v-model="name" required maxlength="256" :disabled="busy || !!secrets"></label><label>授权流程编码<input v-model="flowCodes" :disabled="busy || !!secrets" placeholder="按逗号或空格分隔"></label><label>结果回调 URL<input v-model="callbackUrl" type="url" :disabled="busy || !!secrets" placeholder="https://business.example/callback"></label><label>取数钩子 URL<input v-model="dataUrl" type="url" :disabled="busy || !!secrets"></label><label>校验钩子 URL<input v-model="validationUrl" type="url" :disabled="busy || !!secrets"></label><label>取人钩子 URL<input v-model="assigneeUrl" type="url" :disabled="busy || !!secrets"></label><label>自动动作 URL<input v-model="actionUrl" type="url" :disabled="busy || !!secrets"></label><label class="admin-app-editor-check"><input v-model="canViewAllTasks" type="checkbox" :disabled="busy || !!secrets">允许跨应用查看待办统计</label></template><template v-else><label>接入应用<select v-model="code" :disabled="busy || !!secrets" required><option v-for="app in apps" :key="app.code" :value="app.code">{{ app.name }}（{{ app.code }}）</option></select></label><label v-if="mode === 'status'">目标状态<select v-model="enabled" :disabled="busy"><option :value="true">启用</option><option :value="false">停用</option></select></label></template><label v-if="!secrets">变更原因<input v-model="reason" required minlength="2" maxlength="200" :disabled="busy" placeholder="说明本次管理操作的原因"></label><div v-if="!secrets" class="admin-import-actions"><button class="btn btn-primary" type="submit" :disabled="busy || reason.trim().length < 2 || !code.trim()">{{ mode === 'rotate' ? '确认轮换密钥' : '保存' }}</button></div><p v-if="message" role="status">{{ message }}</p><div v-if="secrets" class="admin-app-secrets"><strong>仅此一次显示 · {{ secrets.code }}</strong><label>API Key<input :value="secrets.apiKey" readonly autocomplete="off"></label><label>HMAC 签名密钥<input :value="secrets.signingSecret" readonly autocomplete="off"></label><p>请在关闭面板前安全保存；管理列表不会再次回显。</p></div></form>
  </section>
</template>

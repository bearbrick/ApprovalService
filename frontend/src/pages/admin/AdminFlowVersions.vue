<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ApiError } from '../../api/http.ts'
import { getManagedFlow, listManagedFlowVersions, restoreManagedFlowVersion } from '../../api/admin-flow-management.ts'
import type { ManagedFlow, ManagedFlowVersion } from '../../api/admin-flow-management.ts'
import { restorableVersions, validRestoreReason } from '../../model/flow-version.ts'

/**
 * 当前管理目录中选中的流程。
 */
const props = defineProps<{
  /**
   * 服务端授权的流程摘要。
   */
  flow: ManagedFlow
}>()

/**
 * 会话失效、恢复成功和面板关闭事件。
 */
const emit = defineEmits<{
  /**
   * 当前本地身份失效。
   */
  lost: []
  /**
   * 服务端已创建新的草稿修订。
   */
  restored: [flow: ManagedFlow]
  /**
   * 用户关闭历史面板。
   */
  close: []
}>()

/**
 * 打开面板时重新读取的管理详情，供恢复操作作双重并发前提。
 */
const detail = ref<ManagedFlow | null>(null)
/**
 * 已授权的不可变发布版本摘要。
 */
const versions = ref<ManagedFlowVersion[]>([])
/**
 * 可复制成新草稿的旧版本，当前发布版不会进入操作列表。
 */
const older = computed(() => restorableVersions(versions.value))
/**
 * 读取状态。
 */
const state = ref<'loading' | 'ready' | 'error'>('loading')
/**
 * 要复制的旧版本。
 */
const selected = ref<ManagedFlowVersion | null>(null)
/**
 * 本次恢复的审计原因。
 */
const reason = ref('')
/**
 * 读取或恢复失败的中文说明。
 */
const error = ref('')
/**
 * 恢复写入期间禁止重复提交。
 */
const saving = ref(false)
/**
 * 当前读取请求，切换流程或关闭面板时取消。
 */
let request: AbortController | null = null

/**
 * 读取最新草稿修订、当前发布指针及版本摘要。
 * @returns 当前流程历史读取完成。
 */
async function load(): Promise<void> {
  request?.abort()
  const current = new AbortController()
  request = current
  state.value = 'loading'
  detail.value = null
  versions.value = []
  selected.value = null
  reason.value = ''
  error.value = ''
  try {
    const [flow, history] = await Promise.all([
      getManagedFlow(props.flow.code, current.signal),
      listManagedFlowVersions(props.flow.code, current.signal),
    ])
    if (request !== current || current.signal.aborted) return
    detail.value = flow
    versions.value = history
    state.value = 'ready'
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    if (cause instanceof ApiError && cause.status === 401) { emit('lost'); return }
    error.value = cause instanceof Error ? cause.message : '版本历史读取失败，请重试。'
    state.value = 'error'
  } finally {
    if (request === current) request = null
  }
}

/**
 * 显示可读的发布时间，异常响应不显示无效日期。
 * @param value 服务端的 ISO 8601 时间。
 * @returns 中文本地时间或安全的缺省提示。
 */
function displayTime(value: string): string {
  const timestamp = Date.parse(value)
  return Number.isFinite(timestamp) ? new Date(timestamp).toLocaleString('zh-CN') : '时间未知'
}

/**
 * 明确选择要复制的旧版，清除上一次操作的原因和错误。
 * @param version 一份不可变的历史版本摘要。
 */
function choose(version: ManagedFlowVersion): void {
  if (version.isCurrent) return
  selected.value = version
  reason.value = ''
  error.value = ''
}

/**
 * 带草稿修订和当前发布版双重前提复制旧版内容。
 * @returns 恢复请求完成。
 */
async function restore(): Promise<void> {
  if (!detail.value || !selected.value || saving.value) return
  if (!validRestoreReason(reason.value)) {
    error.value = '恢复原因须填写 2 至 200 字。'
    return
  }
  saving.value = true
  error.value = ''
  try {
    // 使用打开面板时重读的两个前提；其他管理员的变更由服务端 409 拒绝。
    const restored = await restoreManagedFlowVersion(detail.value, selected.value.version, reason.value.trim())
    emit('restored', restored)
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) { emit('lost'); return }
    error.value = cause instanceof ApiError && cause.status === 409
      ? '草稿或当前发布版已变化，请刷新版本历史后重试。'
      : cause instanceof Error ? cause.message : '恢复旧版失败，请重试。'
  } finally {
    saving.value = false
  }
}

watch(() => props.flow.code, () => { void load() }, { immediate: true })
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <section class="admin-panel admin-flow-versions" aria-label="流程发布版本历史">
    <header class="admin-flow-version-heading"><div><h3>{{ flow.name }} · 发布版本</h3><p>发布版不可修改；恢复旧版只会创建新草稿，当前发布版和在途审批保持不变。</p></div><div class="admin-flow-version-actions"><button class="btn" type="button" :disabled="saving || state === 'loading'" @click="load">刷新版本</button><button class="btn" type="button" @click="emit('close')">关闭</button></div></header>
    <p v-if="state === 'loading'" role="status">正在读取版本历史…</p>
    <p v-if="error" class="admin-flow-error" role="alert">{{ error }}</p>
    <button v-if="state === 'error'" class="btn" type="button" @click="load">重新读取</button>
    <template v-if="state === 'ready' && detail">
      <p class="admin-flow-version-current">当前发布版：{{ detail.publishedVersion ? `v${detail.publishedVersion}` : '无' }}；当前草稿修订：{{ detail.draftRevision }}</p>
      <p v-if="!older.length" class="admin-flow-empty">暂无可恢复的旧发布版。</p>
      <ol v-else class="admin-flow-version-list"><li v-for="version in older" :key="version.id"><div><strong>版本 v{{ version.version }}</strong><small>{{ displayTime(version.publishedAt) }}</small></div><button class="btn" type="button" :disabled="saving" @click="choose(version)">复制为新草稿</button></li></ol>
      <form v-if="selected" class="admin-flow-version-form" @submit.prevent="restore"><h4>从版本 v{{ selected.version }} 创建草稿</h4><p>将产生新的草稿修订；请在设计器检查后显式发布，当前发布版不会立即切换。</p><label>恢复原因<input v-model="reason" type="text" minlength="2" maxlength="200" required></label><div><button class="btn btn-primary" type="submit" :disabled="saving">{{ saving ? '正在恢复…' : '确认复制到草稿' }}</button><button class="btn" type="button" :disabled="saving" @click="selected = null">取消</button></div></form>
    </template>
  </section>
</template>

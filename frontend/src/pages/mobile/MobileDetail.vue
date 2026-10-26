<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import { visibleCenterActions } from '../../model/center.ts'
import type { CenterDetail } from '../../model/center.ts'
import type { CenterAction } from '../../model/center.ts'
import type { CenterActionInput } from '../../api/center-actions.ts'
import { mobileActionLabels } from '../../model/mobile.ts'
import SignatureChoice from '../center/SignatureChoice.vue'

/**
 * 手机审批详情的只读数据和查询状态。
 */
const props = defineProps<{
  /**
   * 当前账号有权查看的审批详情。
   */
  detail: CenterDetail | null
  /**
   * 详情读取状态。
   */
  state: 'loading' | 'ready' | 'unavailable' | 'offline' | 'error'
  /**
   * 失败时的中文说明。
   */
  error: string
  /**
   * 审批命令正在提交。
   */
  busy?: boolean
  /**
   * 上次审批命令的错误。
   */
  actionError?: string
}>()

/**
 * 手动刷新详情。
 */
const emit = defineEmits<{
  /**
   * 请求重新读取详情。
   */
  reload: []
  /**
   * 用户明确确认的任务操作。
   */
  execute: [taskId: string, input: CenterActionInput]
}>()

/**
 * 只显示服务端明确允许的动作；提交接口接入前按钮始终禁用。
 */
const actionTasks = computed(() => props.detail?.actionTasks ?? [])
/**
 * 当前选择的任务与操作。
 */
const taskId = ref('')
/**
 * 本次选用的服务器许可操作。
 */
const selectedAction = ref<CenterAction | ''>('')
/**
 * 当前任务的服务器许可。
 */
const actions = computed(() => actionTasks.value.find(task => task.taskId === taskId.value)?.actions ??
  (props.detail ? visibleCenterActions(props.detail.item) : []))
/**
 * 当前填写的审批意见。
 */
const opinion = ref('')
/**
 * 本次明确选用的签名资产 ID。
 */
const signatureFileId = ref('')
/**
 * 转交等操作的目标人员 ID。
 */
const targetPersonId = ref('')
/**
 * 退回目标节点 ID。
 */
const targetNodeId = ref('')
/**
 * 征询或加签关联 ID。
 */
const relatedId = ref('')
/**
 * 退回或加签的模式。
 */
const mode = ref('')
/**
 * 审批人字段的 JSON 输入。
 */
const fieldValuesText = ref('')
/**
 * 当前表单的可修正错误。
 */
const localError = ref('')
/**
 * 仅成功推进动作适用的节点必签规则。
 */
const signatureRequired = computed(() => Boolean(actionTasks.value.find(task => task.taskId === taskId.value)?.signatureRequired &&
  ['approve', 'handle', 'acknowledge'].includes(selectedAction.value)))
/**
 * 页面操作名到服务端命令编码。
 */
const commandByAction: Readonly<Record<CenterAction, string>> = {
  approve: 'approve', handle: 'handle', acknowledge: 'acknowledge', reject: 'reject', return: 'return',
  transfer: 'transfer', addSign: 'addSigner', removeSign: 'removeSigner', consult: 'consult', reply: 'reply',
  supplement: 'requestSupplement', copy: 'copyTo', undoApproval: 'revokeApproval', comment: 'comment',
  saveDraft: 'saveDraft', remind: 'remind',
}

watch(actionTasks, tasks => { taskId.value = tasks[0]?.taskId ?? ''; selectedAction.value = '' })
watch(taskId, () => { selectedAction.value = ''; signatureFileId.value = '' })
watch(selectedAction, () => { signatureFileId.value = '' })

/**
 * 校验当前输入后交给父页执行服务器命令。
 */
function submit(): void {
  localError.value = ''
  if (!selectedAction.value || !taskId.value || !actions.value.includes(selectedAction.value)) return
  if (signatureRequired.value && !signatureFileId.value) {
    localError.value = '当前节点成功办理时必须明确选择手写签名。'
    return
  }
  let fieldValues: Record<string, unknown> | undefined
  if (fieldValuesText.value.trim()) {
    try {
      const parsed: unknown = JSON.parse(fieldValuesText.value)
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('格式错误')
      fieldValues = parsed as Record<string, unknown>
    } catch {
      localError.value = '审批人字段必须填写有效的 JSON 对象。'
      return
    }
  }
  emit('execute', taskId.value, {
    action: commandByAction[selectedAction.value], opinion: opinion.value,
    signatureFileId: signatureFileId.value || undefined,
    targetPersonId: targetPersonId.value || undefined, targetNodeId: targetNodeId.value || undefined,
    relatedId: relatedId.value || undefined, mode: mode.value || undefined, fieldValues,
  })
}
</script>

<template>
  <div class="mobile-detail">
    <div class="mobile-detail-tools"><a href="#/m"><UiIcon name="arrow" />返回列表</a><button type="button" :disabled="state === 'loading'" @click="emit('reload')"><UiIcon name="refresh" />刷新</button></div>
    <template v-if="detail && state === 'ready'"><header class="mobile-detail-heading"><p>{{ detail.item.flowName }} · {{ detail.item.serialNo }}</p><h1 id="page-title" tabindex="-1">{{ detail.item.title }}</h1><div><span class="mobile-status">{{ detail.item.status }}</span><em v-if="detail.item.urgent">加急</em></div></header>
      <section class="mobile-detail-section"><h2>基本信息 <small>业务数据只读</small></h2><dl><div><dt>发起人</dt><dd>{{ detail.item.initiatorName }}</dd></div><div><dt>来源系统</dt><dd>{{ detail.item.sourceName }}</dd></div><div><dt>业务单号</dt><dd>{{ detail.item.businessKey }}</dd></div><div><dt>当前环节</dt><dd>{{ detail.item.currentStep }}</dd></div><div><dt>发起时间</dt><dd>{{ detail.item.createdAt }}</dd></div></dl></section>
      <section class="mobile-detail-section"><h2>审批数据</h2><dl v-if="detail.fields.length"><div v-for="field in detail.fields" :key="field.key"><dt>{{ field.label }}</dt><dd>{{ field.value }}</dd></div></dl><p v-else>没有可展示的业务字段。</p></section>
      <section class="mobile-detail-section"><h2>附件</h2><ul v-if="detail.attachmentNames.length"><li v-for="name in detail.attachmentNames" :key="name">{{ name }}<small>下载与预览待权限接口接入</small></li></ul><p v-else>没有可展示的附件。</p></section>
      <section class="mobile-detail-section"><h2>审批进度</h2><ol v-if="detail.timeline.length" class="mobile-timeline"><li v-for="entry in detail.timeline" :key="entry.id" :class="{ current: entry.current }"><strong>{{ entry.title }}</strong><small>{{ entry.actor }}<template v-if="entry.occurredAt"> · {{ entry.occurredAt }}</template></small><p v-if="entry.opinion">{{ entry.opinion }}</p></li></ol><p v-else>暂无可展示的进度记录。</p></section>
      <form v-if="actionTasks.length" class="mobile-detail-actions" @submit.prevent="submit">
        <p>当前可用操作由服务端确认，提交时会再次校验。</p>
        <label v-if="actionTasks.length > 1">办理任务<select v-model="taskId"><option v-for="task in actionTasks" :key="task.taskId" :value="task.taskId">{{ task.nodeName }}</option></select></label>
        <div><button v-for="action in actions" :key="action" type="button" :disabled="busy" :aria-pressed="selectedAction === action" @click="selectedAction = action">{{ mobileActionLabels[action] }}</button></div>
        <template v-if="selectedAction">
          <label>审批意见<textarea v-model="opinion" rows="3" placeholder="按节点要求填写意见"></textarea></label>
          <SignatureChoice v-if="['approve', 'handle', 'acknowledge', 'reject', 'return'].includes(selectedAction)" v-model="signatureFileId" :required="signatureRequired" />
          <label v-if="['transfer', 'addSign', 'consult', 'copy'].includes(selectedAction)">目标人员 ID<input v-model="targetPersonId"></label>
          <label v-if="selectedAction === 'return'">退回节点 ID<input v-model="targetNodeId"></label>
          <label v-if="['reply', 'removeSign', 'undoApproval'].includes(selectedAction)">关联 ID<input v-model="relatedId"></label>
          <label v-if="['return', 'addSign'].includes(selectedAction)">操作模式<input v-model="mode"></label>
          <label>审批人字段（可选 JSON 对象）<textarea v-model="fieldValuesText" rows="2"></textarea></label>
          <p v-if="localError || actionError" role="alert">{{ localError || actionError }}</p>
          <button type="submit" :disabled="busy">{{ busy ? '提交中…' : `确认${mobileActionLabels[selectedAction]}` }}</button>
        </template>
      </form>
    </template>
    <UiEmptyState v-else-if="state === 'loading'" title="正在读取审批详情" description="请稍候。" icon="refresh" />
    <UiEmptyState v-else-if="state === 'unavailable'" title="手机详情尚未接入" description="真实登录与审批中心接口接入后才能读取这张审批单。" icon="info" />
    <UiEmptyState v-else-if="state === 'offline'" title="当前没有网络" description="联网后会自动重试，也可以手动刷新。" icon="alert"><template #action><button class="btn" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
    <UiEmptyState v-else title="读取审批详情失败" :description="error" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
  </div>
</template>

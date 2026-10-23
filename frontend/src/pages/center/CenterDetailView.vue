<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import { visibleCenterActions } from '../../model/center.ts'
import type { CenterAction, CenterDetail } from '../../model/center.ts'
import type { CenterActionInput } from '../../api/center-adapter.ts'

/**
 * 审批详情已授权数据与查询状态。
 */
const props = defineProps<{
  /**
   * 服务端返回的只读详情。
   */
  detail: CenterDetail | null
  /**
   * 加载、可用或失败。
   */
  state: 'loading' | 'ready' | 'error' | 'unavailable'
  /**
   * 查询失败的中文说明。
   */
  error: string
  /**
   * 路由上的审批单标识。
   */
  requestedId: string
  /**
   * 操作正在提交。
   */
  busy?: boolean
  /**
   * 服务端或本地操作错误。
   */
  actionError?: string
}>()

/**
 * 详情页请求重试。
 */
const emit = defineEmits<{
  /**
   * 用户主动刷新详情。
   */
  reload: []
  /**
   * 当前已许可任务的办理请求。
   */
  execute: [taskId: string, input: CenterActionInput]
}>()

/**
 * 当前的只读详情页签。
 */
const tab = ref<'data' | 'progress'>('data')
/**
 * 仅展示服务端明确许可的操作。
 */
const actionTasks = computed(() => props.detail?.actionTasks ?? [])
/**
 * 当前选择的人工任务。
 */
const taskId = ref('')
/**
 * 当前任务可办理的操作。
 */
const actions = computed(() => actionTasks.value.find(task => task.taskId === taskId.value)?.actions ??
  (props.detail ? visibleCenterActions(props.detail.item) : []))
/**
 * 当前选择的操作与可选参数。
 */
const selectedAction = ref<CenterAction | ''>('')
/**
 * 用户当前填写的审批意见。
 */
const opinion = ref('')
/**
 * 可选目标人员的内部 ID。
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
 * 退回或加签的具体模式。
 */
const mode = ref('')
/**
 * 可选审批人字段的 JSON 正文。
 */
const fieldValuesText = ref('')
/**
 * JSON 输入校验错误。
 */
const localError = ref('')
/**
 * 操作名称与设计书术语保持一致。
 */
const actionLabels: Readonly<Record<CenterAction, string>> = {
  approve: '同意', handle: '办理完成', acknowledge: '确认已阅', reject: '拒绝', return: '退回', transfer: '转交', addSign: '加签', removeSign: '减签',
  consult: '征询意见', reply: '回复征询', supplement: '要求补充', copy: '抄送', undoApproval: '撤销同意', comment: '评论', saveDraft: '暂存意见', remind: '催办',
}

/**
 * 界面词汇到服务端引擎命令的唯一映射。
 */
const commandByAction: Readonly<Record<CenterAction, string>> = {
  approve: 'approve', handle: 'handle', acknowledge: 'acknowledge', reject: 'reject', return: 'return',
  transfer: 'transfer', addSign: 'addSigner', removeSign: 'removeSigner', consult: 'consult', reply: 'reply',
  supplement: 'requestSupplement', copy: 'copyTo', undoApproval: 'revokeApproval', comment: 'comment',
  saveDraft: 'saveDraft', remind: 'remind',
}

watch(actionTasks, tasks => { taskId.value = tasks[0]?.taskId ?? ''; selectedAction.value = '' })
watch(taskId, () => { selectedAction.value = '' })

/**
 * 校验可选字段 JSON 后发出已选任务命令。
 */
function submitAction(): void {
  localError.value = ''
  if (!selectedAction.value || !taskId.value || !actions.value.includes(selectedAction.value)) return
  let fieldValues: Record<string, unknown> | undefined
  if (fieldValuesText.value.trim()) {
    try {
      const parsed: unknown = JSON.parse(fieldValuesText.value)
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) throw new Error('审批人字段必须是 JSON 对象。')
      fieldValues = parsed as Record<string, unknown>
    } catch {
      localError.value = '审批人字段必须填写有效的 JSON 对象。'
      return
    }
  }
  emit('execute', taskId.value, {
    action: commandByAction[selectedAction.value], opinion: opinion.value,
    targetPersonId: targetPersonId.value || undefined, targetNodeId: targetNodeId.value || undefined,
    relatedId: relatedId.value || undefined, mode: mode.value || undefined, fieldValues,
  })
}

watch(() => props.requestedId, () => { tab.value = 'data' })
</script>

<template>
  <div class="center-detail">
    <div class="center-detail-top"><a class="center-back" href="#/"><UiIcon name="arrow" />返回审批中心</a><button class="btn" type="button" :disabled="state === 'loading'" @click="emit('reload')"><UiIcon name="refresh" />刷新</button></div>
    <template v-if="detail && state === 'ready'">
      <header class="center-detail-title"><span class="center-detail-icon"><UiIcon name="center" /></span><div><h2>{{ detail.item.title }} <span v-if="detail.item.urgent" class="center-urgent">加急</span></h2><p>{{ detail.item.flowName }} · {{ detail.item.serialNo }} · {{ detail.item.status }}</p></div></header>
      <div class="center-detail-grid">
        <div class="center-detail-primary">
          <nav class="center-detail-tabs" aria-label="审批详情页签"><button type="button" :aria-current="tab === 'data' ? 'page' : undefined" @click="tab = 'data'">审批数据</button><button type="button" :aria-current="tab === 'progress' ? 'page' : undefined" @click="tab = 'progress'">流程进度</button></nav>
          <section v-if="tab === 'data'" class="center-card"><h3>业务数据快照</h3><dl v-if="detail.fields.length" class="center-data-grid"><div v-for="field in detail.fields" :key="field.key"><dt>{{ field.label }}</dt><dd>{{ field.value }}</dd></div></dl><p v-else class="center-blank">这张审批单没有可展示的业务字段。</p></section>
          <section v-if="tab === 'progress'" class="center-card"><h3>审批经过</h3><ol v-if="detail.timeline.length" class="center-timeline"><li v-for="entry in detail.timeline" :key="entry.id" :class="{ current: entry.current }"><strong>{{ entry.title }}</strong><p>{{ entry.actor }} <span v-if="entry.occurredAt">· {{ entry.occurredAt }}</span></p><blockquote v-if="entry.opinion">{{ entry.opinion }}</blockquote></li></ol><p v-else class="center-blank">暂无可展示的流程记录。</p></section>
          <section class="center-card"><h3>附件</h3><ul v-if="detail.attachmentNames.length" class="center-attachment-list"><li v-for="name in detail.attachmentNames" :key="name">{{ name }} <span>下载与预览待权限接口接入</span></li></ul><p v-else class="center-blank">当前详情接口尚未返回附件信息。</p></section>
        </div>
        <aside class="center-detail-side"><section class="center-card"><h3>审批信息</h3><dl class="center-meta"><dt>发起人</dt><dd>{{ detail.item.initiatorName }}</dd><dt>来源系统</dt><dd>{{ detail.item.sourceName }}</dd><dt>业务单号</dt><dd>{{ detail.item.businessKey }}</dd><dt>当前环节</dt><dd>{{ detail.item.currentStep }}</dd><dt>发起时间</dt><dd>{{ detail.item.createdAt }}</dd></dl></section><section class="center-card"><h3>当前进度</h3><ol v-if="detail.timeline.length" class="center-timeline"><li v-for="entry in detail.timeline" :key="entry.id" :class="{ current: entry.current }"><strong>{{ entry.title }}</strong><p>{{ entry.actor }}</p></li></ol><p v-else class="center-blank">暂无进度记录。</p></section></aside>
      </div>
      <form v-if="actionTasks.length" class="center-action-bar" @submit.prevent="submitAction"><p>可用操作由服务端按当前任务确认，提交时会再次校验。</p><label v-if="actionTasks.length > 1">办理任务<select v-model="taskId"><option v-for="task in actionTasks" :key="task.taskId" :value="task.taskId">{{ task.nodeName }} · {{ task.taskId }}</option></select></label><div><button v-for="action in actions" :key="action" class="btn" :class="{ 'btn-primary': selectedAction === action }" type="button" :disabled="busy" @click="selectedAction = action">{{ actionLabels[action] }}</button></div><template v-if="selectedAction"><label>审批意见<textarea v-model="opinion" rows="3" placeholder="根据节点规则填写意见"></textarea></label><label v-if="['transfer', 'addSign', 'consult', 'copy'].includes(selectedAction)">目标人员 ID<input v-model="targetPersonId" placeholder="审批服务内部人员 ID"></label><label v-if="selectedAction === 'return'">目标节点 ID<input v-model="targetNodeId" placeholder="退回目标节点"></label><label v-if="['reply', 'removeSign', 'undoApproval'].includes(selectedAction)">关联 ID<input v-model="relatedId" placeholder="征询或加签关联 ID"></label><label v-if="['return', 'addSign'].includes(selectedAction)">操作模式<input v-model="mode" placeholder="例如 before、after 或 parallel"></label><label>审批人字段（可选 JSON 对象）<textarea v-model="fieldValuesText" rows="2" placeholder="例如：{&quot;reviewNote&quot;:&quot;已核对&quot;}"></textarea></label><p v-if="localError || actionError" role="alert" class="center-login-error">{{ localError || actionError }}</p><button class="btn btn-primary" type="submit" :disabled="busy">{{ busy ? '提交中…' : `确认${actionLabels[selectedAction]}` }}</button></template></form>
    </template>
    <div v-else class="center-card center-detail-empty">
      <UiEmptyState v-if="state === 'loading'" title="正在读取审批详情" description="请稍候。" icon="refresh" />
      <UiEmptyState v-else-if="state === 'unavailable'" title="审批详情尚未接入" description="详情页面已经就绪，登录与审批中心接口接入后才能读取真实数据。" icon="info" />
      <UiEmptyState v-else title="读取审批详情失败" :description="error" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
    </div>
  </div>
</template>

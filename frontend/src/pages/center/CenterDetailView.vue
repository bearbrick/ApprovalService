<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiDialog from '../../components/ui/UiDialog.vue'
import { fetchCenterFile } from '../../api/center-files.ts'
import { fetchCenterSignature } from '../../api/center-signatures.ts'
import SignatureChoice from './SignatureChoice.vue'
import CenterOriginalDocument from './CenterOriginalDocument.vue'
import { visibleCenterActions } from '../../model/center.ts'
import type { CenterAction, CenterAttachment, CenterDetail } from '../../model/center.ts'
import type { CenterActionInput } from '../../api/center-adapter.ts'
import { loadCenterOpinions } from '../../api/center-opinions.ts'
import type { CenterOpinion } from '../../api/center-opinions.ts'

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
  /**
   * 详情已显示但已读标记未能保存的提示。
   */
  readError?: string
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
 * 业务原单加载成功时隐藏普通数据字段；失败时立即回退。
 */
const originalReady = ref(false)
watch(() => props.detail?.item.id, () => { originalReady.value = false })
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
 * 本次明确选择的手写签名；切换任务或操作后清空。
 */
const signatureFileId = ref('')
/**
 * 仅成功推进时适用的发布节点必签规则。
 */
const signatureRequired = computed(() => Boolean(actionTasks.value.find(task => task.taskId === taskId.value)?.signatureRequired &&
  ['approve', 'handle', 'acknowledge'].includes(selectedAction.value)))
/**
 * 当前人员可选择的已保存意见。
 */
const commonOpinions = ref<CenterOpinion[]>([])
/**
 * 常用意见读取错误不会阻止手动填写和审批。
 */
const commonOpinionError = ref('')
/**
 * 同一详情只读取一次常用意见。
 */
let commonOpinionsLoaded = false
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
 * 当前正在读取的附件 ID。
 */
const busyFileId = ref('')
/**
 * 附件读取失败说明。
 */
const fileError = ref('')
/**
 * 只在当前对话框生命周期内有效的图片或 PDF 地址。
 */
const previewUrl = ref('')
/**
 * 预览文件的显示名称。
 */
const previewName = ref('')
/**
 * 预览文件的实际媒体类型。
 */
const previewType = ref('')
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
watch(taskId, () => { selectedAction.value = ''; signatureFileId.value = '' })
watch(selectedAction, action => {
  signatureFileId.value = ''
  if (!action || commonOpinionsLoaded) return
  commonOpinionsLoaded = true
  void loadCenterOpinions().then(items => { commonOpinions.value = items }).catch(cause => {
    commonOpinionError.value = cause instanceof Error ? cause.message : '常用意见读取失败。'
  })
})

/**
 * 只把已保存的意见填入编辑框，由审批人继续确认提交。
 * @param event 选择事件。
 * @returns 意见栏更新完成。
 */
function chooseCommonOpinion(event: Event): void {
  const select = event.target as HTMLSelectElement
  const selected = commonOpinions.value.find(item => item.id === select.value)
  if (selected) opinion.value = selected.content
  select.value = ''
}

/**
 * 校验可选字段 JSON 后发出已选任务命令。
 */
function submitAction(): void {
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
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) throw new Error('审批人字段必须是 JSON 对象。')
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

watch(() => props.requestedId, () => { tab.value = 'data' })

/**
 * 图片和 PDF 可以在当前页面预览，其他格式只允许下载。
 * @param file 服务端已授权的文件元数据。
 * @returns 是否支持预览。
 */
function canPreview(file: CenterAttachment): boolean {
  return file.contentType.startsWith('image/') || file.contentType === 'application/pdf'
}

/**
 * 关闭预览并释放暂存的浏览器对象地址。
 */
function closePreview(): void {
  if (previewUrl.value) URL.revokeObjectURL(previewUrl.value)
  previewUrl.value = ''
  previewName.value = ''
  previewType.value = ''
}

/**
 * 从现有验权接口取内容，预览或触发用户所选下载。
 * @param file 当前详情已授权的附件。
 * @param preview 是否希望在线预览。
 * @returns 文件操作完成。
 */
async function openFile(file: CenterAttachment, preview: boolean): Promise<void> {
  if (busyFileId.value) return
  busyFileId.value = file.id
  fileError.value = ''
  try {
    const blob = await fetchCenterFile(file.id)
    const url = URL.createObjectURL(blob)
    if (preview && canPreview(file)) {
      closePreview()
      previewName.value = file.fileName
      previewType.value = file.contentType
      previewUrl.value = url
    } else {
      const link = document.createElement('a')
      link.href = url
      link.download = file.fileName
      document.body.appendChild(link)
      link.click()
      link.remove()
      // 下载启动后留一小段时间给浏览器读取对象地址。
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    }
  } catch (cause) {
    fileError.value = cause instanceof Error ? cause.message : '附件读取失败，请稍后重试。'
  } finally {
    busyFileId.value = ''
  }
}

/**
 * 在当前已授权审批单上读取事件冻结的签名 PNG。
 * @param id 事件中的签名资产 ID。
 * @returns 预览已打开。
 */
async function openSignature(id: string): Promise<void> {
  if (!props.detail || busyFileId.value) return
  busyFileId.value = id
  fileError.value = ''
  try {
    const blob = await fetchCenterSignature(id, props.detail.item.id)
    closePreview()
    previewName.value = '手写签名'
    previewType.value = 'image/png'
    previewUrl.value = URL.createObjectURL(blob)
  } catch (cause) {
    fileError.value = cause instanceof Error ? cause.message : '签名读取失败。'
  } finally {
    busyFileId.value = ''
  }
}

onBeforeUnmount(closePreview)
</script>

<template>
  <div class="center-detail">
    <div class="center-detail-top"><a class="center-back" href="#/"><UiIcon name="arrow" />返回审批中心</a><button class="btn" type="button" :disabled="state === 'loading'" @click="emit('reload')"><UiIcon name="refresh" />刷新</button></div>
    <template v-if="detail && state === 'ready'">
      <p v-if="readError" role="alert" class="center-login-error">{{ readError }}</p>
      <header class="center-detail-title"><span class="center-detail-icon"><UiIcon name="center" /></span><div><h2>{{ detail.item.title }} <span v-if="detail.item.urgent" class="center-urgent">加急</span></h2><p>{{ detail.item.flowName }} · {{ detail.item.serialNo }} · {{ detail.item.status }}</p></div></header>
      <div class="center-detail-grid">
        <div class="center-detail-primary">
          <nav class="center-detail-tabs" aria-label="审批详情页签"><button type="button" :aria-current="tab === 'data' ? 'page' : undefined" @click="tab = 'data'">审批数据</button><button type="button" :aria-current="tab === 'progress' ? 'page' : undefined" @click="tab = 'progress'">流程进度</button></nav>
          <section v-if="tab === 'data'" class="center-card">
            <h3>业务数据快照</h3>
            <CenterOriginalDocument v-if="detail.layout.mode === 'embed'" :key="detail.item.id"
              :instance-id="detail.item.id" @ready="originalReady = $event" />
            <div v-if="!originalReady">
            <div v-if="detail.fields.length && detail.layout.mode === 'custom'" class="center-layout-groups">
              <section v-for="(group, groupIndex) in detail.layout.groups" :key="groupIndex" class="center-layout-group">
                <h4>{{ group.title || `分组 ${groupIndex + 1}` }}</h4>
                <dl class="center-layout-grid" :style="{ '--detail-columns': group.columns }">
                  <div v-for="entry in group.items" :key="entry.field.key" :style="{ gridColumn: `span ${entry.columnSpan}` }">
                    <dt>{{ entry.field.label }}</dt>
                    <dd v-if="entry.field.rows && entry.field.columns?.length" class="center-data-table-scroll"><table class="center-data-table"><thead><tr><th v-for="column in entry.field.columns" :key="column.key" :style="{ width: entry.detailColumnWidths[column.key] ? `${entry.detailColumnWidths[column.key]}%` : undefined }">{{ column.label }}</th></tr></thead><tbody><tr v-for="(row, rowIndex) in entry.field.rows" :key="rowIndex"><td v-for="(cell, columnIndex) in row" :key="columnIndex">{{ cell }}</td></tr></tbody></table></dd>
                    <dd v-else>{{ entry.field.value }}<span v-if="entry.moneyUppercase" class="center-money-uppercase">{{ entry.moneyUppercase }}</span></dd>
                  </div>
                </dl>
              </section>
            </div>
            <dl v-else-if="detail.fields.length" class="center-data-grid">
              <div v-for="field in detail.fields" :key="field.key" :class="{ 'center-data-table-field': field.rows }">
                <dt>{{ field.label }}</dt>
                <dd v-if="field.rows && field.columns?.length" class="center-data-table-scroll">
                  <table class="center-data-table"><thead><tr><th v-for="column in field.columns" :key="column.key">{{ column.label }}</th></tr></thead>
                    <tbody><tr v-for="(row, rowIndex) in field.rows" :key="rowIndex"><td v-for="(cell, columnIndex) in row" :key="columnIndex">{{ cell }}</td></tr></tbody>
                  </table>
                </dd>
                <dd v-else>{{ field.value }}</dd>
              </div>
            </dl>
            <p v-else class="center-blank">这张审批单没有可展示的业务字段。</p>
            </div>
          </section>
          <section v-if="tab === 'progress'" class="center-card"><h3>审批经过</h3><ol v-if="detail.timeline.length" class="center-timeline"><li v-for="entry in detail.timeline" :key="entry.id" :class="{ current: entry.current }"><strong>{{ entry.title }}</strong><p>{{ entry.actor }} <span v-if="entry.occurredAt">· {{ entry.occurredAt }}</span></p><blockquote v-if="entry.opinion">{{ entry.opinion }}</blockquote><button v-if="entry.signatureFileId" class="btn" type="button" @click="openSignature(entry.signatureFileId)">查看手写签名</button></li></ol><p v-else class="center-blank">暂无可展示的流程记录。</p></section>
          <section class="center-card"><h3>附件</h3>
            <ul v-if="detail.attachments.length" class="center-attachment-list"><li v-for="file in detail.attachments" :key="file.id"><span class="center-attachment-name">{{ file.fileName }}</span><span>{{ Math.max(1, Math.ceil(file.size / 1024)) }} KB</span><button v-if="canPreview(file)" class="btn" type="button" :disabled="Boolean(busyFileId)" @click="openFile(file, true)">预览</button><button class="btn" type="button" :disabled="Boolean(busyFileId)" @click="openFile(file, false)">下载</button></li></ul>
            <p v-else class="center-blank">没有可展示的附件。</p><p v-if="fileError" role="alert" class="center-login-error">{{ fileError }}</p>
          </section>
        </div>
        <aside class="center-detail-side"><section class="center-card"><h3>审批信息</h3><dl class="center-meta"><dt>发起人</dt><dd>{{ detail.item.initiatorName }}</dd><dt>来源系统</dt><dd>{{ detail.item.sourceName }}</dd><dt>业务单号</dt><dd>{{ detail.item.businessKey }}</dd><dt>当前环节</dt><dd>{{ detail.item.currentStep }}</dd><dt>发起时间</dt><dd>{{ detail.item.createdAt }}</dd></dl></section><section class="center-card"><h3>当前进度</h3><ol v-if="detail.timeline.length" class="center-timeline"><li v-for="entry in detail.timeline" :key="entry.id" :class="{ current: entry.current }"><strong>{{ entry.title }}</strong><p>{{ entry.actor }}</p></li></ol><p v-else class="center-blank">暂无进度记录。</p></section></aside>
      </div>
      <form v-if="actionTasks.length" class="center-action-bar" @submit.prevent="submitAction">
        <p>可用操作由服务端按当前任务确认，提交时会再次校验。</p>
        <label v-if="actionTasks.length > 1">办理任务<select v-model="taskId"><option v-for="task in actionTasks" :key="task.taskId" :value="task.taskId">{{ task.nodeName }} · {{ task.taskId }}</option></select></label>
        <div><button v-for="action in actions" :key="action" class="btn" :class="{ 'btn-primary': selectedAction === action }" type="button" :disabled="busy" @click="selectedAction = action">{{ actionLabels[action] }}</button></div>
        <template v-if="selectedAction">
          <label v-if="commonOpinions.length">常用意见<select aria-label="选择常用意见填入审批意见" @change="chooseCommonOpinion"><option value="">选择常用意见</option><option v-for="item in commonOpinions" :key="item.id" :value="item.id">{{ item.content }}</option></select></label>
          <p v-if="commonOpinionError" role="status">{{ commonOpinionError }}</p>
          <label>审批意见<textarea v-model="opinion" rows="3" placeholder="根据节点规则填写意见"></textarea></label>
          <SignatureChoice v-if="['approve', 'handle', 'acknowledge', 'reject', 'return'].includes(selectedAction)" v-model="signatureFileId" :required="signatureRequired" />
          <label v-if="['transfer', 'addSign', 'consult', 'copy'].includes(selectedAction)">目标人员 ID<input v-model="targetPersonId" placeholder="审批服务内部人员 ID"></label>
          <label v-if="selectedAction === 'return'">目标节点 ID<input v-model="targetNodeId" placeholder="退回目标节点"></label>
          <label v-if="['reply', 'removeSign', 'undoApproval'].includes(selectedAction)">关联 ID<input v-model="relatedId" placeholder="征询或加签关联 ID"></label>
          <label v-if="['return', 'addSign'].includes(selectedAction)">操作模式<input v-model="mode" placeholder="例如 before、after 或 parallel"></label>
          <label>审批人字段（可选 JSON 对象）<textarea v-model="fieldValuesText" rows="2" placeholder="例如：{&quot;reviewNote&quot;:&quot;已核对&quot;}"></textarea></label>
          <p v-if="localError || actionError" role="alert" class="center-login-error">{{ localError || actionError }}</p>
          <button class="btn btn-primary" type="submit" :disabled="busy">{{ busy ? '提交中…' : `确认${actionLabels[selectedAction]}` }}</button>
        </template>
      </form>
    </template>
    <div v-else class="center-card center-detail-empty">
      <UiEmptyState v-if="state === 'loading'" title="正在读取审批详情" description="请稍候。" icon="refresh" />
      <UiEmptyState v-else-if="state === 'unavailable'" title="审批详情尚未接入" description="详情页面已经就绪，登录与审批中心接口接入后才能读取真实数据。" icon="info" />
      <UiEmptyState v-else title="读取审批详情失败" :description="error" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
    </div>
  </div>
  <UiDialog :open="Boolean(previewUrl)" :title="previewName || '附件预览'" class="center-preview-dialog" @close="closePreview">
    <img v-if="previewType.startsWith('image/')" :src="previewUrl" :alt="previewName" class="center-preview-image">
    <iframe v-else-if="previewType === 'application/pdf'" :src="previewUrl" :title="previewName" class="center-preview-pdf"></iframe>
    <template #footer><button class="btn" type="button" @click="closePreview">关闭</button></template>
  </UiDialog>
</template>

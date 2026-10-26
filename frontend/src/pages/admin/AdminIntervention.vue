<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { ApiError } from '../../api/http.ts'
import { executeIntervention, getIntervention, listInterventions } from '../../api/admin-intervention.ts'
import type { InterventionCommand, InterventionRecord } from '../../api/admin-intervention.ts'
import { buildInterventionCommand } from '../../model/admin-intervention.ts'
import UiDialog from '../../components/ui/UiDialog.vue'

/**
 * 权限失效交由外壳退出会话。
 */
const emit = defineEmits<{
  /**
   * 本地会话或管理权限失效。
   */
  lost: []
}>()
/**
 * 最近可干预单据。
 */
const rows = ref<InterventionRecord[]>([])
/**
 * 当前详情。
 */
const selected = ref<InterventionRecord | null>(null)
/**
 * 指定实例查询输入。
 */
const lookup = ref('')
/**
 * 列表内搜索词。
 */
const search = ref('')
/**
 * 当前已授权列表内搜索结果。
 */
const visibleRows = computed(() => rows.value.filter(row => [row.title, row.serialNo, row.id, row.flowCode]
  .some(value => value.toLowerCase().includes(search.value.toLowerCase()))))
/**
 * 操作类型。
 */
const action = ref<InterventionCommand['action']>('reassign')
/**
 * 任务 ID。
 */
const taskId = ref('')
/**
 * 接手人员 ID。
 */
const targetPersonId = ref('')
/**
 * 目标节点 ID。
 */
const targetNodeId = ref('')
/**
 * 原因。
 */
const reason = ref('')
/**
 * 持久错误说明。
 */
const error = ref('')
/**
 * 已确认的成功反馈。
 */
const success = ref('')
/**
 * 防重复操作状态。
 */
const busy = ref(false)
/**
 * 最终确认的不可变命令快照。
 */
const pending = ref<InterventionCommand | null>(null)
/**
 * 上次完整请求正文，供结果未知时复用幂等标识。
 */
let lastPayload = ''
/**
 * 稳定请求 ID。
 */
let requestId = ''
/**
 * 读取取消控制器。
 */
const controller = new AbortController()
/**
 * 当前操作中文名称。
 */
const actionName = computed(() => action.value === 'reassign' ? '改派当前审批人' : action.value === 'jump' ? '跳转到人工节点' : '终止审批')

/**
 * 显示请求错误和处理权限失效。
 * @param cause 失败原因。
 */
function fail(cause: unknown): void {
  if (controller.signal.aborted) return
  error.value = cause instanceof Error ? cause.message : '操作失败，请稍后重试。'
  if (cause instanceof ApiError && [401, 403].includes(cause.status ?? 0)) emit('lost')
}

/**
 * 清除旧结果并刷新列表。
 * @returns 查询完成。
 */
async function refresh(): Promise<void> {
  if (busy.value) return
  busy.value = true
  error.value = ''
  rows.value = []
  selected.value = null
  try { rows.value = await listInterventions(controller.signal) }
  catch (cause) { fail(cause) }
  finally { busy.value = false }
}

/**
 * 重新授权读取详情和运行修订。
 * @param id 审批单 ID。
 * @returns 读取完成。
 */
async function select(id: string): Promise<void> {
  if (busy.value || !id.trim()) return
  busy.value = true
  error.value = ''
  success.value = ''
  selected.value = null
  pending.value = null
  try {
    selected.value = await getIntervention(id.trim(), controller.signal)
    taskId.value = selected.value.tasks[0]?.id ?? ''
    targetNodeId.value = ''
    targetPersonId.value = ''
    reason.value = ''
    action.value = selected.value.status === 'running' ? 'reassign' : 'terminate'
  } catch (cause) { fail(cause) }
  finally { busy.value = false }
}

/**
 * 校验输入并打开最终确认。
 */
function review(): void {
  if (!selected.value || busy.value) return
  error.value = ''
  try {
    pending.value = buildInterventionCommand(selected.value, action.value, taskId.value,
      targetPersonId.value, targetNodeId.value, reason.value)
  } catch (cause) { fail(cause) }
}

/**
 * 提交确认快照；同一实例、修订及正文的未知结果重试复用请求 ID。
 * @returns 写入及刷新结束。
 */
async function submit(): Promise<void> {
  if (!selected.value || !pending.value || busy.value) return
  busy.value = true
  error.value = ''
  const id = selected.value.id
  const command = pending.value
  const payload = JSON.stringify([id, command])
  if (payload !== lastPayload) {
    lastPayload = payload
    requestId = crypto.randomUUID()
  }
  try {
    await executeIntervention(id, command, requestId)
    pending.value = null
    selected.value = null
    success.value = '干预已执行并记录原因。'
    rows.value = []
    rows.value = await listInterventions(controller.signal)
  } catch (cause) {
    pending.value = null
    fail(cause)
    // 保留表单及稳定标识；409 后须重新读取详情再确认。
  } finally { busy.value = false }
}

onMounted(refresh)
onBeforeUnmount(() => controller.abort())
</script>

<template>
  <section class="admin-panel">
    <div class="admin-panel-head"><div><h2>管理员干预</h2><p>仅显示有权管理的在途审批单；保密单还要求本人具备实际参与关系。</p></div><button class="btn" :disabled="busy" @click="refresh">刷新列表</button></div>
    <p class="intervention-note">固定审批人变更：在流程草稿中修改，再发布为新版本；在途实例继续使用原发布版本。此处改派只作用于所选实例的当前任务。批量离职交接尚未接入。</p>
    <p v-if="error" role="alert" class="admin-login-error">{{ error }}</p><p v-if="success" role="status">{{ success }}</p>
    <div class="intervention-filters"><label>筛选当前列表<input v-model="search" placeholder="标题、编号、流程编码"></label><form @submit.prevent="select(lookup)"><label>按审批单 ID 定位<input v-model="lookup" maxlength="100" required></label><button class="btn" :disabled="busy">读取审批单</button></form></div>
    <p>最近 100 条有权查看的在途单据；其他单据可按审批单 ID 定位。</p>
    <div class="intervention-table"><table><thead><tr><th>审批单</th><th>流程</th><th>状态</th><th>操作</th></tr></thead><tbody>
      <tr v-for="row in visibleRows" :key="row.id"><td>{{ row.title }}<small>{{ row.serialNo || row.id }}</small></td><td>{{ row.flowCode }}</td><td>{{ row.status === 'running' ? '审批中' : '等待发起人' }}</td><td><button class="btn" :disabled="busy" @click="select(row.id)">选择</button></td></tr>
      <tr v-if="!visibleRows.length"><td colspan="4">{{ busy ? '正在读取…' : '当前没有匹配的在途单据。' }}</td></tr>
    </tbody></table></div>
    <form v-if="selected" class="intervention-editor" @submit.prevent="review">
      <h3>{{ selected.title }}</h3><p>审批单：{{ selected.id }} · 运行修订：{{ selected.version }}<br>锁定发布版本：{{ selected.flowVersionId }}</p>
      <label>操作<select v-model="action" :disabled="busy"><option v-if="selected.status === 'running'" value="reassign">改派当前审批人</option><option v-if="selected.status === 'running'" value="jump">跳转到人工节点</option><option value="terminate">终止审批</option></select></label>
      <template v-if="action === 'reassign'"><label>当前任务<select v-model="taskId" :disabled="busy" required><option value="">请选择</option><option v-for="task in selected.tasks" :key="task.id" :value="task.id">{{ task.name }} · {{ task.assigneeId }} · {{ task.status === 'queued' ? '排队' : '待办' }}</option></select></label><label>接手人的内部人员 ID<input v-model="targetPersonId" :disabled="busy" maxlength="100" required placeholder="由服务端再次核对在职状态"></label></template>
      <label v-if="action === 'jump'">目标人工节点<select v-model="targetNodeId" :disabled="busy" required><option value="">请选择</option><option v-for="node in selected.nodes" :key="node.id" :value="node.id">{{ node.name }}（{{ node.id }}）</option></select></label>
      <p v-if="action === 'jump'">跳转会取消当前全部待办和分支令牌，并从目标人工节点继续。</p><p v-if="action === 'terminate'">终止会取消未处理任务并通知业务系统，不能撤销本次终止。</p>
      <label>干预原因<textarea v-model="reason" :disabled="busy" minlength="2" maxlength="200" rows="3" required></textarea></label>
      <div><button class="btn btn-primary" :disabled="busy || !['running', 'awaitingInitiator'].includes(selected.status)">核对并确认</button><button class="btn" type="button" :disabled="busy" @click="select(selected.id)">重新读取详情</button></div>
    </form>
    <UiDialog :open="!!pending" title="确认管理员干预" @close="!busy && (pending = null)">
      <p>{{ selected?.title }} · {{ actionName }}</p><p v-if="pending?.taskId">任务：{{ pending.taskId }}</p><p v-if="pending?.targetPersonId">接手人员：{{ pending.targetPersonId }}</p><p v-if="pending?.targetNodeId">目标节点：{{ pending.targetNodeId }}</p><p>原因：{{ pending?.reason }}</p><p>操作将写入审批记录，提交时再次核对权限和运行修订。</p>
      <template #footer><button class="btn" :disabled="busy" @click="pending = null">返回修改</button><button class="btn btn-primary" :disabled="busy" @click="submit">{{ busy ? '正在执行…' : '确认执行' }}</button></template>
    </UiDialog>
  </section>
</template>

<style scoped>
.intervention-note { padding: 14px; background: var(--surface-soft, #f4f7fb); border-radius: 8px; line-height: 1.7; }
.intervention-filters, .intervention-filters form { display: flex; gap: 16px; flex-wrap: wrap; align-items: end; margin: 16px 0; }
label { display: grid; gap: 7px; }
input, select, textarea { padding: 9px; border: 1px solid #cdd5df; border-radius: 6px; font: inherit; }
.intervention-table { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; }
th, td { padding: 12px; text-align: left; border-bottom: 1px solid #e7ebf0; }
small { display: block; margin-top: 5px; color: #667085; }
.intervention-editor { display: grid; gap: 16px; margin-top: 24px; border-top: 1px solid #e7ebf0; padding-top: 20px; max-width: 800px; }
.intervention-editor .btn { margin-right: 10px; }
</style>


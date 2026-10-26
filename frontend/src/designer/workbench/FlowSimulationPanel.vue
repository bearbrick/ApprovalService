<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { FlowDefinition } from '../../model/flow.ts'
import { deleteFlowSimulation, getFlowSimulation, startFlowSimulation, stepFlowSimulation } from '../../api/flow-simulation.ts'
import type { SimulationSession, SimulationTask, SimulationWait } from '../../api/flow-simulation.ts'
import { buildSimulationInput, simulationExampleData, simulationManualNodes } from './simulation-model.ts'

/**
 * 当前页面内可编辑的完整草稿。
 */
const props = defineProps<{
  /**
   * 已读取或新建的流程草稿。
   */
  definition: FlowDefinition
  /**
   * 后端已登记流程编码；模拟接口只接受有管理权限的现存流程。
   */
  registered: boolean
}>()
/**
 * 假发起人；不创建真实组织人员或审批单。
 */
const initiatorId = ref('fake-initiator')
/**
 * 业务数据样例，可直接编辑成不同分支的测试输入。
 */
const dataText = ref(simulationExampleData(props.definition))
/**
 * 每个人工节点的假处理人列表文本。
 */
const peopleText = ref<Record<string, string>>({})
/**
 * 可选的自动动作结果 JSON。
 */
const actionResultText = ref('{}')
/**
 * 当前服务端内存会话。
 */
const session = ref<SimulationSession | null>(null)
/**
 * 会话创建时的节点名称快照，编辑草稿不会改变正在试跑的解释。
 */
const nodeNames = ref<Record<string, string>>({})
/**
 * 请求执行期间防止重复推进。
 */
const busy = ref(false)
/**
 * 错误与操作提示分开显示。
 */
const error = ref('')
/**
 * 仅描述当前模拟的操作结果。
 */
const notice = ref('')
/**
 * 当前草稿需要提供假处理人的节点。
 */
const manualNodes = computed(() => simulationManualNodes(props.definition))
/**
 * 只展示仍可处理的假任务。
 */
const pendingTasks = computed(() => session.value?.tasks.filter(task => task.status === 'pending') ?? [])
/**
 * 最新事件优先，避免长流程把操作区推到屏幕底部。
 */
const recentEvents = computed(() => session.value?.events.slice(-20).reverse() ?? [])

watch(() => props.definition, definition => {
  // 模拟中的会话始终按创建时草稿运行；输入区只在尚未启动时同步新草稿。
  if (session.value) return
  dataText.value = simulationExampleData(definition)
  peopleText.value = Object.fromEntries(simulationManualNodes(definition).map(node => [node.id, 'fake-approver']))
}, { immediate: true })

/**
 * 显示创建会话时的节点名，并始终保留节点 ID 以方便定位画布。
 * @param id 流程节点 ID。
 * @returns 节点名与稳定 ID。
 */
function nodeLabel(id: string): string {
  return `${nodeNames.value[id] ?? props.definition.canvas.nodes.find(node => node.id === id)?.name ?? id} · ${id}`
}

/**
 * 从当前输入启动一场独立内存模拟。
 * @returns 创建操作完成。
 */
async function start(): Promise<void> {
  if (busy.value || session.value || !props.registered) return
  error.value = ''
  notice.value = ''
  try {
    const input = buildSimulationInput(props.definition, dataText.value, initiatorId.value, peopleText.value)
    busy.value = true
    const result = await startFlowSimulation(input)
    session.value = result
    nodeNames.value = Object.fromEntries(props.definition.canvas.nodes.map(node => [node.id, node.name]))
    notice.value = '模拟已开始。会话只保存在服务端内存，闲置 30 分钟后过期。'
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '无法开始测试模式。'
  } finally {
    busy.value = false
  }
}

/**
 * 假审批人处理当前任务，服务端再次核对任务和处理人。
 * @param task 当前待办。
 * @param reject 是否模拟拒绝。
 * @returns 下一步状态读取完成。
 */
async function decide(task: SimulationTask, reject: boolean): Promise<void> {
  if (!session.value || busy.value) return
  busy.value = true
  error.value = ''
  try {
    session.value = await stepFlowSimulation(session.value.id, {
      kind: reject ? 'reject' : task.completionKind, taskId: task.id, actorId: task.assigneeId,
    })
    notice.value = reject ? '已模拟拒绝。' : '已完成该假任务。'
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '假任务处理失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 显式给自动动作或子流程提供假结果，不调用业务系统。
 * @param wait 当前等待令牌。
 * @param success 是否模拟成功。
 * @returns 下一步状态读取完成。
 */
async function resolveWait(wait: SimulationWait, success: boolean): Promise<void> {
  if (!session.value || busy.value) return
  error.value = ''
  let result: unknown
  if (wait.kind === 'action' && success) {
    try { result = JSON.parse(actionResultText.value) } catch {
      error.value = '自动动作假结果必须是有效 JSON。'
      return
    }
  }
  busy.value = true
  try {
    session.value = await stepFlowSimulation(session.value.id, wait.kind === 'subprocess'
      ? { kind: 'subprocessCompleted', tokenId: wait.tokenId, success }
      : { kind: success ? 'actionSucceeded' : 'actionFailed', tokenId: wait.tokenId, actionResult: result })
    notice.value = success ? '假外部结果已提交。' : '已模拟外部失败。'
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '假外部结果提交失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 重新读取服务端会话，失效时保留错误供管理员决定是否重新开始。
 * @returns 读取完成。
 */
async function refresh(): Promise<void> {
  if (!session.value || busy.value) return
  busy.value = true
  error.value = ''
  try { session.value = await getFlowSimulation(session.value.id) } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '模拟会话读取失败。'
  } finally { busy.value = false }
}

/**
 * 删除服务端会话并允许重新编辑假输入。
 * @returns 清理完成。
 */
async function close(): Promise<void> {
  if (!session.value || busy.value) return
  busy.value = true
  error.value = ''
  try { await deleteFlowSimulation(session.value.id) } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '结束模拟失败，请稍后重试。'
    busy.value = false
    return
  }
  session.value = null
  nodeNames.value = {}
  notice.value = '模拟已结束；可以修改假数据后再次试跑。'
  busy.value = false
}
</script>

<template>
  <section class="simulation-panel" aria-labelledby="simulation-title">
    <div class="simulation-heading"><div><span class="eyebrow">发布前检查</span><h2 id="simulation-title">测试模式</h2><p>用当前草稿、假数据和假处理人运行同一审批引擎。不会生成真实审批单，也不会发送提醒或调用业务接口。</p></div><span class="session-badge">仅内存 · 30 分钟闲置过期</span></div>
    <div v-if="!session" class="simulation-inputs">
      <label>假发起人内部 ID<input v-model="initiatorId" maxlength="128" autocomplete="off" /></label>
      <label class="data-field">假业务数据 JSON<textarea v-model="dataText" spellcheck="false" rows="9" /></label>
      <div class="people-grid"><label v-for="node in manualNodes" :key="node.id">{{ node.name }} <small>{{ node.id }} · 多人用逗号分隔</small><input v-model="peopleText[node.id]" autocomplete="off" /></label></div>
      <div class="simulation-actions"><span v-if="!registered" class="empty-hint">请先保存流程草稿，登记编码后再试跑。</span><button type="button" class="btn btn-primary" :disabled="busy || !registered" @click="start">{{ busy ? '正在创建…' : '开始试跑当前草稿' }}</button></div>
    </div>
    <div v-else class="simulation-running">
      <div class="session-toolbar"><div><strong>状态：{{ session.status }}</strong><small>流程 {{ session.flowCode }} · 闲置到期 {{ new Date(session.expiresAt).toLocaleString() }}</small></div><div><button type="button" class="btn" :disabled="busy" @click="refresh">刷新</button><button type="button" class="btn" :disabled="busy" @click="close">结束模拟</button></div></div>
      <div class="simulation-columns"><section><h3>当前假任务 <span>{{ pendingTasks.length }}</span></h3><p v-if="!pendingTasks.length" class="empty-hint">当前没有待处理的人工任务。</p><article v-for="task in pendingTasks" :key="task.id" class="simulation-card"><strong>{{ nodeLabel(task.nodeId) }}</strong><small>假处理人：{{ task.assigneeId }}</small><div><button type="button" class="btn btn-primary" :disabled="busy" @click="decide(task, false)">模拟完成</button><button type="button" class="btn" :disabled="busy" @click="decide(task, true)">模拟拒绝</button></div></article></section><section><h3>等待假外部结果 <span>{{ session.waits.length }}</span></h3><p v-if="!session.waits.length" class="empty-hint">当前没有自动动作或子流程等待。</p><label v-if="session.waits.some(wait => wait.kind === 'action')">自动动作假结果 JSON<textarea v-model="actionResultText" spellcheck="false" rows="3" /></label><article v-for="wait in session.waits" :key="wait.tokenId" class="simulation-card"><strong>{{ nodeLabel(wait.nodeId) }}</strong><small>{{ wait.kind === 'subprocess' ? '子流程' : '自动动作' }} · 令牌 {{ wait.tokenId }}</small><div><button type="button" class="btn btn-primary" :disabled="busy" @click="resolveWait(wait, true)">假成功</button><button type="button" class="btn" :disabled="busy" @click="resolveWait(wait, false)">假失败</button></div></article></section></div>
      <section class="simulation-events"><h3>模拟事件</h3><p v-if="!recentEvents.length" class="empty-hint">引擎尚未产生事件。</p><ol v-else><li v-for="(event, index) in recentEvents" :key="index"><strong>{{ event.kind }}</strong><span>{{ event.nodeId ? nodeLabel(event.nodeId) : '流程' }}</span></li></ol></section>
    </div>
    <p v-if="error" class="simulation-error" role="alert">{{ error }}</p><p v-if="notice" class="simulation-notice" role="status">{{ notice }}</p>
  </section>
</template>

<style scoped>
.simulation-panel { max-width: 960px; margin-top: 18px; padding: 22px; border: 1px solid var(--line); border-radius: 6px; background: #fff; }
.simulation-heading, .session-toolbar { display: flex; justify-content: space-between; align-items: start; gap: 16px; }
.simulation-heading h2 { margin: 4px 0 8px; font-size: 17px; }
.simulation-heading p, .empty-hint { color: var(--muted); font-size: 13px; }
.session-badge { flex: none; padding: 6px 9px; border-radius: 4px; background: #eef4ff; color: var(--blue); font-size: 11px; }
.simulation-inputs { display: grid; gap: 14px; margin-top: 18px; }
.simulation-inputs label, .simulation-running label { display: grid; gap: 6px; color: var(--ink); font-size: 13px; font-weight: 600; }
.simulation-inputs input, .simulation-inputs textarea, .simulation-running textarea { width: 100%; padding: 9px 11px; border: 1px solid var(--line); border-radius: 4px; background: #fff; color: var(--ink); font: inherit; }
.simulation-inputs textarea, .simulation-running textarea { font: 12px/1.5 Consolas, monospace; resize: vertical; }
.data-field { max-width: 700px; }
.people-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
.people-grid small, .session-toolbar small, .simulation-card small { display: block; color: var(--muted); font-size: 11px; font-weight: 400; }
.simulation-actions { display: flex; justify-content: flex-end; }
.simulation-running { margin-top: 20px; }
.session-toolbar { padding: 14px; border: 1px solid var(--line); border-radius: 5px; background: #f7f9fd; }
.session-toolbar div:last-child { display: flex; gap: 8px; }
.simulation-columns { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; margin-top: 20px; }
.simulation-columns h3, .simulation-events h3 { margin-bottom: 10px; font-size: 14px; }
.simulation-columns h3 span { color: var(--blue); }
.simulation-card { display: grid; gap: 8px; margin: 9px 0; padding: 13px; border: 1px solid var(--line); border-radius: 5px; }
.simulation-card div { display: flex; flex-wrap: wrap; gap: 8px; }
.simulation-events { margin-top: 22px; }
.simulation-events ol { display: grid; gap: 8px; max-height: 260px; overflow: auto; margin: 0; padding-left: 22px; }
.simulation-events li { padding: 7px 0; border-bottom: 1px solid var(--line); font-size: 12px; }
.simulation-events li span { margin-left: 12px; color: var(--muted); }
.simulation-error, .simulation-notice { margin-top: 14px; padding: 10px 12px; border-radius: 4px; font-size: 13px; }
.simulation-error { background: #fff2f3; color: #982f3e; }
.simulation-notice { background: #eff8f2; color: #267346; }
@media (max-width: 720px) { .simulation-heading, .session-toolbar { flex-direction: column; } .people-grid, .simulation-columns { grid-template-columns: 1fr; } }
</style>

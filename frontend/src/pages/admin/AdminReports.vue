<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { ApiError } from '../../api/http.ts'
import { loadAdminReports } from '../../api/admin-reports.ts'
import type { ReportBundle, ReportFilter } from '../../api/admin-reports.ts'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import '../../styles/admin-reports.css'

/**
 * 报表页当前角度；概览固定显示在各页签顶部。
 */
const props = defineProps<{
  /**
   * 报表角度。
   */
  tab: string
}>()
/**
 * 访问令牌失效时通知外层移除本地会话。
 */
const emit = defineEmits<{
  /**
   * 访问令牌已失效。
   */
  lost: []
}>()

/**
 * 本地时间写成日期时间输入可显示的格式。
 * @param value 待显示时间。
 * @returns 本地日期时间文本。
 */
function localInputTime(value: Date): string {
  const padded = (part: number) => String(part).padStart(2, '0')
  return `${value.getFullYear()}-${padded(value.getMonth() + 1)}-${padded(value.getDate())}T${padded(value.getHours())}:${padded(value.getMinutes())}`
}

/**
 * 当前待提交的筛选内容。
 */
const filter = ref<ReportFilter>({
  fromLocal: localInputTime(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000)),
  untilLocal: localInputTime(new Date()),
  flowCode: '', clientAppCode: '', departmentId: '',
})
/**
 * 查询状态；失败时清除此前显示的数据。
 */
const state = ref<'loading' | 'ready' | 'empty' | 'forbidden' | 'error'>('loading')
/**
 * 四条接口均成功后才替换的报表数据。
 */
const report = ref<ReportBundle | null>(null)
/**
 * 当前错误说明。
 */
const error = ref('')
/**
 * 最新请求的取消控制器。
 */
let request: AbortController | null = null

/**
 * 查询全部报表数据并阻止旧请求覆盖最新筛选。
 * @returns 查询完成。
 */
async function load(): Promise<void> {
  request?.abort()
  const current = new AbortController()
  request = current
  report.value = null
  error.value = ''
  state.value = 'loading'
  try {
    const result = await loadAdminReports({ ...filter.value }, current.signal)
    if (request !== current || current.signal.aborted) return
    report.value = result
    state.value = result.flows.length || result.nodes.length || result.people.length ||
      Object.values(result.overview).some(value => value > 0) ? 'ready' : 'empty'
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    if (cause instanceof ApiError && cause.status === 401) { emit('lost'); return }
    error.value = cause instanceof Error ? cause.message : '读取报表失败，请稍后重试。'
    state.value = cause instanceof ApiError && cause.status === 403 ? 'forbidden' : 'error'
  }
}

/**
 * 显示非负分钟数；没有真实样本时不制造零值。
 * @param value 服务端耗时。
 * @returns 带单位文本或无样本标记。
 */
function minutes(value: number | null | undefined): string {
  return value == null ? '—' : `${value.toFixed(1)} 分钟`
}

/**
 * 使用已返回的真实分子和分母计算比例。
 * @param numerator 分子。
 * @param denominator 分母。
 * @returns 比例文本或无样本标记。
 */
function rate(numerator: number, denominator: number): string {
  return denominator === 0 ? '—' : `${(numerator / denominator * 100).toFixed(1)}%`
}

watch(() => props.tab, () => { void load() }, { immediate: true })
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <section class="admin-reports">
    <form class="admin-panel admin-report-filters" @submit.prevent="load">
      <div class="admin-panel-head"><div><h2>统计范围</h2><p>按发起时的部门快照筛选；时间范围左闭右开，当前待办以查询时刻为准。</p></div><button class="btn btn-primary" type="submit" :disabled="state === 'loading'">{{ state === 'loading' ? '查询中…' : '查询报表' }}</button></div>
      <div class="admin-report-fields">
        <label>开始时间（包含）<input v-model="filter.fromLocal" type="datetime-local" required></label>
        <label>结束时间（不包含）<input v-model="filter.untilLocal" type="datetime-local" required></label>
        <label>流程编码<input v-model="filter.flowCode" type="text" placeholder="全部流程"></label>
        <label>来源应用编码<input v-model="filter.clientAppCode" type="text" placeholder="全部应用"></label>
        <label>发起部门 ID<input v-model="filter.departmentId" type="text" placeholder="仅查询有权查看的部门"></label>
      </div>
    </form>

    <UiEmptyState v-if="state === 'loading'" title="正在读取统计" description="将同时读取概览、流程、节点和人员指标。" icon="refresh" />
    <UiEmptyState v-else-if="state === 'forbidden'" title="没有报表查看权限" :description="error" icon="alert" />
    <UiEmptyState v-else-if="state === 'error'" title="读取报表失败" :description="error" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="load">重试</button></template></UiEmptyState>
    <template v-else-if="report">
      <div class="admin-report-summary" aria-label="报表概览">
        <div class="admin-report-stat"><span>公开流程发起</span><strong>{{ report.overview.visibleStartedCount }}</strong></div>
        <div class="admin-report-stat"><span>公开流程结束</span><strong>{{ report.overview.visibleCompletedCount }}</strong></div>
        <div class="admin-report-stat"><span>当前待办</span><strong>{{ report.overview.visiblePendingCount }}</strong></div>
        <div class="admin-report-stat"><span>已到期任务中的逾期</span><strong>{{ report.overview.visibleOverdueCount }}</strong></div>
      </div>
      <div class="admin-report-secret" role="note"><strong>保密流程 · 仅匿名数量</strong><span>发起 {{ report.overview.confidentialStartedCount }} · 结束 {{ report.overview.confidentialCompletedCount }}</span><small>不提供保密流程名称、节点或人员明细。</small></div>
      <div v-if="state === 'empty'" class="admin-report-empty">所选范围内没有可展示的统计记录。</div>
      <section v-if="props.tab === 'flow'" class="admin-panel">
        <div class="admin-panel-head"><div><h2>流程效率</h2><p>发起按开始时间，结束和耗时按结束时间；P90 只统计已结束审批。</p></div></div>
        <div class="admin-table-wrap"><table class="admin-table"><thead><tr><th>流程</th><th>发起</th><th>结束</th><th>通过率</th><th>拒绝率</th><th>撤回率</th><th>平均耗时</th><th>P90 耗时</th></tr></thead><tbody><tr v-for="item in report.flows" :key="item.flowCode"><td>{{ item.flowCode }}</td><td>{{ item.startedCount }}</td><td>{{ item.completedCount }}</td><td>{{ rate(item.approvedCount, item.completedCount) }}</td><td>{{ rate(item.rejectedCount, item.completedCount) }}</td><td>{{ rate(item.withdrawnCount, item.completedCount) }}</td><td>{{ minutes(item.averageDurationMinutes) }}</td><td>{{ minutes(item.p90DurationMinutes) }}</td></tr><tr v-if="!report.flows.length"><td colspan="8">当前范围没有公开流程指标。</td></tr></tbody></table></div>
      </section>
      <section v-else-if="props.tab === 'node'" class="admin-panel">
        <div class="admin-panel-head"><div><h2>节点瓶颈</h2><p>超时率分母为筛选区间内已到期的任务。</p></div></div>
        <div class="admin-table-wrap"><table class="admin-table"><thead><tr><th>流程</th><th>节点 ID</th><th>处理量</th><th>平均处理时长</th><th>已到期任务</th><th>逾期任务</th><th>超时率</th></tr></thead><tbody><tr v-for="item in report.nodes" :key="`${item.flowCode}:${item.nodeId}`"><td>{{ item.flowCode }}</td><td>{{ item.nodeId }}</td><td>{{ item.completedCount }}</td><td>{{ minutes(item.averageDurationMinutes) }}</td><td>{{ item.dueCount }}</td><td>{{ item.overdueCount }}</td><td>{{ rate(item.overdueCount, item.dueCount) }}</td></tr><tr v-if="!report.nodes.length"><td colspan="7">当前范围没有公开节点指标。</td></tr></tbody></table></div>
      </section>
      <section v-else class="admin-panel">
        <div class="admin-panel-head"><div><h2>人员负荷</h2><p>仅展示非保密流程的实际处理人和任务指标。</p></div></div>
        <div class="admin-table-wrap"><table class="admin-table"><thead><tr><th>人员 ID</th><th>当前待办</th><th>处理量</th><th>平均处理时长</th><th>逾期次数</th></tr></thead><tbody><tr v-for="item in report.people" :key="item.assigneeId"><td>{{ item.assigneeId }}</td><td>{{ item.pendingCount }}</td><td>{{ item.completedCount }}</td><td>{{ minutes(item.averageDurationMinutes) }}</td><td>{{ item.overdueCount }}</td></tr><tr v-if="!report.people.length"><td colspan="5">当前范围没有公开人员指标。</td></tr></tbody></table></div>
      </section>
    </template>
  </section>
</template>

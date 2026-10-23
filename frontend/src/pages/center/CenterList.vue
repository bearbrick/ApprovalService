<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import { canSelectBatchApprove, eligibleBatchIds, filterCenterItems } from '../../model/center.ts'
import type { CenterFilters, CenterItem, CenterSection } from '../../model/center.ts'

/**
 * 列表所需的已授权数据与加载状态。
 */
const props = defineProps<{
  /**
   * 当前列表分组。
   */
  section: CenterSection
  /**
   * 已授权的列表记录。
   */
  items: readonly CenterItem[]
  /**
   * 加载、可用或失败。
   */
  state: 'loading' | 'ready' | 'error' | 'unavailable'
  /**
   * 查询失败的中文说明。
   */
  error: string
  /**
   * 批量提交中。
   */
  busy?: boolean
  /**
   * 最近一次批量结果说明。
   */
  batchMessage?: string
}>()

/**
 * 请求重新读取列表。
 */
const emit = defineEmits<{
  /**
   * 用户主动重试。
   */
  reload: []
  /**
   * 提交已选审批单的批量任务。
   */
  batch: [instanceIds: string[], opinion: string]
}>()

/**
 * 本地筛选只改变当前用户已授权的记录展示。
 */
const filters = ref<CenterFilters>({ search: '', flowCode: '', status: '', dateFrom: '', dateTo: '' })
/**
 * 已勾选的待办标识，切换分组或数据刷新后清除。
 */
const selectedIds = ref<string[]>([])
/**
 * 筛选后的记录。
 */
const visibleItems = computed(() => filterCenterItems(props.items, props.section, filters.value))
/**
 * 可用的流程筛选项。
 */
const flows = computed(() => [...new Map(props.items.filter(item => item.section === props.section).map(item => [item.flowCode, item.flowName])).entries()])
/**
 * 可用的状态筛选项。
 */
const statuses = computed(() => [...new Set(props.items.filter(item => item.section === props.section).map(item => item.status))])
/**
 * 当前选择里仍满足批量许可的待办。
 */
const batchIds = computed(() => eligibleBatchIds(visibleItems.value, selectedIds.value))
/**
 * 当前可见且可批量的待办。
 */
const selectableItems = computed(() => visibleItems.value.filter(canSelectBatchApprove))
/**
 * 是否全选了当前可见且可批量的待办。
 */
const allSelected = computed(() => selectableItems.value.length > 0 && selectableItems.value.every(item => selectedIds.value.includes(item.id)))
/**
 * 批量同意的共同意见。
 */
const batchOpinion = ref('')

watch(() => props.section, () => { filters.value = { search: '', flowCode: '', status: '', dateFrom: '', dateTo: '' }; selectedIds.value = [] })
watch(() => props.items, () => { selectedIds.value = [] })

/**
 * 切换一条可批量待办的选择状态。
 * @param id 审批单标识。
 */
function toggleSelection(id: string): void {
  const item = visibleItems.value.find(row => row.id === id)
  if (!item || !canSelectBatchApprove(item)) return
  selectedIds.value = selectedIds.value.includes(id) ? selectedIds.value.filter(value => value !== id) : [...selectedIds.value, id]
}

/**
 * 全选或清空当前可见且允许批量的待办。
 */
function toggleAll(): void {
  selectedIds.value = allSelected.value ? [] : selectableItems.value.map(item => item.id)
}

/**
 * 生成安全的详情地址。
 * @param id 审批单标识。
 * @returns 编码后的 hash 地址。
 */
function detailHash(id: string): string {
  return `#/center/approvals/${encodeURIComponent(id)}`
}
</script>

<template>
  <div class="center-list">
    <div class="center-list-head">
      <div><h2>{{ section === 'todo' ? '待处理任务' : section === 'done' ? '处理记录' : section === 'cc' ? '抄送通知' : '发起进度' }}</h2><p>由业务系统提供单据，审批中心只展示有权查看的审批记录。</p></div>
      <button class="btn" type="button" :disabled="state === 'loading'" @click="emit('reload')"><UiIcon name="refresh" />刷新</button>
    </div>
    <div class="center-filterbar">
      <label class="center-search"><span class="sr-only">搜索审批</span><UiIcon name="search" /><input v-model="filters.search" type="search" placeholder="搜索标题、编号、单号或摘要" :disabled="state !== 'ready'"></label>
      <label><span class="sr-only">按流程筛选</span><select v-model="filters.flowCode" :disabled="state !== 'ready'"><option value="">全部流程</option><option v-for="[code, name] in flows" :key="code" :value="code">{{ name }}</option></select></label>
      <label><span class="sr-only">按状态筛选</span><select v-model="filters.status" :disabled="state !== 'ready'"><option value="">全部状态</option><option v-for="status in statuses" :key="status">{{ status }}</option></select></label>
      <label class="center-date"><span>起始</span><input v-model="filters.dateFrom" type="date" :disabled="state !== 'ready'"></label>
      <label class="center-date"><span>截至</span><input v-model="filters.dateTo" type="date" :min="filters.dateFrom || undefined" :disabled="state !== 'ready'"></label>
    </div>
    <div class="center-table-scroll"><table class="center-table"><thead><tr>
      <th v-if="section === 'todo'" class="center-check-cell"><input type="checkbox" aria-label="选择全部可批量同意的待办" :checked="allSelected" :disabled="state !== 'ready' || selectableItems.length === 0" @change="toggleAll"></th>
      <th>审批事项</th><th>流程 / 来源</th><th>摘要</th><th>发起人</th><th>当前环节</th><th>状态</th><th>更新时间</th>
    </tr></thead><tbody>
      <tr v-for="item in visibleItems" :key="item.id" :class="{ 'center-row-unread': item.unread }">
        <td v-if="section === 'todo'" class="center-check-cell"><input type="checkbox" :aria-label="`选择 ${item.title}`" :checked="selectedIds.includes(item.id)" :disabled="!canSelectBatchApprove(item)" @change="toggleSelection(item.id)"></td>
        <td><a class="center-subject" :href="detailHash(item.id)">{{ item.title }}</a><span v-if="item.urgent" class="center-urgent">加急</span><small>{{ item.serialNo }} · {{ item.businessKey }}</small></td>
        <td>{{ item.flowName }}<small>{{ item.sourceName }}</small></td>
        <td><span v-for="field in item.summary" :key="field.key" class="center-summary"><span>{{ field.label }}：</span>{{ field.value }}</span></td>
        <td>{{ item.initiatorName }}</td><td>{{ item.currentStep }}</td><td><span class="center-status">{{ item.status }}</span></td><td>{{ item.updatedAt }}</td>
      </tr>
    </tbody></table></div>
    <UiEmptyState v-if="state === 'loading'" title="正在读取审批记录" description="请稍候。" icon="refresh" />
    <UiEmptyState v-else-if="state === 'unavailable'" title="审批中心尚未接入" description="列表页面已经就绪，登录与审批中心接口接入后才能读取真实待办。" icon="info" />
    <UiEmptyState v-else-if="state === 'error'" title="读取审批记录失败" :description="error" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
    <UiEmptyState v-else-if="visibleItems.length === 0" title="没有匹配的审批记录" description="可以调整筛选条件，或稍后刷新。" icon="search" />
    <div v-if="state === 'ready'" class="center-list-footer"><span>显示 {{ visibleItems.length }} 条</span><div v-if="section === 'todo' && batchIds.length" class="center-batch-note">已选择 {{ batchIds.length }} 条可批量待办 <input v-model="batchOpinion" aria-label="批量审批意见" placeholder="共同审批意见"><button class="btn btn-primary" type="button" :disabled="busy" @click="emit('batch', batchIds, batchOpinion)">{{ busy ? '提交中…' : '批量同意' }}</button></div><p v-if="batchMessage" role="status">{{ batchMessage }}</p></div>
  </div>
</template>

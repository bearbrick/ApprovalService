<script setup lang="ts">
import { computed } from 'vue'
import type { AdminMetric, AdminRow, AdminView } from '../../model/admin.ts'
import AdminTable from './AdminTable.vue'

/**
 * 工作台的真实指标、关注项与查询状态。
 */
const props = defineProps<{
  /**
   * 工作台页面定义。
   */
  view: AdminView
  /**
   * 已授权的关注项。
   */
  rows: readonly AdminRow[]
  /**
   * 已授权的指标；无数据时不得显示虚构数值。
   */
  metrics: readonly AdminMetric[]
  /**
   * 查询状态。
   */
  state: 'loading' | 'ready' | 'error' | 'unavailable'
  /**
   * 查询失败说明。
   */
  error: string
}>()

/**
 * 请求重新读取工作台。
 */
const emit = defineEmits<{
  /**
   * 用户主动刷新。
   */
  reload: []
}>()

/**
 * 需要重点关注的四项真实指标定义。
 */
const metricSlots = [
  { key: 'running', label: '运行中审批' },
  { key: 'completedToday', label: '今日已完成' },
  { key: 'overdue', label: '超时待办' },
  { key: 'failedOutbox', label: '待处理回调' },
] as const
/**
 * 只取真实响应中已提供的指标，其他保持占位。
 */
const visibleMetrics = computed(() => metricSlots.map(slot => ({ ...slot, value: props.metrics.find(metric => metric.key === slot.key)?.value ?? '—' })))
</script>

<template>
  <div class="admin-overview">
    <div class="admin-permission-note">管理权限与数据接口尚未接入；以下指标只在真实响应到达后显示。</div>
    <div class="admin-metrics"><div v-for="metric in visibleMetrics" :key="metric.key" class="admin-metric"><span>{{ metric.label }}</span><strong>{{ metric.value }}</strong><small>{{ state === 'ready' ? '当前授权范围' : '等待数据接口' }}</small></div></div>
    <div class="admin-overview-grid"><AdminTable :view="view" :rows="rows" :state="state" :error="error" @reload="emit('reload')" /><section class="admin-panel admin-shortcuts"><h2>管理入口</h2><p>选择模块继续查看配置结构。</p><div><a href="#/admin/flows">流程管理</a><a href="#/admin/org/people">组织人员</a><a href="#/admin/apps/list">接入应用</a><a href="#/admin/runtime">运行实例</a><a href="#/admin/system/logs">操作日志</a></div></section></div>
  </div>
</template>

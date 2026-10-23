<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import { adminCellText, filterAdminRows } from '../../model/admin.ts'
import type { AdminRow, AdminView } from '../../model/admin.ts'

/**
 * 管理页面的只读列表、当前状态和错误说明。
 */
const props = defineProps<{
  /**
   * 当前页面定义。
   */
  view: AdminView
  /**
   * 服务端授权后的只读行。
   */
  rows: readonly AdminRow[]
  /**
   * 查询状态。
   */
  state: 'loading' | 'ready' | 'error' | 'unavailable'
  /**
   * 查询失败时的中文说明。
   */
  error: string
}>()

/**
 * 用户主动重新读取数据。
 */
const emit = defineEmits<{
  /**
   * 刷新当前页面。
   */
  reload: []
}>()

/**
 * 只读列表中的本地搜索词。
 */
const search = ref('')
/**
 * 从已经授权的安全列中筛出的行。
 */
const visibleRows = computed(() => filterAdminRows(props.rows, props.view.columns, search.value))

watch(() => props.view.key, () => { search.value = '' })
</script>

<template>
  <section class="admin-panel">
    <div class="admin-panel-head"><div><h2>{{ view.title }}</h2><p>{{ view.description }}</p></div><div class="admin-panel-tools"><button v-for="action in view.actions" :key="action" class="btn" type="button" disabled :title="`${action}需管理员权限和审计接口接入`">{{ action }}</button><button class="btn" type="button" :disabled="state === 'loading'" @click="emit('reload')"><UiIcon name="refresh" />刷新</button></div></div>
    <div class="admin-filterbar"><label class="admin-search"><span class="admin-sr-only">搜索当前列表</span><UiIcon name="search" /><input v-model="search" type="search" placeholder="搜索当前列表" :disabled="state !== 'ready'"></label><span class="admin-filter-hint">仅搜索当前账户有权查看的列</span></div>
    <div class="admin-table-wrap"><table class="admin-table"><thead><tr><th v-for="column in view.columns" :key="column.key">{{ column.title }}</th></tr></thead><tbody><tr v-for="row in visibleRows" :key="row.id"><td v-for="column in view.columns" :key="column.key">{{ adminCellText(row, column) }}</td></tr></tbody></table></div>
    <UiEmptyState v-if="state === 'loading'" title="正在读取管理数据" description="请稍候。" icon="refresh" />
    <UiEmptyState v-else-if="state === 'unavailable'" title="管理接口尚未接入" description="页面结构已经就绪，管理员登录与权限接口接入后才能读取真实数据。" icon="info" />
    <UiEmptyState v-else-if="state === 'error'" title="读取管理数据失败" :description="error" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
    <UiEmptyState v-else-if="visibleRows.length === 0" title="没有匹配的记录" description="可以调整搜索词，或稍后刷新。" icon="search" />
    <div v-if="state === 'ready'" class="admin-table-footer">显示 {{ visibleRows.length }} 条已授权记录</div>
  </section>
</template>

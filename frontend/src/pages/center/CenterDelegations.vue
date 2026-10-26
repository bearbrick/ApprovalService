<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ApiError } from '../../api/http-types.ts'
import { centerAdapter } from '../../api/center-adapter.ts'
import { centerSession } from '../../api/center-session.ts'
import { createCenterDelegation, delegateCenterTasks, deleteCenterDelegation, loadCenterDelegations } from '../../api/center-delegations.ts'
import type { CenterDelegation } from '../../api/center-delegations.ts'

/**

 * 当前身份失效时退出中心。

 */
const emit = defineEmits<{
  /**
   * 当前会话失效。
   */
  unauthorized: []
}>()
/**
 * 嵌入会话只读，不能改变本人的委托设置。
 */
const readOnly = Boolean(centerSession()?.targetPath)
/**
 * 已保存的本人规则。
 */
const rules = ref<CenterDelegation[]>([])
/**
 * 代理人的内部人员 ID。
 */
const delegatePersonId = ref('')
/**
 * 浏览器本地时间输入。
 */
const startsAt = ref('')
/**
 * 浏览器本地结束时间。
 */
const endsAt = ref('')
/**
 * 逗号分隔的流程编码。
 */
const flowCodes = ref('')
/**
 * 在本人已授权待办中搜索标题、编号或业务单号。
 */
const taskSearch = ref('')
/**
 * 可转委托的当前在手任务。
 */
const tasks = ref<{ id: string, title: string }[]>([])
/**
 * 被明确选中的已有任务。
 */
const selectedTaskIds = ref<string[]>([])
/**
 * 正在读取或写入。
 */
const busy = ref(false)
/**
 * 可恢复的错误提示。
 */
const error = ref('')
/**
 * 服务器确认后的保存或逐项处理说明。
 */
const status = ref('')

/**
 * 将已保存 UTC 时间显示为浏览器本地时间。
 * @param value 带时区的 ISO 时间。
 * @returns 本地日期与时刻。
 */
function localTime(value: string): string {
  return new Date(value).toLocaleString()
}

/**
 * 读取本人规则，失败不保留旧结果。
 * @returns 读取完成。
 */
async function reload(): Promise<void> {
  busy.value = true
  error.value = ''
  try { rules.value = await loadCenterDelegations() } catch (cause) {
    rules.value = []
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '委托设置读取失败。'
  } finally { busy.value = false }
}

/**
 * 将本地时段转换为 UTC 后交给服务端检查并保存。
 * @returns 保存完成。
 */
async function save(): Promise<void> {
  if (busy.value || readOnly) return
  const start = new Date(startsAt.value)
  const end = new Date(endsAt.value)
  if (!delegatePersonId.value.trim() || !Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) ||
    start >= end) { error.value = '请填写代理人和有效的起止时间。'; return }
  busy.value = true
  error.value = ''
  status.value = ''
  try {
    const created = await createCenterDelegation({
      delegatePersonId: delegatePersonId.value.trim(), startsAtUtc: start.toISOString(), endsAtUtc: end.toISOString(),
      flowCodes: flowCodes.value.split(',').map(code => code.trim()).filter(Boolean),
    })
    rules.value = [...rules.value, created].sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
    delegatePersonId.value = ''; startsAt.value = ''; endsAt.value = ''; flowCodes.value = ''
    status.value = '委托设置已保存；新生成的任务会自动按生效时段交给代理人。'
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '委托设置保存失败。'
  } finally { busy.value = false }
}

/**
 * 删除规则只影响未来任务。
 * @param id 规则 ID。
 * @returns 删除完成。
 */
async function remove(id: string): Promise<void> {
  if (busy.value || readOnly) return
  busy.value = true; error.value = ''; status.value = ''
  try {
    await deleteCenterDelegation(id)
    rules.value = rules.value.filter(item => item.id !== id)
    status.value = '委托设置已删除；已经转出的任务保持原状态。'
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '委托设置删除失败。'
  } finally { busy.value = false }
}

/**
 * 从当前本人有权读取的待办详情取任务 ID，用户再逐项勾选。
 * @returns 当前在手任务读取完成。
 */
async function loadTasks(): Promise<void> {
  if (busy.value || readOnly) return
  busy.value = true; error.value = ''; status.value = ''; tasks.value = []; selectedTaskIds.value = []
  try {
    const rows = await centerAdapter.loadList('todo', new AbortController().signal, taskSearch.value.trim())
    const details = await Promise.all(rows.slice(0, 20).map(item =>
      centerAdapter.loadDetail(item.id, new AbortController().signal)))
    tasks.value = details.flatMap(detail => (detail.delegationTasks ?? []).map(task => ({
      id: task.taskId, title: `${detail.item.title} · ${task.nodeName}`,
    })))
    if (rows.length > 20) status.value = '当前显示前 20 张待办审批单；可输入标题或业务单号缩小范围。'
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '待办任务读取失败。'
  } finally { busy.value = false }
}

/**
 * 对已选任务逐项转委托，并展示每项失败原因。
 * @returns 逐项处理完成。
 */
async function transferSelected(): Promise<void> {
  if (busy.value || readOnly || selectedTaskIds.value.length === 0) return
  busy.value = true; error.value = ''; status.value = ''
  try {
    const results = await delegateCenterTasks(selectedTaskIds.value)
    const succeeded = results.filter(item => item.succeeded).length
    tasks.value = tasks.value.filter(item => !results.some(result => result.taskId === item.id && result.succeeded))
    selectedTaskIds.value = []
    status.value = `已转委托 ${succeeded} 项，失败 ${results.length - succeeded} 项。` +
      results.filter(item => !item.succeeded).map(item => ` ${item.taskId}：${item.message ?? '请刷新后重试'}`).join('；')
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '已有待办转委托失败。'
  } finally { busy.value = false }
}

onMounted(() => { void reload() })
</script>

<template>
  <section class="center-card center-settings center-delegations">
    <header><h2>委托代理</h2><p>按本地时间设置生效区间，系统保存为 UTC 左闭右开时段；流程编码留空表示全部流程。新任务自动委托，已有待办由本人逐项选择。</p></header>
    <div class="center-delegations-body">
      <p v-if="error" role="alert" class="center-login-error">{{ error }}</p><p v-if="status" role="status" class="center-settings-saved">{{ status }}</p>
      <div v-if="rules.length" class="center-delegations-list"><article v-for="rule in rules" :key="rule.id"><div><strong>代理人 {{ rule.delegatePersonId }}</strong><small>{{ localTime(rule.startsAtUtc) }} 至 {{ localTime(rule.endsAtUtc) }} · {{ rule.flowCodes.length ? rule.flowCodes.join('、') : '全部流程' }}</small></div><button v-if="!readOnly" class="btn" type="button" :disabled="busy" @click="remove(rule.id)">删除</button></article></div>
      <p v-else-if="!busy" class="center-settings-muted">尚未设置委托。</p>
      <form v-if="!readOnly" class="center-delegations-form" @submit.prevent="save"><label>代理人内部人员 ID<input v-model="delegatePersonId" required placeholder="由组织管理员维护的人员 ID"></label><label>开始时间<input v-model="startsAt" type="datetime-local" required></label><label>结束时间<input v-model="endsAt" type="datetime-local" required></label><label>流程编码（可选，逗号分隔）<input v-model="flowCodes" placeholder="例如 PURCHASE,EXPENSE"></label><button class="btn btn-primary" type="submit" :disabled="busy">保存委托</button></form>
      <div v-if="!readOnly" class="center-delegations-existing"><h3>已有待办逐项转委托</h3><p>只转移明确勾选且当前规则已生效的任务。完成过的任务不会受影响。</p><label>搜索在手待办<input v-model="taskSearch" placeholder="标题、编号或业务单号"></label><button class="btn" type="button" :disabled="busy" @click="loadTasks">读取当前在手任务</button><div v-if="tasks.length" class="center-delegations-tasks"><label v-for="task in tasks" :key="task.id"><input v-model="selectedTaskIds" type="checkbox" :value="task.id">{{ task.title }}</label></div><button v-if="tasks.length" class="btn btn-primary" type="button" :disabled="busy || !selectedTaskIds.length" @click="transferSelected">转委托所选 {{ selectedTaskIds.length }} 项</button></div>
    </div>
  </section>
</template>

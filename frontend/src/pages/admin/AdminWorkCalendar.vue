<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { loadWorkCalendar, saveWorkCalendar } from '../../api/work-calendar.ts'
import { ApiError } from '../../api/http.ts'
import { calendarValidation } from '../../model/work-calendar.ts'
import type { WorkCalendar } from '../../model/work-calendar.ts'

/**
 * 权限失效时通知外层移除会话。
 */
const emit = defineEmits<{
  /**
   * 当前管理员身份已失效。
   */
  lost: []
}>()
/**
 * 编辑中的完整日历，保留读取时的修订。
 */
const value = ref<WorkCalendar | null>(null)
/**
 * 修改原因。
 */
const reason = ref('')
/**
 * 请求进行状态。
 */
const busy = ref(false)
/**
 * 持久操作结果。
 */
const message = ref('')
/**
 * 错误状态用于辅助技术反馈。
 */
const failed = ref(false)
/**
 * 页面读取取消信号。
 */
let request: AbortController | null = null
/**
 * 页面仍挂载时才回写异步结果。
 */
let active = true
/**
 * 当前输入的可修正错误。
 */
const validation = computed(() => value.value
  ? calendarValidation(value.value.workingStart, value.value.workingEnd, value.value.overrides) : '')
/**
 * 原因须符合服务端审计约束。
 */
const reasonValid = computed(() => reason.value.trim().length >= 2 && reason.value.trim().length <= 200
  && !/[\u0000-\u001f\u007f]/.test(reason.value.trim()))

/**
 * 显示请求错误并处理失效身份。
 * @param error 请求错误。
 */
function showError(error: unknown): void {
  if (!active) return
  if (error instanceof ApiError && (error.status === 401 || error.status === 403)) { emit('lost'); return }
  failed.value = true
  message.value = error instanceof Error ? error.message : '工作日历操作失败，请重试。'
}

/**
 * 显式重新读取会替换当前未保存的编辑。
 * @returns 读取完成。
 */
async function refresh(): Promise<void> {
  if (busy.value) return
  request?.abort()
  request = new AbortController()
  busy.value = true
  message.value = ''
  failed.value = false
  try {
    const next = await loadWorkCalendar(request.signal)
    if (active) { value.value = next; reason.value = '' }
  } catch (error) { showError(error) }
  finally { if (active) busy.value = false }
}

/**
 * 保存成功才替换修订，冲突和网络错误保留全部编辑。
 * @returns 保存完成。
 */
async function save(): Promise<void> {
  if (!value.value || busy.value || validation.value || !reasonValid.value) return
  busy.value = true
  message.value = ''
  failed.value = false
  try {
    const next = await saveWorkCalendar(value.value, reason.value.trim())
    if (active) {
      value.value = next
      reason.value = ''
      message.value = '工作日历已保存。之后新进入待办的任务使用新日历，已有期限和已排提醒保持不变。'
    }
  } catch (error) { showError(error) }
  finally { if (active) busy.value = false }
}

onBeforeUnmount(() => { active = false; request?.abort() })
void refresh()
</script>

<template>
  <section class="admin-panel" aria-label="工作日历设置" :aria-busy="busy">
    <div class="admin-panel-head"><div><h2>工作时段与节假日</h2><p>默认周一至周五工作；下方逐日设置可覆盖周末、节假日和调休。</p></div><button class="btn" type="button" :disabled="busy" @click="refresh">重新读取</button></div>
    <p>重新读取会替换未保存的编辑。保存只影响之后新进入待办的任务，包括排队后才激活的任务；已有期限和已排提醒不重算。</p>
    <form v-if="value" class="calendar-form" @submit.prevent="save">
      <p>日历时区：<strong>{{ value.timeZoneId }}</strong>（由部署配置，只读） · 修订 {{ value.revision }}</p>
      <fieldset :disabled="busy"><legend>每日工作时段</legend><div class="calendar-hours"><label>开始时间<input v-model="value.workingStart" type="time" required></label><label>结束时间<input v-model="value.workingEnd" type="time" required></label></div></fieldset>
      <fieldset :disabled="busy"><legend>逐日覆盖</legend>
        <p v-if="!value.overrides.length">尚无逐日覆盖，所有日期按默认工作周计算。</p>
        <div v-for="(day, index) in value.overrides" :key="index" class="calendar-day"><label>日期<input v-model="day.date" type="date" required></label><label>日期类型<select v-model="day.isWorkingDay"><option :value="false">休息日 / 节假日</option><option :value="true">工作日 / 调休</option></select></label><button class="btn" type="button" :aria-label="`删除第 ${index + 1} 行日期`" @click="value.overrides.splice(index, 1)">删除</button></div>
        <button class="btn" type="button" @click="value.overrides.push({ date: '', isWorkingDay: false })">添加日期</button>
      </fieldset>
      <label>修改原因<textarea v-model="reason" maxlength="200" rows="2" required :disabled="busy" placeholder="填写 2–200 字原因"></textarea></label>
      <p v-if="validation" role="alert">{{ validation }}</p>
      <div><button class="btn btn-primary" type="submit" :disabled="busy || !!validation || !reasonValid">{{ busy ? '正在处理…' : '保存工作日历' }}</button></div>
    </form>
    <p v-else-if="busy" role="status">正在读取工作日历…</p>
    <p v-if="message" :role="failed ? 'alert' : 'status'">{{ message }}</p>
  </section>
</template>

<style scoped>
.calendar-form { display: grid; gap: 20px; max-width: 820px; }
.calendar-form fieldset { border: 1px solid var(--line); border-radius: 8px; padding: 16px; min-width: 0; }
.calendar-form legend { padding: 0 8px; font-weight: 600; }
.calendar-form label { display: grid; gap: 8px; }
.calendar-hours, .calendar-day { display: flex; flex-wrap: wrap; align-items: end; gap: 16px; }
.calendar-day { margin-bottom: 12px; }
.calendar-form input, .calendar-form select, .calendar-form textarea { padding: 9px 12px; border: 1px solid var(--line); border-radius: 6px; font: inherit; background: #fff; color: inherit; }
.calendar-form textarea { width: 100%; box-sizing: border-box; }
</style>

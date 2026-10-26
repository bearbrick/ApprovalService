<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { AppRoute } from '../model/routes.ts'
import type { CenterDetail, CenterItem, CenterSection } from '../model/center.ts'
import { centerAdapter } from '../api/center-adapter.ts'
import { loadCenterTodoCount } from '../api/center-count.ts'
import type { CenterActionInput } from '../api/center-adapter.ts'
import { centerSession, exchangeCenterEmbed, loginCenter, logoutCenter } from '../api/center-session.ts'
import { loadCenterEmbedContext, matchesEmbedTarget, postCenterChange, takeEmbedTokenFromHash } from '../api/center-embed.ts'
import type { CenterEmbedContext } from '../api/center-embed.ts'
import { requestJson } from '../api/http.ts'
import { ApiError } from '../api/http-types.ts'
import CenterList from './center/CenterList.vue'
import CenterDetailView from './center/CenterDetailView.vue'
import CenterSettings from './center/CenterSettings.vue'
import '../styles/center.css'

/**
 * 公共路由解析后的审批中心页面。
 */
const props = defineProps<{
  /**
   * 当前桌面审批中心地址。
   */
  route: AppRoute
}>()

/**
 * 四类列表入口，详情页由路径参数识别。
 */
const sections = [
  { key: 'todo', title: '待我处理', hash: '#/' },
  { key: 'done', title: '我已处理', hash: '#/center/done' },
  { key: 'cc', title: '抄送我的', hash: '#/center/cc' },
  { key: 'started', title: '我发起的', hash: '#/center/started' },
] as const
/**
 * 当前列表分组。
 */
const section = computed<CenterSection>(() => sections.find(item => item.key === props.route.section)?.key ?? 'todo')
/**
 * 列表或详情查询状态。
 */
const state = ref<'loading' | 'ready' | 'error' | 'unavailable'>('loading')
/**
 * 失败时的中文说明。
 */
const error = ref('')
/**
 * 详情已显示但本人已读游标未能持久化时的提示。
 */
const readError = ref('')
/**
 * 服务端按当前人员重新计算的待办角标。
 */
const todoCount = ref<number | null>(null)
/**
 * 当前用户有权查看的列表数据。
 */
const items = ref<readonly CenterItem[]>([])
/**
 * 当前服务端字段搜索词；路由切换时清空。
 */
const searchTerm = ref('')
/**
 * 搜索输入的短暂防抖计时器。
 */
let searchTimer: ReturnType<typeof setTimeout> | null = null
/**
 * 当前用户有权查看的详情。
 */
const detail = ref<CenterDetail | null>(null)
/**
 * 当前人员是否已登录。
 */

const signedIn = ref(Boolean(centerSession()))
/**
 * 登录工号。
 */

const employeeNumber = ref('')
/**
 * 登录密码，不持久化。
 */

const password = ref('')
/**
 * 首次登录时的新密码。
 */

const newPassword = ref('')
/**
 * 登录和改密提交状态。
 */

const authenticating = ref(false)
/**
 * 身份接口返回的中文错误。
 */

const identityError = ref('')
/**
 * 初始片段令牌须在网络请求之前从浏览器历史中同步删除。
 */
let pendingEmbedToken: { token: string, cleanHash: string } | null = null
try { pendingEmbedToken = takeEmbedTokenFromHash() } catch (cause) {
  identityError.value = cause instanceof Error ? cause.message : '内嵌登录地址无效，请从业务系统重新打开。'
}
/**
 * 一次性令牌正在兑换时暂缓普通登录与列表读取。
 */
const embedding = ref(Boolean(pendingEmbedToken))
/**
 * 仅嵌入 JWT 成功读取的服务端来源列表。
 */
let embedContext: CenterEmbedContext | null = null
/**
 * 人工操作与批量同意的提交状态。
 */
const actionBusy = ref(false)
/**
 * 当前详情操作失败说明。
 */
const actionError = ref('')
/**
 * 最近一次成功操作的页面反馈。
 */
const actionNotice = ref('')
/**
 * 最近一次批量办理结果。
 */
const batchMessage = ref('')
/**
 * 首次登录需要修改密码。
 */

const mustChangePassword = ref(centerSession()?.mustChangePassword ?? false)
/**
 * 正在进行的请求，用于避免切换页面后的旧响应覆盖新页面。
 */
let request: AbortController | null = null

/**
 * 读取当前路由对应的列表或详情。
 * @returns 请求完成。
 */
async function load(): Promise<void> {
  request?.abort()
  items.value = []
  detail.value = null
  error.value = ''
  readError.value = ''
  if (!signedIn.value || embedding.value) { state.value = 'unavailable'; todoCount.value = null; return }
  const current = new AbortController()
  request = current
  if (props.route.section === 'settings') {
    state.value = 'unavailable'
    await refreshTodoCount(current)
    return
  }
  state.value = 'loading'
  try {
    if (props.route.resourceId) detail.value = await centerAdapter.loadDetail(props.route.resourceId, current.signal)
    else items.value = await centerAdapter.loadList(section.value, current.signal, searchTerm.value)
    if (request === current && !current.signal.aborted) {
      state.value = 'ready'
      await refreshTodoCount(current)
      if (props.route.resourceId && detail.value) {
        // 等详情真正进入页面后才显式确认已读，列表查询本身不产生写入。
        await nextTick()
        if (request === current && !current.signal.aborted) {
          try { await centerAdapter.markRead(props.route.resourceId, current.signal, detail.value.readSequence) } catch (cause) {
            if (request === current && !current.signal.aborted) {
              if (cause instanceof ApiError && cause.status === 401) {
                signOut()
                identityError.value = '登录已失效，请重新登录。'
              } else readError.value = '已读状态未同步，请刷新详情重试。'
            }
          }
        }
      }
    }
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    if (cause instanceof ApiError && cause.status === 401) {
      logoutCenter()
      embedContext = null
      signedIn.value = false
      identityError.value = '登录已失效，请重新登录。'
    }
    state.value = 'error'
    error.value = cause instanceof Error ? cause.message : '审批中心暂时无法读取数据。'
  }
}

/**
 * 待办数读取失败时清除旧角标，不覆盖已经成功显示的详情。
 * @param current 本轮读取控制器。
 * @returns 待办数更新完成。
 */
async function refreshTodoCount(current: AbortController): Promise<void> {
  try {
    const count = await loadCenterTodoCount(current.signal)
    if (request === current && !current.signal.aborted) todoCount.value = count
  } catch {
    if (request === current && !current.signal.aborted) todoCount.value = null
  }
}

/**
 * 仅在有可信嵌入上下文和实际父窗口时发送完成提示。
 * @param instanceId 服务端返回的审批单 ID。
 * @param taskId 服务端返回的任务 ID。
 */
function notifyChanged(instanceId: string, taskId: string): void {
  if (embedContext) postCenterChange(embedContext, document.referrer,
    window.parent === window ? null : window.parent, instanceId, taskId)
}

/**
 * 兑换一次性令牌并验证服务端声明的目标路由和嵌入来源。
 * @returns 初始身份流程完成。
 */
async function establishEmbedSession(): Promise<void> {
  const pending = pendingEmbedToken
  if (!pending) return
  pendingEmbedToken = null
  logoutCenter()
  signedIn.value = false
  authenticating.value = true
  try {
    const tokens = await exchangeCenterEmbed(pending.token)
    if (tokens.mustChangePassword) throw new Error('请先在审批中心完成首次改密，再从业务系统打开。')
    if (!matchesEmbedTarget(tokens.targetPath, pending.cleanHash)) throw new Error('内嵌登录目标页面不匹配，请从业务系统重新打开。')
    embedContext = await loadCenterEmbedContext()
    signedIn.value = true
  } catch (cause) {
    logoutCenter()
    embedContext = null
    identityError.value = cause instanceof Error ? cause.message : '内嵌登录失败，请从业务系统重新打开。'
  } finally {
    authenticating.value = false
    embedding.value = false
    await load()
  }
}

/**
 * 提交当前任务命令，成功后重新读取服务器许可、详情与角标来源列表。
 * @param taskId 服务端返回的当前任务 ID。
 * @param input 已填写的命令参数。
 * @returns 操作完成。
 */
async function executeAction(taskId: string, input: CenterActionInput): Promise<void> {
  if (actionBusy.value) return
  actionBusy.value = true
  actionError.value = ''
  actionNotice.value = ''
  try {
    const result = await centerAdapter.executeAction(taskId, input)
    notifyChanged(result.instanceId, result.taskId)
    await load()
    actionNotice.value = '操作已完成，详情和待办数量已刷新。'
  } catch (cause) {
    actionError.value = cause instanceof Error ? cause.message : '审批操作失败，请刷新后重试。'
  } finally {
    actionBusy.value = false
  }
}

/**
 * 发起人催办后读取最新详情和待办角标，频率由后端统一引擎核验。
 * @returns 催办完成。
 */
async function remind(): Promise<void> {
  const instanceId = detail.value?.item.id
  if (!instanceId || actionBusy.value || !detail.value?.canRemind) return
  actionBusy.value = true
  actionError.value = ''
  actionNotice.value = ''
  try {
    await centerAdapter.remind(instanceId)
    await load()
    actionNotice.value = '已催办当前审批人。'
  } catch (cause) {
    actionError.value = cause instanceof Error ? cause.message : '催办失败，请稍后重试。'
  } finally {
    actionBusy.value = false
  }
}

/**
 * 将已选单据映射为服务端许可的任务并逐项批量提交。
 * @param instanceIds 已选审批单 ID。
 * @param opinion 共同审批意见。
 * @returns 批量操作完成。
 */
async function executeBatch(instanceIds: string[], opinion: string): Promise<void> {
  if (actionBusy.value) return
  const selected = new Set(instanceIds)
  const taskIds = items.value.filter(item => selected.has(item.id)).flatMap(item => item.bulkTaskIds ?? [])
  if (!taskIds.length) return
  actionBusy.value = true
  batchMessage.value = ''
  try {
    const results = await centerAdapter.batchApprove(taskIds, opinion)
    const passed = results.filter(result => result.succeeded).length
    const failed = results.length - passed
    for (const result of results) {
      if (result.succeeded && result.instanceId) notifyChanged(result.instanceId, result.taskId)
    }
    await load()
    actionNotice.value = '批量结果已更新，待办数量已刷新。'
    batchMessage.value = `已同意 ${passed} 项；失败 ${failed} 项。${failed ? results.filter(result => !result.succeeded).map(result => `${result.taskId}：${result.message ?? '请刷新后重试'}`).join('；') : ''}`
  } catch (cause) {
    batchMessage.value = cause instanceof Error ? cause.message : '批量同意失败，请刷新后核对。'
  } finally {
    actionBusy.value = false
  }
}

/**
 * 通过本地工号密码登录，并立即读取已授权的中心数据。
 * @returns 登录流程完成。
 */
async function signIn(): Promise<void> {
  if (!employeeNumber.value.trim() || !password.value) { identityError.value = '请输入工号和密码。'; return }
  authenticating.value = true
  identityError.value = ''
  try {
    embedContext = null
    const tokens = await loginCenter(employeeNumber.value.trim(), password.value)
    mustChangePassword.value = tokens.mustChangePassword
    if (tokens.mustChangePassword) return
    password.value = ''
    signedIn.value = true
    await load()
  } catch (cause) {
    identityError.value = cause instanceof Error ? cause.message : '登录失败，请稍后重试。'
  } finally {
    authenticating.value = false
  }
}

/**
 * 按首次登录要求改密；服务端作废旧令牌后重新登录。
 * @returns 改密流程完成。
 */
async function changeInitialPassword(): Promise<void> {
  const token = centerSession()?.accessToken
  if (!token || !newPassword.value) { identityError.value = '请输入新密码。'; return }
  authenticating.value = true
  identityError.value = ''
  try {
    await requestJson<void>('/api/v1/identity/change-password', {
      method: 'POST', headers: { Authorization: `Bearer ${token}` },
      body: { currentPassword: password.value, newPassword: newPassword.value }, cache: 'no-store',
    })
    logoutCenter()
    mustChangePassword.value = false
    password.value = ''
    newPassword.value = ''
    identityError.value = '密码已修改，请使用新密码登录。'
  } catch (cause) {
    identityError.value = cause instanceof Error ? cause.message : '修改密码失败。'
  } finally {
    authenticating.value = false
  }
}

/**
 * 清除当前页面会话和已加载的审批数据。
 */
function signOut(): void {
  request?.abort()
  logoutCenter()
  embedContext = null
  signedIn.value = false
  items.value = []
  detail.value = null
  todoCount.value = null
  password.value = ''
}

/**
 * 用户停止输入后请求服务端，避免逐字触发全量字段搜索。
 * @param value 已清理的搜索词。
 */
function search(value: string): void {
  searchTerm.value = value
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => { searchTimer = null; void load() }, 300)
}

watch(() => [props.route.section, props.route.resourceId], () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = null
  searchTerm.value = ''
  void load()
}, { immediate: true })
onMounted(() => { void establishEmbedSession() })
onBeforeUnmount(() => { request?.abort(); if (searchTimer) clearTimeout(searchTimer) })
</script>

<template>
  <header class="page-header center-page-header"><div class="breadcrumb"><a href="#/">审批服务</a><span>/</span><span>{{ route.resourceId ? '审批详情' : route.title }}</span></div><div class="page-title-row"><h1 id="page-title" tabindex="-1">{{ route.resourceId ? '审批详情' : '审批中心' }}</h1><span v-if="signedIn && todoCount !== null" class="center-count-badge">待办 {{ todoCount }}</span><button v-if="signedIn" class="btn" type="button" @click="signOut">退出登录</button></div><p>统一查看待办与审批进度，业务单据由来源系统维护。</p></header>
  <div class="page-body center-page-body">
    <section v-if="!signedIn" class="center-card center-login">
      <h2>{{ embedding ? '正在验证内嵌身份' : mustChangePassword ? '首次登录请修改密码' : '登录审批服务' }}</h2>
      <p>{{ embedding ? '正在确认一次性令牌及应用来源…' : '使用已开通的工号和密码查看属于自己的审批记录。' }}</p>
      <form v-if="!embedding && !mustChangePassword" @submit.prevent="signIn"><label>工号<input v-model="employeeNumber" autocomplete="username" required></label><label>密码<input v-model="password" type="password" autocomplete="current-password" required></label><button class="btn btn-primary" type="submit" :disabled="authenticating">{{ authenticating ? '登录中…' : '登录' }}</button></form>
      <form v-else-if="!embedding" @submit.prevent="changeInitialPassword"><label>新密码<input v-model="newPassword" type="password" autocomplete="new-password" required></label><button class="btn btn-primary" type="submit" :disabled="authenticating">{{ authenticating ? '提交中…' : '修改密码' }}</button></form>
      <p v-if="identityError" role="alert" class="center-login-error">{{ identityError }}</p>
    </section>
    <template v-else>
    <nav v-if="!route.resourceId" class="center-tabs" aria-label="审批列表"><a v-for="item in sections" :key="item.key" :href="item.hash" :aria-current="route.section === item.key ? 'page' : undefined">{{ item.title }}</a><a href="#/center/settings" :aria-current="route.section === 'settings' ? 'page' : undefined">个人设置</a></nav>
    <CenterDetailView v-if="route.resourceId" :requested-id="route.resourceId" :detail="detail" :state="state" :error="error" :read-error="readError" :busy="actionBusy" :action-error="actionError" :action-notice="actionNotice" @reload="load" @execute="executeAction" @remind="remind" />
    <CenterSettings v-else-if="route.section === 'settings'" @unauthorized="signOut" />
    <CenterList v-else :section="section" :items="items" :state="state" :error="error" :busy="actionBusy" :batch-message="batchMessage" @reload="load" @batch="executeBatch" @search="search" />
    <p class="workspace-note">审批中心只处理审批与查看进度，不创建或修改业务单据。</p>
    </template>
  </div>
</template>

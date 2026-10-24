<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import type { AppRoute } from '../model/routes.ts'
import { ADMIN_TABS, TAB_TITLES } from '../model/navigation.ts'
import { resolveAdminView } from '../model/admin.ts'
import type { AdminViewData } from '../model/admin.ts'
import { adminAdapter, AdminAdapterUnavailableError } from '../api/admin-adapter.ts'
import { adminSession, canReadAdminSection, changeAdminPassword, loginAdmin, logoutAdmin } from '../api/admin-session.ts'
import type { AdminSession } from '../api/admin-session.ts'
import { ApiError } from '../api/http.ts'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import AdminOverview from './admin/AdminOverview.vue'
import AdminTable from './admin/AdminTable.vue'
import AdminIntervention from './admin/AdminIntervention.vue'
import AdminOrganizationImport from './admin/AdminOrganizationImport.vue'
import AdminEmbedOriginsEditor from './admin/AdminEmbedOriginsEditor.vue'
import AdminBindingEditor from './admin/AdminBindingEditor.vue'
import AdminCallbackRetryEditor from './admin/AdminCallbackRetryEditor.vue'
import AdminClientAppEditor from './admin/AdminClientAppEditor.vue'
import AdminAccountEditor from './admin/AdminAccountEditor.vue'
import AdminPasswordEditor from './admin/AdminPasswordEditor.vue'
import AdminReports from './admin/AdminReports.vue'
import AdminFlows from './admin/AdminFlows.vue'
import '../styles/admin.css'

/**
 * 管理后台当前已解析的路由。
 */
const props = defineProps<{
  /**
   * 管理一级模块和二级页签。
   */
  route: AppRoute
}>()

/**
 * 由路由决定的当前正式页面定义。
 */
const view = computed(() => resolveAdminView(props.route.section, props.route.tab))
/**
 * 当前模块的二级页签，不涉及权限判断。
 */
const tabs = computed(() => (ADMIN_TABS[props.route.section] ?? []).map(key => ({ key, title: TAB_TITLES[key], hash: `#/admin/${props.route.section}/${key}` })))
/**
 * 管理数据查询状态。
 */
const state = ref<'loading' | 'ready' | 'error' | 'unavailable'>('loading')
/**
 * 授权后得到的只读行与指标。
 */
const data = ref<AdminViewData | null>(null)
/**
 * 当前查询失败时的中文说明。
 */
const error = ref('')
/**
 * 路由切换时取消旧请求，防止旧数据覆盖新页面。
 */
let request: AbortController | null = null
/**
 * 当前仅保存在页面内存中的登录会话。
 */
const session = ref<AdminSession | null>(adminSession())
/**
 * 登录工号。
 */
const employeeNumber = ref('')
/**
 * 登录密码。
 */
const password = ref('')
/**
 * 首次改密时的旧密码。
 */
const currentPassword = ref('')
/**
 * 首次改密时的新密码。
 */
const newPassword = ref('')
/**
 * 登录或改密请求状态。
 */
const signingIn = ref(false)
/**
 * 登录与改密失败说明。
 */
const signInError = ref('')
/**
 * 组织文件导入面板的展开状态。
 */
const importOpen = ref(false)
/**
 * 接入应用来源配置面板的展开状态。
 */
const originsOpen = ref(false)
/**
 * 手工业务系统绑定编辑面板的展开状态。
 */
const bindingOpen = ref(false)
/**
 * 本地人员账号管理面板的展开状态。
 */
const accountOpen = ref(false)
/**
 * 回调手动重发面板的展开状态。
 */
const retryOpen = ref(false)
/**
 * 接入应用创建、轮换和启停编辑器模式。
 */
const appEditorMode = ref<'create' | 'rotate' | 'status' | null>(null)
/**
 * 当前队列列表可供选择的回调事件 ID。
 */
const callbackEventIds = computed(() => [...new Set((data.value?.rows ?? [])
  .filter(row => row.cells.kind === '回调').map(row => row.cells.eventId ?? '').filter(Boolean))])
/**
 * 从已授权应用行提取来源配置所需的选择项。
 */
const appOptions = computed(() => (data.value?.rows ?? []).map(row => ({
  code: row.cells.code ?? '', name: row.cells.name ?? '', isEnabled: row.cells.status === '启用',
})).filter(item => item.code))
/**
 * 仅开放已有后端授权、校验和审计的导入入口。
 */
const enabledActions = computed(() => view.value?.key === 'org/departments' ? ['导入组织']
  : view.value?.key === 'org/people' ? ['导入人员', '账号管理']
    : view.value?.key === 'apps/list' ? ['新建应用', '配置嵌入来源', '轮换密钥', '启停应用']
      : view.value?.key === 'apps/bindings' ? ['新增绑定']
        : view.value?.key === 'outbound/queue' ? ['重新投递'] : [])

/**
 * 处理已接通的页面操作。
 * @param name 当前操作名称。
 */
function handleAction(name: string): void {
  if (!enabledActions.value.includes(name)) return
  if (name === '配置嵌入来源') originsOpen.value = true
  else if (name === '新建应用') appEditorMode.value = 'create'
  else if (name === '轮换密钥') appEditorMode.value = 'rotate'
  else if (name === '启停应用') appEditorMode.value = 'status'
  else if (name === '新增绑定') bindingOpen.value = true
  else if (name === '账号管理') accountOpen.value = true
  else if (name === '重新投递') retryOpen.value = true
  else importOpen.value = true
}

/**
 * 登录后核对管理员权限，再读取当前管理页面。
 * @returns 登录流程完成。
 */
async function submitLogin(): Promise<void> {
  if (signingIn.value) return
  signingIn.value = true
  signInError.value = ''
  try {
    const result = await loginAdmin(employeeNumber.value.trim(), password.value,
      view.value?.section === 'reports' ? 'reports' : view.value?.section === 'flows' ? 'flows' : 'admin')
    session.value = result
    if (result.tokens.mustChangePassword) currentPassword.value = password.value
    else void load()
    password.value = ''
  } catch (cause) {
    signInError.value = cause instanceof Error ? cause.message : '登录失败，请稍后重试。'
  } finally {
    signingIn.value = false
  }
}

/**
 * 首次登录改密后使用新密码重新登录。
 * @returns 改密流程完成。
 */
async function submitPasswordChange(): Promise<void> {
  if (signingIn.value) return
  signingIn.value = true
  signInError.value = ''
  try {
    session.value = await changeAdminPassword(currentPassword.value, newPassword.value,
      view.value?.section === 'reports' ? 'reports' : view.value?.section === 'flows' ? 'flows' : 'admin')
    currentPassword.value = ''
    newPassword.value = ''
    void load()
  } catch (cause) {
    signInError.value = cause instanceof Error ? cause.message : '修改密码失败，请稍后重试。'
  } finally {
    signingIn.value = false
  }
}

/**
 * 退出当前管理会话并移除页面数据。
 */
function signOut(): void {
  request?.abort()
  logoutAdmin()
  session.value = null
  data.value = null
  signInError.value = ''
}

/**
 * 改密后换用已通过管理员权限核对的新会话。
 * @param updated 新密码签发的本地管理员会话。
 */
function passwordChanged(updated: AdminSession): void {
  session.value = updated
}

/**
 * 改密后重新登录失败时清除旧会话并提示使用新密码。
 */
function passwordSessionLost(): void {
  signOut()
  signInError.value = '密码可能已修改，请用新密码重新登录。'
}

/**
 * 只读取当前管理员有权查看的页面数据。
 * @returns 查询完成。
 */
async function load(): Promise<void> {
  request?.abort()
  data.value = null
  error.value = ''
  if (!session.value || session.value.tokens.mustChangePassword) { state.value = 'unavailable'; return }
  if (!view.value) { state.value = 'error'; error.value = '管理页面不存在。'; return }
  if (!canReadAdminSection(session.value, view.value.section)) {
    state.value = 'error'
    error.value = '此会话仅允许查看统计报表。请退出后使用组织管理员身份登录。'
    return
  }
  // 报表组件独立调用四条受 JWT 保护的接口，不经管理员专属数据适配器。
  if (view.value.section === 'reports' || view.value.section === 'flows') { state.value = 'ready'; return }
  const current = new AbortController()
  request = current
  state.value = 'loading'
  try {
    const result = await adminAdapter.loadView(view.value.key, current.signal)
    if (request === current && !current.signal.aborted) { data.value = result; state.value = 'ready' }
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    if (cause instanceof ApiError && (cause.status === 401 || cause.status === 403)) {
      signOut()
      signInError.value = '管理员权限已失效，请重新登录。'
      return
    }
    state.value = cause instanceof AdminAdapterUnavailableError ? 'unavailable' : 'error'
    error.value = cause instanceof Error ? cause.message : '暂时无法读取管理数据。'
  }
}

watch(() => [props.route.section, props.route.tab], () => { importOpen.value = false; originsOpen.value = false; bindingOpen.value = false; accountOpen.value = false; retryOpen.value = false; appEditorMode.value = null; void load() }, { immediate: true })
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <header class="page-header admin-page-header"><div class="breadcrumb"><a href="#/">审批服务</a><span>/</span><span>管理后台</span><template v-if="view && view.section !== 'overview'"><span>/</span><span>{{ view.title }}</span></template></div><div class="page-title-row"><h1 id="page-title" tabindex="-1">{{ view?.title ?? '管理后台' }}</h1><span class="badge">{{ session && !session.tokens.mustChangePassword ? (session.scope === 'reports' ? '报表会话' : session.scope === 'flows' ? '流程管理会话' : '管理员已登录') : '需要登录' }}</span><button v-if="session && !session.tokens.mustChangePassword" class="btn" type="button" @click="signOut">退出登录</button></div><p>{{ view?.description ?? '查看管理模块。' }}</p></header>
  <div class="page-body admin-page-body">
    <section v-if="!session" class="admin-panel admin-login-card"><h2>{{ view?.section === 'reports' ? '报表登录' : view?.section === 'flows' ? '流程管理登录' : '管理员登录' }}</h2><p>{{ view?.section === 'reports' ? '组织管理员和直接负责部门的负责人可登录查看授权范围内的统计。' : view?.section === 'flows' ? '组织管理员与本部门流程管理员可登录；服务端只返回有权管理的流程。' : '请使用本地组织管理员账号登录，管理权限由服务端实时核对。' }}</p><form @submit.prevent="submitLogin"><label>工号<input v-model="employeeNumber" autocomplete="username" required></label><label>密码<input v-model="password" type="password" autocomplete="current-password" required></label><p v-if="signInError" class="admin-login-error" role="alert">{{ signInError }}</p><button class="btn btn-primary" type="submit" :disabled="signingIn">{{ signingIn ? '正在登录…' : '登录' }}</button></form></section>
    <section v-else-if="session.tokens.mustChangePassword" class="admin-panel admin-login-card"><h2>首次登录请修改密码</h2><p>临时密码只能用于改密，完成后才可进入管理后台。</p><form @submit.prevent="submitPasswordChange"><label>当前密码<input v-model="currentPassword" type="password" autocomplete="current-password" required></label><label>新密码<input v-model="newPassword" type="password" autocomplete="new-password" minlength="10" required></label><p v-if="signInError" class="admin-login-error" role="alert">{{ signInError }}</p><div class="admin-login-actions"><button class="btn btn-primary" type="submit" :disabled="signingIn">保存新密码</button><button class="btn" type="button" @click="signOut">退出</button></div></form></section>
    <template v-else>
    <nav v-if="tabs.length" class="admin-tabs" aria-label="管理二级页签"><a v-for="tab in tabs" :key="tab.key" :href="tab.hash" :aria-current="route.tab === tab.key ? 'page' : undefined">{{ tab.title }}</a></nav>
    <template v-if="view">
      <section v-if="!canReadAdminSection(session, view.section)" class="admin-panel"><UiEmptyState title="当前会话不能进入此管理模块" description="请退出后用具有相应管理权限的本地账号登录。" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="signOut">退出并重新登录</button></template></UiEmptyState></section>
      <AdminReports v-else-if="view.section === 'reports'" :tab="view.tab" @lost="signOut" />
      <AdminFlows v-else-if="view.section === 'flows'" @lost="signOut" />
      <AdminOverview v-else-if="view.kind === 'overview'" :view="view" :rows="data?.rows ?? []" :metrics="data?.metrics ?? []" :state="state" :error="error" @reload="load" />
      <template v-else><AdminOrganizationImport v-if="importOpen" @close="importOpen = false" @saved="load" /><AdminAccountEditor v-if="accountOpen" @close="accountOpen = false" @saved="load" /><AdminEmbedOriginsEditor v-if="originsOpen && appOptions.length" :apps="appOptions" @close="originsOpen = false" /><AdminClientAppEditor v-if="appEditorMode" :key="appEditorMode" :mode="appEditorMode" :apps="appOptions" @close="appEditorMode = null" @saved="load" /><AdminBindingEditor v-if="bindingOpen" @close="bindingOpen = false" @saved="load" /><AdminCallbackRetryEditor v-if="retryOpen" :event-ids="callbackEventIds" @close="retryOpen = false" @saved="load" /><AdminPasswordEditor v-if="view.key === 'system/security' && state === 'ready'" @changed="passwordChanged" @lost="passwordSessionLost" /><AdminTable v-else :view="view" :rows="data?.rows ?? []" :state="state" :error="error" :enabled-actions="enabledActions" @reload="load" @action="handleAction" /><AdminIntervention v-if="view.kind === 'runtime'" /></template>
    </template>
    <section v-else class="admin-panel"><UiEmptyState title="没有找到这个管理页面" description="请从管理导航重新选择。" icon="search" /></section>
    <p class="workspace-note">审批服务只管理流程与审批记录；业务单据由来源业务系统维护。</p>
    </template>
  </div>
</template>

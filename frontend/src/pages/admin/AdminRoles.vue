<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { listManagedRoles, saveManagedRole } from '../../api/admin-role-management.ts'
import type { ManagedRole } from '../../api/admin-role-management.ts'
import { loadAdminAccountPeople } from '../../api/admin-adapter.ts'
import type { AdminPerson } from '../../model/admin-data.ts'
import { ApiError } from '../../api/http.ts'

/**
 * 权限丢失时通知外层退出。
 */
const emit = defineEmits<{
  /**
   * 本地会话或当前组织管理员资格失效。
   */
  lost: []
}>()
/**
 * 服务端角色列表。
 */
const roles = ref<ManagedRole[]>([])
/**
 * 已授权人员安全摘要。
 */
const people = ref<AdminPerson[]>([])
/**
 * 当前编辑时读取的角色。
 */
const selected = ref<ManagedRole | null>(null)
/**
 * 是否正在创建角色。
 */
const creating = ref(false)
/**
 * 创建用编码。
 */
const code = ref('')
/**
 * 名称草稿。
 */
const name = ref('')
/**
 * 成员草稿。
 */
const members = ref<string[]>([])
/**
 * 审计原因。
 */
const reason = ref('')
/**
 * 搜索人员文字。
 */
const search = ref('')
/**
 * 读取或提交状态。
 */
const busy = ref(false)
/**
 * 持久操作反馈。
 */
const message = ref('')
/**
 * 当前页面读取取消器。
 */
const controller = new AbortController()
/**
 * 成员筛选仅影响显示，保留未显示成员的选择。
 */
const filteredPeople = computed(() => people.value.filter(person =>
  `${person.name} ${person.employeeNumber} ${person.id}`.toLowerCase().includes(search.value.trim().toLowerCase())))
/**
 * 不在当前目录中的历史成员仍须显式展示，不能默默移除。
 */
const unknownMembers = computed(() => members.value.filter(id => !people.value.some(person => person.id === id)))
/**
 * 原因长度与服务端一致。
 */
const validReason = computed(() => reason.value.trim().length >= 2 && reason.value.trim().length <= 200)

/**
 * 统一处理失效权限和可重试错误。
 * @param cause 原始错误。
 */
function failed(cause: unknown): void {
  if (cause instanceof ApiError && (cause.status === 401 || cause.status === 403)) emit('lost')
  message.value = cause instanceof Error ? cause.message : '角色操作失败，请重试。'
}

/**
 * 切换角色时复制服务端快照，保留原修订直到成功保存。
 * @param role 当前角色；空表示新建。
 */
function edit(role: ManagedRole | null): void {
  selected.value = role
  creating.value = role === null
  code.value = role?.code ?? ''
  name.value = role?.name ?? ''
  members.value = [...(role?.personIds ?? [])]
  reason.value = ''
  message.value = ''
}

/**
 * 重新读取角色和人员，失败不保留可编辑的旧目录。
 * @returns 读取完成。
 */
async function refresh(): Promise<void> {
  if (busy.value) return
  busy.value = true
  message.value = ''
  const previousCode = selected.value?.code
  selected.value = null
  creating.value = false
  roles.value = []
  try {
    const [nextRoles, nextPeople] = await Promise.all([listManagedRoles(controller.signal), loadAdminAccountPeople()])
    if (controller.signal.aborted) return
    roles.value = nextRoles
    people.value = nextPeople
    const next = nextRoles.find(role => role.code === previousCode)
    if (next) edit(next)
  } catch (cause) {
    if (!controller.signal.aborted) failed(cause)
  } finally { busy.value = false }
}

/**
 * 保存创建、名称或完整成员，成功才更新页面修订。
 * @param kind 本次保存类别。
 * @returns 保存完成。
 */
async function save(kind: 'create' | 'name' | 'members'): Promise<void> {
  if (busy.value || !validReason.value || (kind !== 'create' && !selected.value)) return
  busy.value = true
  message.value = ''
  try {
    const draftName = name.value
    const draftMembers = [...members.value]
    const body = kind === 'create' ? { code: code.value.trim(), name: name.value.trim(), reason: reason.value.trim() }
      : { expectedRevision: selected.value!.revision, reason: reason.value.trim(),
        ...(kind === 'name' ? { name: name.value.trim() } : { personIds: [...members.value] }) }
    const saved = await saveManagedRole(kind === 'create' ? 'POST' : 'PUT',
      kind === 'create' ? null : selected.value!.code, kind === 'members', body)
    if (controller.signal.aborted) return
    roles.value = [...roles.value.filter(role => role.code !== saved.code), saved].sort((a, b) => a.code.localeCompare(b.code))
    edit(saved)
    // 两个保存入口独立提交，保留另一部分尚未提交的草稿。
    if (kind === 'name') members.value = draftMembers
    if (kind === 'members') name.value = draftName
    message.value = '角色已保存，权限按当前成员立即生效。'
  } catch (cause) { if (!controller.signal.aborted) failed(cause) }
  finally { busy.value = false }
}

void refresh()
onBeforeUnmount(() => controller.abort())
</script>

<template>
  <section class="admin-panel">
    <div class="page-title-row"><h2>组织角色</h2><button class="btn" :disabled="busy" @click="refresh">刷新角色</button><button class="btn btn-primary" :disabled="busy" @click="edit(null)">新建角色</button></div>
    <p>组织管理员管理全部流程；流程管理员仅管理直接所属部门的流程。角色编码创建后固定。</p>
    <p v-if="message" role="status">{{ message }}</p><p v-if="busy" aria-live="polite">正在处理…</p>
    <table class="data-table"><thead><tr><th>角色</th><th>编码</th><th>成员数</th><th>修订</th><th>操作</th></tr></thead><tbody><tr v-for="role in roles" :key="role.code"><td>{{ role.name }} <span v-if="role.isBuiltIn" class="badge">内置</span></td><td>{{ role.code }}</td><td>{{ role.personIds.length }}</td><td>{{ role.revision }}</td><td><button class="btn" :disabled="busy" @click="edit(role)">编辑</button></td></tr></tbody></table>
    <p v-if="!busy && !roles.length">暂无可显示角色，请刷新重试。</p>
  </section>
  <section v-if="selected || creating" class="admin-panel admin-login-card">
    <h2>{{ creating ? '新建角色' : `编辑 ${selected?.name}` }}</h2>
    <label>角色编码<input v-model="code" maxlength="128" :disabled="busy || !creating" required></label>
    <label>角色名称<input v-model="name" maxlength="256" :disabled="busy" required></label>
    <label>本次修改原因<input v-model="reason" minlength="2" maxlength="200" :disabled="busy" placeholder="填写 2–200 字的操作原因" required></label>
    <button class="btn btn-primary" :disabled="busy || !validReason || !name.trim() || !code.trim()" @click="save(creating ? 'create' : 'name')">{{ creating ? '创建角色' : '保存名称' }}</button>
    <template v-if="selected">
      <h3>角色成员</h3><p>名称和成员分别保存。新增成员须在职；组织管理员至少保留一位已开通账号且在职的人员。保存成员使用当前修订 {{ selected.revision }}。</p>
      <label>筛选人员<input v-model="search" type="search" placeholder="姓名、工号或内部 ID"></label>
      <div class="role-members"><label v-for="person in filteredPeople" :key="person.id"><input v-model="members" type="checkbox" :value="person.id" :disabled="busy || (!person.isActive && !selected.personIds.includes(person.id))">{{ person.name }}（{{ person.employeeNumber }}）{{ person.isActive ? '' : ' · 已离职' }}</label><label v-for="id in unknownMembers" :key="id"><input v-model="members" type="checkbox" :value="id" :disabled="busy">{{ id }} · 历史成员不在当前目录</label></div>
      <p>已选 {{ members.length }} 人；筛选不会清除其他选择。</p><button class="btn btn-primary" :disabled="busy || !validReason" @click="save('members')">保存成员</button>
    </template>
  </section>
</template>

<style scoped>
.role-members { display: grid; gap: .65rem; max-height: 24rem; overflow: auto; margin: 1rem 0; }
.role-members label { display: flex; align-items: center; gap: .5rem; }
.role-members input { width: auto; }
.data-table { width: 100%; text-align: left; border-collapse: collapse; }
.data-table th, .data-table td { padding: .65rem; border-bottom: 1px solid var(--border); }
</style>

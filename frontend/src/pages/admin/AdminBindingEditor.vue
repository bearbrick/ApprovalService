<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { loadAdminBindingOptions, saveAdminBinding } from '../../api/admin-adapter.ts'
import type { AdminClientApp, AdminDirectory } from '../../model/admin-data.ts'

/**
 * 保存完成后通知父页面刷新列表，或关闭编辑器。
 */
const emit = defineEmits<{
  /**
   * 完成一条绑定。
   */
  saved: []
  /**
   * 关闭编辑器。
   */
  close: []
}>()

/**
 * 可选择的接入应用。
 */
const apps = ref<AdminClientApp[]>([])
/**
 * 内部组织目录，用于选择合法目标。
 */
const directory = ref<AdminDirectory>({ departments: [], people: [] })
/**
 * 当前应用编码。
 */
const clientAppCode = ref('')
/**
 * 绑定对象类型。
 */
const kind = ref<'person' | 'department'>('person')
/**
 * 业务系统中的外部 ID。
 */
const externalId = ref('')
/**
 * 内部人员或部门 ID。
 */
const internalId = ref('')
/**
 * 查询或保存状态。
 */
const busy = ref(false)
/**
 * 当前反馈信息。
 */
const message = ref('')
/**
 * 卸载后忽略迟到的异步响应。
 */
let active = true

/**
 * 当前类型下有效的内部目标。
 */
const targets = computed(() => kind.value === 'person'
  ? directory.value.people.filter(person => person.isActive).map(person => ({ id: person.id, label: `${person.name}（${person.employeeNumber}）` }))
  : directory.value.departments.map(department => ({ id: department.id, label: `${department.name}（${department.code}）` })))

/**
 * 从受保护接口加载应用和人员目录。
 * @returns 加载完成。
 */
async function load(): Promise<void> {
  busy.value = true
  message.value = ''
  try {
    const result = await loadAdminBindingOptions()
    if (!active) return
    apps.value = result.apps
    directory.value = result.directory
    clientAppCode.value = result.apps[0]?.code ?? ''
  } catch (error) {
    if (active) message.value = error instanceof Error ? error.message : '无法读取绑定选项。'
  } finally {
    if (active) busy.value = false
  }
}

/**
 * 使用当前本地管理员身份保存绑定并刷新父列表。
 * @returns 保存完成。
 */
async function save(): Promise<void> {
  if (busy.value || !clientAppCode.value || !internalId.value || !externalId.value.trim()) return
  busy.value = true
  message.value = ''
  try {
    await saveAdminBinding(clientAppCode.value, kind.value, externalId.value.trim(), internalId.value)
    if (!active) return
    message.value = '绑定已保存。'
    externalId.value = ''
    emit('saved')
  } catch (error) {
    if (active) message.value = error instanceof Error ? error.message : '保存绑定失败。'
  } finally {
    if (active) busy.value = false
  }
}

watch(kind, () => { internalId.value = '' })
onBeforeUnmount(() => { active = false })
void load()
</script>

<template>
  <section class="admin-panel admin-binding-panel" aria-label="业务系统绑定编辑器">
    <div class="admin-panel-head"><div><h2>建立业务系统绑定</h2><p>指定业务系统外部 ID 对应的审批服务人员或部门；已有绑定将更新目标。</p></div><button class="btn" type="button" @click="emit('close')">关闭</button></div>
    <form class="admin-binding-content" @submit.prevent="save"><label>接入应用<select v-model="clientAppCode" required :disabled="busy"><option v-for="app in apps" :key="app.code" :value="app.code">{{ app.name }}（{{ app.code }}）</option></select></label><label>绑定类型<select v-model="kind" :disabled="busy"><option value="person">人员</option><option value="department">部门</option></select></label><label>业务系统外部 ID<input v-model="externalId" required maxlength="128" :disabled="busy" placeholder="如 ERP 用户或部门 ID"></label><label>审批服务目标<select v-model="internalId" required :disabled="busy"><option value="" disabled>请选择</option><option v-for="target in targets" :key="target.id" :value="target.id">{{ target.label }}</option></select></label><div class="admin-import-actions"><button class="btn btn-primary" type="submit" :disabled="busy || !apps.length || !targets.length">保存绑定</button></div><p v-if="message" role="status">{{ message }}</p></form>
  </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { ApiError } from '../../api/http.ts'
import { exportManagedFlow, importManagedFlow, listManagedFlows, previewManagedImport,
  setManagedFlowEnabled } from '../../api/admin-flow-management.ts'
import type { FlowImportPreview, ManagedFlow } from '../../api/admin-flow-management.ts'
import '../../styles/admin-flows.css'

/**
 * 会话过期后请管理外壳清除内存令牌。
 */
const emit = defineEmits<{
  /**
   * 本地管理会话失效。
   */
  lost: []
}>()

/**
 * 当前本人获授权的流程摘要。
 */
const flows = ref<ManagedFlow[]>([])
/**
 * 读取、写入与导出共用的等待状态。
 */
const busy = ref(false)
/**
 * 当前列表筛选词。
 */
const search = ref('')
/**
 * 可见错误。
 */
const error = ref('')
/**
 * 成功提示。
 */
const message = ref('')
/**
 * 停用或恢复前明确选择的流程。
 */
const statusFlow = ref<ManagedFlow | null>(null)
/**
 * 停启用的管理员原因。
 */
const statusReason = ref('')
/**
 * 从本机读取但尚未保存的 BPMN 文件。
 */
const importXml = ref('')
/**
 * 预检结果，失效时不得继续导入。
 */
const preview = ref<FlowImportPreview | null>(null)
/**
 * 导入新流程或覆盖草稿的原因。
 */
const importReason = ref('')
/**
 * 新建时使用的流程分组。
 */
const importGroup = ref('')
/**
 * 新建时使用的直属所属部门内部 ID。
 */
const importOwnerDepartmentId = ref('')
/**
 * 新建时使用的运行期管理员内部 ID。
 */
const importAdministratorId = ref('')
/**
 * 是否显式覆盖同编码草稿。
 */
const overwrite = ref(false)
/**
 * 当前目录读取请求，可在组件卸载时中止。
 */
let request: AbortController | null = null

/**
 * 仅根据服务器返回的安全摘要筛选当前页面行。
 * @returns 当前筛选可见的流程。
 */
const visible = () => flows.value.filter(flow => !search.value.trim()
  || `${flow.name} ${flow.code} ${flow.group ?? ''}`.toLocaleLowerCase()
    .includes(search.value.trim().toLocaleLowerCase()))

/**
 * 从有权限的管理接口重读流程目录。
 * @returns 目录读取完成。
 */
async function load(): Promise<void> {
  request?.abort()
  const current = new AbortController()
  request = current
  error.value = ''
  try {
    const result = await listManagedFlows(current.signal)
    if (request === current && !current.signal.aborted) flows.value = result
  } catch (cause) {
    if (request !== current || current.signal.aborted) return
    if (cause instanceof ApiError && cause.status === 401) { emit('lost'); return }
    error.value = cause instanceof Error ? cause.message : '暂时无法读取流程目录。'
  } finally {
    if (request === current) request = null
  }
}

/**
 * 选择停用或恢复操作，实际写入前还要填写原因。
 * @param flow 服务端返回的当前流程摘要。
 */
function chooseStatus(flow: ManagedFlow): void {
  statusFlow.value = flow
  statusReason.value = ''
  error.value = ''
  message.value = ''
}

/**
 * 以列表中读到的旧状态作为 CAS 前提提交停启用。
 * @returns 操作完成。
 */
async function changeStatus(): Promise<void> {
  if (!statusFlow.value || busy.value) return
  busy.value = true
  error.value = ''
  try {
    const result = await setManagedFlowEnabled(statusFlow.value, !statusFlow.value.isEnabled,
      statusReason.value.trim())
    flows.value = flows.value.map(flow => flow.code === result.code ? result : flow)
    message.value = result.isEnabled ? '流程已恢复新发起。' : '流程已停用新发起；在途审批不受影响。'
    statusFlow.value = null
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) { emit('lost'); return }
    error.value = cause instanceof Error ? cause.message : '流程状态修改失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 读取用户明确选择的兼容文件并调用只读预检。
 * @param event 文件选择事件。
 * @returns 文件预检完成。
 */
async function chooseImport(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file || busy.value) return
  preview.value = null
  importXml.value = ''
  error.value = ''
  message.value = ''
  if (file.size > 4 * 1024 * 1024) { error.value = 'BPMN 文件超过 4 MB，请选择较小的兼容文件。'; return }
  busy.value = true
  try {
    const xml = await file.text()
    const result = await previewManagedImport(xml)
    importXml.value = xml
    preview.value = result
    overwrite.value = false
    message.value = `已预检 ${result.name}，${result.nodeCount} 个节点；保存时不会直接发布。`
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) { emit('lost'); return }
    error.value = cause instanceof Error ? cause.message : 'BPMN 文件预检失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 使用预检时看到的修订号创建或显式覆盖草稿。
 * @returns 导入操作完成。
 */
async function saveImport(): Promise<void> {
  if (!preview.value || !importXml.value || busy.value) return
  if (preview.value.hasCodeConflict && !overwrite.value) {
    error.value = '同编码流程已存在。若要覆盖，请明确勾选覆盖草稿。'
    return
  }
  busy.value = true
  error.value = ''
  try {
    const result = await importManagedFlow(importXml.value, {
      group: importGroup.value.trim() || null,
      ownerDepartmentId: importOwnerDepartmentId.value.trim() || null,
      administratorId: importAdministratorId.value.trim() || null,
    }, importReason.value.trim(), overwrite.value ? preview.value.code : undefined,
    overwrite.value ? preview.value.existingDraftRevision ?? undefined : undefined)
    preview.value = null
    importXml.value = ''
    message.value = `草稿 ${result.code} 已保存为修订 ${result.draftRevision}；发布前请在设计器检查。`
    await load()
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) { emit('lost'); return }
    error.value = cause instanceof Error ? cause.message : '导入草稿失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 下载一份由服务端授权导出的兼容 BPMN 文件。
 * @param flow 当前安全流程摘要。
 * @param source 当前草稿或当前发布版。
 * @returns 文件下载完成。
 */
async function download(flow: ManagedFlow, source: 'draft' | 'published'): Promise<void> {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    const xml = await exportManagedFlow(flow.code, source,
      source === 'published' ? flow.publishedVersion ?? undefined : undefined)
    const url = URL.createObjectURL(new Blob([xml], { type: 'application/xml' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'approval-flow.bpmn'
    document.body.append(link)
    link.click()
    link.remove()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
    message.value = `${flow.name} 的${source === 'draft' ? '草稿' : '发布版'} BPMN 文件已下载。`
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) { emit('lost'); return }
    error.value = cause instanceof Error ? cause.message : '导出 BPMN 失败。'
  } finally {
    busy.value = false
  }
}

void load()
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <section class="admin-flow-manager">
    <div class="admin-panel admin-flow-heading"><div><h2>流程管理</h2><p>只展示当前账号有权管理的流程。草稿可修改；发布版不可变。</p></div><div class="admin-flow-actions"><a class="btn btn-primary" href="#/designer/NEW_FLOW/design">＋ 新建流程</a><a class="btn" href="#/designer/templates">使用模板</a><button class="btn" type="button" :disabled="busy" @click="load">刷新</button></div></div>
    <p v-if="error" class="admin-flow-error" role="alert">{{ error }}</p><p v-if="message" class="admin-flow-success" role="status">{{ message }}</p>
    <div class="admin-panel admin-flow-list"><label>筛选流程<input v-model="search" type="search" placeholder="名称、编码或分组"></label><div v-if="!visible().length" class="admin-flow-empty">当前范围没有流程。可从空白画布或模板建立草稿。</div><table v-else><thead><tr><th>流程</th><th>分组 / 所属部门</th><th>草稿 / 发布版</th><th>状态</th><th>操作</th></tr></thead><tbody><tr v-for="flow in visible()" :key="flow.code"><td><strong>{{ flow.name }}</strong><small>{{ flow.code }}</small></td><td>{{ flow.group || '未分组' }}<small>{{ flow.ownerDepartmentId || '组织级' }}</small></td><td>修订 {{ flow.draftRevision }}<small>{{ flow.publishedVersion ? `发布 v${flow.publishedVersion}` : '尚未发布' }}</small></td><td><span :class="flow.isEnabled ? 'on' : 'off'">{{ flow.isEnabled ? '可发起' : '已停用' }}</span></td><td class="admin-flow-row-actions"><a :href="`#/designer/${encodeURIComponent(flow.code)}/design`">设计</a><button type="button" :disabled="busy" @click="chooseStatus(flow)">{{ flow.isEnabled ? '停用' : '恢复' }}</button><button type="button" :disabled="busy" @click="download(flow, 'draft')">导出草稿</button><button type="button" :disabled="busy || !flow.publishedVersion" @click="download(flow, 'published')">导出发布版</button></td></tr></tbody></table></div>
    <form v-if="statusFlow" class="admin-panel admin-flow-inline-form" @submit.prevent="changeStatus"><h3>{{ statusFlow.isEnabled ? '停用' : '恢复' }} {{ statusFlow.name }}</h3><p>服务端会再次核对当前状态与权限；停用仅阻止新发起。</p><label>变更原因<input v-model="statusReason" minlength="2" maxlength="200" required></label><div><button class="btn btn-primary" type="submit" :disabled="busy">确认{{ statusFlow.isEnabled ? '停用' : '恢复' }}</button><button class="btn" type="button" @click="statusFlow = null">取消</button></div></form>
    <form class="admin-panel admin-flow-import" @submit.prevent="saveImport"><h3>导入兼容 BPMN</h3><p>先在服务端预检文件。导入只保存草稿，发布须另行确认。</p><label>选择本服务导出的 .bpmn 文件<input type="file" accept=".bpmn,.xml,application/xml,text/xml" :disabled="busy" @change="chooseImport"></label><template v-if="preview"><p class="admin-flow-preview"><strong>{{ preview.name }}</strong>（{{ preview.code }}）· {{ preview.nodeCount }} 个节点</p><p v-if="preview.hasCodeConflict" class="admin-flow-error">同编码流程已存在。默认拒绝；仅有管理权限时可显式覆盖当前草稿。</p><label v-if="preview.hasCodeConflict && preview.canOverwrite"><input v-model="overwrite" type="checkbox"> 覆盖 {{ preview.code }} 的草稿修订 {{ preview.existingDraftRevision }}</label><p v-if="preview.hasCodeConflict && !preview.canOverwrite" class="admin-flow-error">当前账号无权覆盖该流程。</p><ul v-if="preview.publishIssues.length"><li v-for="(issue, index) in preview.publishIssues" :key="index">{{ issue.message }}<small v-if="issue.elementId"> · {{ issue.elementId }}</small></li></ul><div class="admin-flow-import-fields"><label>分组<input v-model="importGroup" maxlength="128"></label><label>所属部门内部 ID<input v-model="importOwnerDepartmentId" maxlength="64" placeholder="组织管理员可留空"></label><label>运行期管理员内部 ID<input v-model="importAdministratorId" maxlength="64"></label><label>导入原因<input v-model="importReason" minlength="2" maxlength="200" required></label></div><button class="btn btn-primary" type="submit" :disabled="busy || preview.hasCodeConflict && (!preview.canOverwrite || !overwrite)">保存为草稿</button></template></form>
  </section>
</template>

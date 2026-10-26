<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ApiError } from '../api/http.ts'
import { adminSession, canReadAdminSection } from '../api/admin-session.ts'
import { createManagedFlow, getManagedFlow, listFlowDepartments, publishManagedFlow,
  saveManagedFlow } from '../api/admin-flow-management.ts'
import type { FlowDepartment, ManagedFlow } from '../api/admin-flow-management.ts'
import type { AppRoute } from '../model/routes.ts'
import type { FlowDefinition, FlowEdge, FlowNode } from '../model/flow.ts'
import { DESIGNER_TABS, TAB_TITLES } from '../model/navigation.ts'
import ApprovalFieldsEditor from '../designer/data/ApprovalFieldsEditor.vue'
import ApprovalDetailLayoutEditor from '../designer/data/ApprovalDetailLayoutEditor.vue'
import FlowCanvasEditor from '../designer/canvas/BpmnCanvasEditor.vue'
import NodeInspector from '../designer/workbench/NodeInspector.vue'
import EdgeInspector from '../designer/workbench/EdgeInspector.vue'
import FlowRulesPanel from '../designer/workbench/FlowRulesPanel.vue'
import FlowSimulationPanel from '../designer/workbench/FlowSimulationPanel.vue'
import FlowPrintPanel from '../designer/workbench/FlowPrintPanel.vue'
import UiDialog from '../components/ui/UiDialog.vue'
import UiIcon from '../components/ui/UiIcon.vue'
import { checkPublication } from '../designer/workbench/publication-check.ts'
import { commitDraft, createDraftHistory, editDraft, previewDraft, redoDraft, undoDraft } from '../designer/workbench/draft-history.ts'
import { copyFlow, createEmptyFlow, findEdge, findNode, flowTemplates, normalizeSample, replaceEdge, replaceNode } from '../designer/workbench/workbench-model.ts'
import { buildCSharpStartSnippet } from '../designer/workbench/integration-snippet.ts'
import type { DesignerIssue } from '../designer/workbench/workbench-model.ts'

/**
 * 工作台路由输入。
 */
const props = defineProps<{
  /**
   * 已解析的流程工作区地址。
   */
  route: AppRoute
}>()
/**
 * 当前管理会话是否有流程管理权限。
 */
const authorized = computed(() => canReadAdminSection(adminSession(), 'flows'))
/**
 * 当前流程编码，无参数时展示模板入口。
 */
const flowCode = computed(() => props.route.resourceId)
/**
 * 当前工作台页签。
 */
const activeTab = computed(() => props.route.tab || 'design')
/**
 * 当前流程草稿和唯一撤销历史。
 */
const history = ref(createDraftHistory(loadDraft(flowCode.value)))
/**
 * 最近一次从服务读取或保存的流程及其并发修订。
 */
const managed = ref<ManagedFlow | null>(null)
/**
 * 服务端确认编码不存在后才允许创建。
 */
const canCreate = ref(false)
/**
 * 读取失败时阻止把未知服务端状态当作新流程覆盖。
 */
const loadError = ref('')
/**
 * 是否正在读取或写入服务端草稿。
 */
const busy = ref(false)
/**
 * 当前页面是否有未保存的编辑。
 */
const dirty = ref(false)
/**
 * 分组或归属改动也必须先保存，才能按当前服务端修订发布。
 */
const metadataDirty = computed(() => !!managed.value && (
  (managed.value.group ?? '') !== group.value.trim()
  || (managed.value.ownerDepartmentId ?? '') !== ownerDepartmentId.value.trim()))
/**
 * 保存或发布时必须填写的审计原因。
 */
const reason = ref('')
/**
 * 将低频编辑的流程归属信息放在对话框中，释放设计空间。
 */
const metadataOpen = ref(false)
/**
 * 新流程的分组；已有流程沿用服务端元数据。
 */
const group = ref('')
/**
 * 新流程的直属所属部门内部 ID。
 */
const ownerDepartmentId = ref('')
/**
 * 当前人员能选择的直属所属部门。
 */
const departments = ref<FlowDepartment[]>([])
/**
 * 部门选项成功读取前不允许创建新流程。
 */
const departmentsLoaded = ref(false)
/**
 * 部门选项读取失败的可见信息。
 */
const departmentError = ref('')
/**
 * 当前请求序号，防止切换流程后的旧响应覆盖新草稿。
 */
let loadSequence = 0
/**
 * 当前流程草稿，供画布、属性面板和其他页签读取。
 */
const draft = computed(() => history.value.present)
/**
 * 已挂载的自由画布，用于从发布问题直接定位图形。
 */
const canvasEditor = ref<InstanceType<typeof FlowCanvasEditor> | null>(null)
/**
 * 从其他页签请求的画布元素定位，等画布真正挂载后执行。
 */
const pendingFocusId = ref<string | null>(null)
/**
 * 可撤销的步骤数。
 */
const pastCount = computed(() => history.value.past.length)
/**
 * 可重做的步骤数。
 */
const futureCount = computed(() => history.value.future.length)
/**
 * 最近一次发布预检问题。
 */
const issues = ref<DesignerIssue[]>([])
/**
 * 草稿状态说明。
 */
const status = ref('正在读取服务端草稿')
/**
 * 当前流程可复制的 SDK 接入示例。
 */
const integrationSnippet = ref('')
/**
 * 生成或复制代码时的反馈。
 */
const snippetStatus = ref('')
/**
 * 画布元素的全部问题，供图形组件直接标记。
 */
const canvasIssues = computed(() => issues.value.filter(issue => issue.elementId && (findNode(draft.value, issue.elementId) || findEdge(draft.value, issue.elementId))))

watch(canvasEditor, editor => {
  if (!editor || !pendingFocusId.value) return
  editor.focusElement(pendingFocusId.value)
  pendingFocusId.value = null
})

watch(flowCode, code => {
  // 工作台组件在流程间复用；切换流程时不能沿用上一份草稿和撤销栈。
  history.value = createDraftHistory(loadDraft(code))
  managed.value = null
  canCreate.value = false
  loadError.value = ''
  departments.value = []
  departmentsLoaded.value = false
  departmentError.value = ''
  dirty.value = false
  issues.value = []
  pendingFocusId.value = null
  status.value = code ? '正在读取服务端草稿' : '请选择流程模板'
  integrationSnippet.value = ''
  snippetStatus.value = ''
  if (code && authorized.value) {
    void loadManaged(code)
    void loadDepartments(code)
  }
}, { immediate: true })

watch(() => draft.value.code, () => {
  // 改动编码后旧示例不能再复制，必须按当前草稿重新生成。
  integrationSnippet.value = ''
  snippetStatus.value = ''
})

/**
 * 创建内置模板的独立副本；实际已有草稿随后从服务端读取。
 * @param code 流程编码。
 * @returns 可编辑的流程草稿。
 */
function loadDraft(code: string): FlowDefinition {
  if (!code) return createEmptyFlow()
  const template = flowTemplates.find(item => item.code === code)
  return template ? copyFlow(template.definition) : createEmptyFlow(code)
}
/**
 * 读取服务端草稿与发布指针；404 表示尚未创建的新流程。
 * @param code 当前路由里的流程编码。
 * @returns 读取操作完成。
 */
async function loadManaged(code: string): Promise<void> {
  const sequence = ++loadSequence
  busy.value = true
  loadError.value = ''
  try {
    const result = await getManagedFlow(code)
    if (sequence !== loadSequence) return
    managed.value = result
    canCreate.value = false
    group.value = result.group ?? ''
    ownerDepartmentId.value = result.ownerDepartmentId ?? ''
    if (result.draft) history.value = createDraftHistory(normalizeSample(result.draft))
    status.value = result.draft ? `服务端草稿修订 ${result.draftRevision}` : '尚无可编辑草稿'
  } catch (cause) {
    if (sequence !== loadSequence) return
    canCreate.value = cause instanceof ApiError && cause.status === 404
    if (!canCreate.value) loadError.value = cause instanceof Error ? cause.message : '读取服务端草稿失败'
    status.value = cause instanceof ApiError && cause.status === 404
      ? '新流程尚未保存到服务端' : cause instanceof Error ? cause.message : '读取服务端草稿失败'
  } finally {
    if (sequence === loadSequence) busy.value = false
  }
}
/**
 * 从服务端读取当前管理范围内的直属部门选项。
 * @param code 发起请求时的当前流程编码。
 * @returns 选项读取完成。
 */
async function loadDepartments(code: string): Promise<void> {
  departmentError.value = ''
  try {
    const result = await listFlowDepartments()
    if (flowCode.value !== code) return
    departments.value = result
    departmentsLoaded.value = true
  } catch (cause) {
    if (flowCode.value !== code) return
    departmentError.value = cause instanceof Error ? cause.message : '所属部门读取失败'
  }
}
/**
 * 应用一次编辑，并在编辑完成时记录可撤销历史。
 * @param next 新流程对象。
 * @param remember 是否把修改前的草稿加入历史。
 */
function updateDraft(next: FlowDefinition, remember = true): void {
  history.value = remember ? editDraft(history.value, next) : previewDraft(history.value, next)
  issues.value = []
  status.value = '草稿已修改，尚未保存'
  dirty.value = true
}
/**
 * 画布连续拖动只在提交时记录一次修改前的完整草稿。
 * @param previous 图形操作开始前的流程定义。
 */
function commitCanvasEdit(previous: FlowDefinition): void {
  history.value = commitDraft(history.value, previous)
  dirty.value = true
}
/**
 * 撤销最近一次编辑。
 */
function undo(): void {
  if (!pastCount.value) return
  history.value = undoDraft(history.value)
  issues.value = []
  status.value = '已撤销，尚未保存'
  dirty.value = true
}
/**
 * 重做最近一次撤销。
 */
function redo(): void {
  if (!futureCount.value) return
  history.value = redoDraft(history.value)
  issues.value = []
  status.value = '已重做，尚未保存'
  dirty.value = true
}
/**
 * 创建或按修订号保存服务端草稿，冲突时保留本页编辑。
 * @returns 保存操作完成。
 */
async function saveDraft(): Promise<void> {
  if (busy.value) return
  if (managed.value && !managed.value.draft) { status.value = '旧流程没有草稿，请先从已发布版本恢复草稿'; return }
  if (!managed.value && !canCreate.value) { status.value = '请先确认服务端流程状态'; return }
  if (!managed.value && !departmentsLoaded.value) { status.value = '请先读取可选所属部门'; return }
  if (reason.value.trim().length < 2) { status.value = '请填写至少两个字的审计原因'; return }
  if (managed.value && draft.value.code !== managed.value.code) {
    status.value = '已有流程的编码不能修改；请恢复原编码后保存'
    return
  }
  busy.value = true
  try {
    const metadata = { group: group.value.trim() || null,
      ownerDepartmentId: ownerDepartmentId.value.trim() || null,
      administratorId: managed.value?.administratorId ?? null }
    const result = managed.value
      ? await saveManagedFlow(managed.value.code, managed.value.draftRevision, draft.value, metadata, reason.value.trim())
      : await createManagedFlow(draft.value, metadata, reason.value.trim())
    managed.value = result
    dirty.value = false
    reason.value = ''
    status.value = `草稿已保存，修订 ${result.draftRevision}`
    if (result.code !== flowCode.value) location.hash = `#/designer/${encodeURIComponent(result.code)}/${activeTab.value}`
  } catch (cause) {
    status.value = cause instanceof Error ? cause.message : '保存草稿失败；当前编辑仍保留在页面'
  } finally {
    busy.value = false
  }
}
/**
 * 经本地预检后以双重 CAS 发布不可变版本。
 * @returns 发布操作完成。
 */
async function publish(): Promise<void> {
  if (!managed.value || busy.value) { status.value = '请先保存草稿'; return }
  if (dirty.value || metadataDirty.value) { status.value = '请先保存当前修改，再发布'; return }
  issues.value = checkPublication(draft.value)
  if (issues.value.length) { status.value = `发现 ${issues.value.length} 个发布问题`; return }
  if (reason.value.trim().length < 2) { status.value = '请填写至少两个字的发布原因'; return }
  busy.value = true
  try {
    const result = await publishManagedFlow(managed.value.code, managed.value.draftRevision,
      managed.value.publishedVersionId, reason.value.trim())
    managed.value = result
    reason.value = ''
    status.value = `已发布版本 ${result.publishedVersion}`
  } catch (cause) {
    status.value = cause instanceof Error ? cause.message : '发布失败；请检查服务端返回的问题'
  } finally {
    busy.value = false
  }
}
/**
 * 载入一个仓库自带的正式流程示例。
 * @param code 示例编码。
 */
function loadTemplate(code: string): void {
  const template = flowTemplates.find(item => item.code === code)
  if (!template) return
  const next = copyFlow(template.definition)
  next.code = flowCode.value || code
  updateDraft(next)
  location.hash = `#/designer/${encodeURIComponent(next.code)}/design`
}
/**
 * 预检流程发布条件，并把问题逐条定位到图形元素。
 */
function validatePublish(): void {
  issues.value = checkPublication(draft.value)
  status.value = issues.value.length ? `发现 ${issues.value.length} 个发布问题` : '本地发布预检通过；正式发布需服务端校验'
}
/**
 * 接收画布工具栏的检查结果，并同步到工作台问题列表。
 * @param result 画布静态检查发现的问题。
 */
function handleCanvasValidation(result: DesignerIssue[]): void {
  issues.value = result
  status.value = result.length ? `发现 ${result.length} 个发布问题` : '本地发布预检通过；正式发布需服务端校验'
}
/**
 * 在属性面板定位一条发布问题。
 * @param issue 要定位的问题。
 */
function focusIssue(issue: DesignerIssue): void {
  if (!issue.elementId) return
  const kind = findNode(draft.value, issue.elementId) ? 'node' : findEdge(draft.value, issue.elementId) ? 'edge' : null
  if (!kind) return
  if (canvasEditor.value) canvasEditor.value.focusElement(issue.elementId)
  else {
    // hashchange 晚于当前渲染轮次；用挂载监听处理跨页签定位。
    pendingFocusId.value = issue.elementId
    location.hash = `#/designer/${encodeURIComponent(flowCode.value)}/design`
  }
}
/**
 * 更新节点后保留选中状态。
 * @param node 编辑后的节点。
 */
function updateNode(node: FlowNode): void {
  updateDraft(replaceNode(draft.value, node))
}
/**
 * 更新连线后保留选中状态。
 * @param edge 编辑后的连线。
 */
function updateEdge(edge: FlowEdge): void {
  updateDraft(replaceEdge(draft.value, edge))
}
/**
 * 读取某个节点或连线的发布问题文本。
 * @param id 图形元素 ID。
 * @returns 问题说明数组。
 */
function issuesFor(id: string): string[] {
  return issues.value.filter(issue => issue.elementId === id).map(issue => issue.message)
}
/**
 * 生成当前流程的 C# SDK 发起示例，不包含接入应用密钥。
 */
function showIntegrationSnippet(): void {
  try {
    integrationSnippet.value = buildCSharpStartSnippet(draft.value.code)
    snippetStatus.value = '示例已生成；正式调用前请先发布流程并授权接入应用。'
  } catch (cause) {
    integrationSnippet.value = ''
    snippetStatus.value = cause instanceof Error ? cause.message : '无法生成示例代码。'
  }
}
/**
 * 将已生成的 C# 代码复制给业务系统开发人员。
 * @returns 复制操作完成。
 */
async function copyIntegrationSnippet(): Promise<void> {
  if (!integrationSnippet.value) return
  try {
    await navigator.clipboard.writeText(integrationSnippet.value)
    snippetStatus.value = '代码已复制；API Key 请放在业务系统环境变量中。'
  } catch {
    snippetStatus.value = '浏览器未允许复制，请直接选中下方代码。'
  }
}
</script>

<template>
  <div class="designer-page">
    <header class="designer-header">
      <div class="designer-heading"><a class="designer-back" href="#/admin/flows" aria-label="返回流程管理" title="返回流程管理"><UiIcon name="arrow" /></a><div class="designer-identity"><h1 id="page-title" tabindex="-1">{{ flowCode ? draft.name : '选择流程模板' }}</h1><p role="status">{{ flowCode ? `${draft.code} · ${status}` : '从常用流程出发，或创建空白自由画布。' }}</p></div></div>
      <div v-if="flowCode && authorized" class="designer-actions">
        <button type="button" class="btn" :disabled="!pastCount || busy" @click="undo">撤销</button><button type="button" class="btn" :disabled="!futureCount || busy" @click="redo">重做</button>
        <button type="button" class="btn" :disabled="busy" @click="metadataOpen = true">流程设置</button>
        <input v-model="reason" class="designer-reason" aria-label="审计原因" maxlength="200" placeholder="保存 / 发布原因" :disabled="busy" />
        <button type="button" class="btn" :disabled="busy" @click="saveDraft">保存草稿</button><button type="button" class="btn" :disabled="busy" @click="validatePublish">发布预检</button><button type="button" class="btn btn-primary" :disabled="busy || !managed" @click="publish">发布版本</button>
      </div>
    </header>
    <UiDialog :open="metadataOpen" title="流程设置" @close="metadataOpen = false">
      <div class="publish-form"><p>分组和所属部门的修改将在保存草稿后生效。</p><label>流程分组<input v-model="group" maxlength="100" placeholder="可选" :disabled="busy" /></label><label>所属部门<select v-model="ownerDepartmentId" :disabled="busy"><option value="">组织级（仅组织管理员）</option><option v-if="ownerDepartmentId && !departments.some(item => item.id === ownerDepartmentId)" :value="ownerDepartmentId">原所属部门</option><option v-for="department in departments" :key="department.id" :value="department.id">{{ department.name }}</option></select></label><p v-if="departmentError" role="alert">{{ departmentError }} <button type="button" class="btn" @click="loadDepartments(flowCode)">重试</button></p></div>
      <template #footer><button type="button" class="btn btn-primary" @click="metadataOpen = false">完成</button></template>
    </UiDialog>
    <template v-if="!flowCode"><div class="template-intro"><h2>从模板开始</h2><p>十类常用审批模板可直接复制修改；采购、报销和质量异常示例还展示金额分支、会签、返工与并行汇合。</p></div><div class="template-grid"><a v-for="template in flowTemplates" :key="template.code" class="template-card" :href="`#/designer/${template.code}/design`"><span>流程模板</span><h3>{{ template.name }}</h3><p>{{ template.description }}</p><small>{{ template.definition.canvas.nodes.length }} 个节点 · {{ template.definition.data.fields.length }} 个字段 →</small></a><a class="template-card blank" href="#/designer/NEW_FLOW/design"><span>空白流程</span><h3>从自由画布开始</h3><p>开始和结束节点已经放好，可在同一画布继续加入任意审批路线。</p><small>创建新流程 →</small></a></div></template>
    <div v-else-if="!authorized" class="designer-content"><p role="alert">请先以流程管理员身份登录，再读取或编辑流程。</p><a href="#/admin/flows">前往流程管理登录</a></div>
    <div v-else-if="loadError" class="designer-content" role="alert"><p>{{ loadError }}</p><button type="button" class="btn" @click="loadManaged(flowCode)">重新读取</button><a href="#/admin/flows">返回流程管理</a></div>
    <div v-else-if="busy && !managed && !canCreate" class="designer-content" role="status">正在读取服务端流程…</div>
    <div v-else-if="managed && !managed.draft" class="designer-content" role="status"><p>此流程尚无服务端草稿。请从已发布版本恢复草稿后再编辑，避免以空白画布覆盖旧流程。</p><a href="#/admin/flows">返回流程管理</a></div>
    <template v-else>
      <nav class="designer-tabs" aria-label="流程配置"><a v-for="tab in DESIGNER_TABS" :key="tab" :href="`#/designer/${encodeURIComponent(flowCode)}/${tab}`" :aria-current="activeTab === tab ? 'page' : undefined">{{ TAB_TITLES[tab] }}</a></nav>
      <div v-if="issues.length" class="publish-issues" role="alert"><div class="publish-issues-head"><strong>发布预检发现 {{ issues.length }} 个问题</strong><span>点击可定位到节点或连线</span></div><ul><li v-for="(issue, index) in issues" :key="index"><button v-if="issue.elementId && canvasIssues.includes(issue)" type="button" @click="focusIssue(issue)">{{ issue.message }} <small>定位：{{ issue.elementId }}</small></button><span v-else>{{ issue.message }}</span></li></ul></div>
      <div v-if="activeTab === 'design'" class="design-workspace">
        <FlowCanvasEditor ref="canvasEditor" :model-value="draft" :issues="issues" history-mode="external" @update:model-value="updateDraft($event, false)" @edit-committed="commitCanvasEdit" @undo="undo" @redo="redo" @validate="handleCanvasValidation">
          <template #inspector="{ node, edge }">
            <NodeInspector v-if="node" :node="node" :nodes="draft.canvas.nodes" :data="draft.data" :issues="issuesFor(node.id)" @update="updateNode" />
            <EdgeInspector v-else-if="edge" :key="edge.id" :edge="edge" :nodes="draft.canvas.nodes" :data="draft.data" :issues="issuesFor(edge.id)" @update="updateEdge" />
            <div v-else class="inspector-empty"><span>◇</span><h2>选择画布元素</h2><p>选中节点或连线后，在此设置审批规则、条件和字段权限。</p></div>
          </template>
        </FlowCanvasEditor>
      </div>
      <div v-else-if="activeTab === 'fields'" class="designer-content"><ApprovalFieldsEditor :data="draft.data" @update:data="updateDraft({ ...draft, data: $event })" /></div>
      <div v-else-if="activeTab === 'layout'" class="designer-content"><ApprovalDetailLayoutEditor :data="draft.data" :detail-layout="draft.detailLayout" @update:detail-layout="updateDraft({ ...draft, detailLayout: $event })" /></div>
      <div v-else-if="activeTab === 'rules'" class="designer-content"><FlowRulesPanel :definition="draft" @update="updateDraft" /><FlowSimulationPanel :definition="draft" :registered="!!managed" /><section class="template-switch"><h2>应用内置模板</h2><p>使用模板会替换当前页面的流程草稿；保存前仍可通过刷新恢复服务端草稿。</p><button v-for="template in flowTemplates" :key="template.code" type="button" class="btn" @click="loadTemplate(template.code)">{{ template.name }}</button></section><section class="template-switch integration-example"><h2>业务系统 C# 接入示例</h2><p>发布流程并授权接入应用后使用；服务地址和 API Key 从业务系统环境变量读取，代码不包含明文密钥。</p><button type="button" class="btn" @click="showIntegrationSnippet">生成 SDK 发起代码</button><button v-if="integrationSnippet" type="button" class="btn" @click="copyIntegrationSnippet">复制代码</button><p v-if="snippetStatus" role="status">{{ snippetStatus }}</p><pre v-if="integrationSnippet"><code>{{ integrationSnippet }}</code></pre></section></div>
      <div v-else-if="activeTab === 'print'" class="designer-content"><FlowPrintPanel :settings="draft.printSettings" :data="draft.data" @update="updateDraft({ ...draft, printSettings: $event })" /></div>
      <div v-else class="designer-content"><section class="version-panel"><span>版本管理</span><h2>{{ managed?.publishedVersion ? `当前发布版本 ${managed.publishedVersion}` : '尚未发布' }}</h2><p>草稿修订 {{ managed?.draftRevision ?? '尚未创建' }}。正式版本不可修改；保存草稿后可在上方发布新版本。</p><button type="button" class="btn btn-primary" @click="validatePublish">检查当前草稿</button></section></div>
    </template>
  </div>
</template>

<style scoped src="../styles/designer-workspace.css"></style>

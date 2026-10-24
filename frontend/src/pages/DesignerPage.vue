<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { AppRoute } from '../model/routes.ts'
import type { FlowDefinition, FlowEdge, FlowNode } from '../model/flow.ts'
import { DESIGNER_TABS, TAB_TITLES } from '../model/navigation.ts'
import ApprovalFieldsEditor from '../designer/data/ApprovalFieldsEditor.vue'
import ApprovalDetailLayoutEditor from '../designer/data/ApprovalDetailLayoutEditor.vue'
import FlowCanvasEditor from '../designer/canvas/BpmnCanvasEditor.vue'
import NodeInspector from '../designer/workbench/NodeInspector.vue'
import EdgeInspector from '../designer/workbench/EdgeInspector.vue'
import FlowRulesPanel from '../designer/workbench/FlowRulesPanel.vue'
import FlowPrintPanel from '../designer/workbench/FlowPrintPanel.vue'
import { checkPublication } from '../designer/workbench/publication-check.ts'
import { commitDraft, createDraftHistory, editDraft, previewDraft, redoDraft, undoDraft } from '../designer/workbench/draft-history.ts'
import { copyFlow, createEmptyFlow, findEdge, findNode, flowTemplates, normalizeSample, replaceEdge, replaceNode } from '../designer/workbench/workbench-model.ts'
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
 * 草稿存储前缀；仅表示当前浏览器里的设计草稿。
 */
const draftPrefix = 'approval-service-designer:'
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
const status = ref('本地草稿，尚未发布')
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
  if (draft.value.code === code) return
  history.value = createDraftHistory(loadDraft(code))
  issues.value = []
  pendingFocusId.value = null
  status.value = '本地草稿，尚未发布'
})

/**
 * 读取浏览器中的草稿；不存在时使用正式示例的本地副本。
 * @param code 流程编码。
 * @returns 可编辑的流程草稿。
 */
function loadDraft(code: string): FlowDefinition {
  if (!code) return createEmptyFlow()
  try {
    const saved = localStorage.getItem(draftPrefix + code)
    if (saved) return normalizeSample(JSON.parse(saved) as FlowDefinition)
  } catch {
    // 草稿无效或浏览器禁用存储时，继续使用只读示例的独立副本。
  }
  const template = flowTemplates.find(item => item.code === code)
  return template ? copyFlow(template.definition) : createEmptyFlow(code)
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
}
/**
 * 画布连续拖动只在提交时记录一次修改前的完整草稿。
 * @param previous 图形操作开始前的流程定义。
 */
function commitCanvasEdit(previous: FlowDefinition): void {
  history.value = commitDraft(history.value, previous)
}
/**
 * 撤销最近一次编辑。
 */
function undo(): void {
  if (!pastCount.value) return
  history.value = undoDraft(history.value)
  issues.value = []
  status.value = '已撤销，尚未保存'
}
/**
 * 重做最近一次撤销。
 */
function redo(): void {
  if (!futureCount.value) return
  history.value = redoDraft(history.value)
  issues.value = []
  status.value = '已重做，尚未保存'
}
/**
 * 保存当前浏览器内的设计草稿。
 */
function saveDraft(): void {
  try {
    localStorage.setItem(draftPrefix + draft.value.code, JSON.stringify(draft.value))
    status.value = '已保存为本地草稿，尚未发布'
    if (draft.value.code !== flowCode.value) location.hash = `#/designer/${encodeURIComponent(draft.value.code)}/${activeTab.value}`
  } catch {
    status.value = '浏览器无法保存草稿；可继续编辑，但离开页面可能丢失改动'
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
</script>

<template>
  <div class="designer-page">
    <header class="designer-header"><div class="designer-breadcrumb"><a href="#/designer">流程设计</a><span>/</span><span>{{ flowCode ? draft.name : '流程模板' }}</span></div><div class="designer-title"><div><span class="eyebrow">统一自由画布</span><h1 id="page-title" tabindex="-1">{{ flowCode ? draft.name : '选择流程模板' }}</h1><p>{{ flowCode ? `${draft.code} · ${status}` : '从常用流程出发，或创建空白自由画布。' }}</p></div><div v-if="flowCode" class="designer-actions"><button type="button" class="btn" :disabled="!pastCount" @click="undo">撤销</button><button type="button" class="btn" :disabled="!futureCount" @click="redo">重做</button><button type="button" class="btn" @click="saveDraft">保存草稿</button><button type="button" class="btn btn-primary" @click="validatePublish">发布预检</button></div></div></header>
    <template v-if="!flowCode"><div class="template-intro"><h2>从模板开始</h2><p>三个示例来自后端正式流程定义，覆盖金额分支、会签、返工和并行汇合。</p></div><div class="template-grid"><a v-for="template in flowTemplates" :key="template.code" class="template-card" :href="`#/designer/${template.code}/design`"><span>流程模板</span><h3>{{ template.name }}</h3><p>{{ template.description }}</p><small>{{ template.definition.canvas.nodes.length }} 个节点 · {{ template.definition.data.fields.length }} 个字段 →</small></a><a class="template-card blank" href="#/designer/NEW_FLOW/design"><span>空白流程</span><h3>从自由画布开始</h3><p>开始和结束节点已经放好，可在同一画布继续加入任意审批路线。</p><small>创建新流程 →</small></a></div></template>
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
      <div v-else-if="activeTab === 'rules'" class="designer-content"><FlowRulesPanel :definition="draft" @update="updateDraft" /><section class="template-switch"><h2>应用内置模板</h2><p>使用模板会替换当前流程草稿；已保存的本地草稿可通过刷新恢复。</p><button v-for="template in flowTemplates" :key="template.code" type="button" class="btn" @click="loadTemplate(template.code)">{{ template.name }}</button></section></div>
      <div v-else-if="activeTab === 'print'" class="designer-content"><FlowPrintPanel :settings="draft.printSettings" :data="draft.data" @update="updateDraft({ ...draft, printSettings: $event })" /></div>
      <div v-else class="designer-content"><section class="version-panel"><span>版本管理</span><h2>当前仅有本地草稿</h2><p>正式版本由服务端发布并保存。管理接口接入后，此处会展示不可修改的发布版本和差异。</p><button type="button" class="btn btn-primary" @click="validatePublish">先检查当前草稿</button></section></div>
    </template>
  </div>
</template>

<style scoped>
.designer-page { min-height: calc(100vh - 56px); background: #f7f9fd; }
.designer-header { padding: 20px 28px 23px; border-bottom: 1px solid var(--line); background: #fff; }
.designer-breadcrumb { display: flex; gap: 10px; color: var(--muted); font-size: 12px; }
.designer-breadcrumb a { color: inherit; }
.designer-title { display: flex; align-items: end; justify-content: space-between; gap: 22px; margin-top: 17px; }
.designer-title h1 { margin: 3px 0; font-size: 24px; line-height: 1.3; }
.designer-title p { color: var(--muted); }
.eyebrow { color: var(--blue); font-size: 11px; font-weight: 700; letter-spacing: .1em; }
.designer-actions { display: flex; gap: 8px; flex-wrap: wrap; }
.designer-tabs { display: flex; gap: 24px; padding: 0 28px; min-height: 54px; overflow-x: auto; border-bottom: 1px solid var(--line); background: #fff; }
.designer-tabs a { display: inline-flex; align-items: center; white-space: nowrap; border-bottom: 3px solid transparent; color: var(--muted); }
.designer-tabs a[aria-current] { border-color: var(--blue); color: var(--blue); font-weight: 700; }
.designer-content { padding: 24px 28px; }
.design-workspace { min-width: 0; padding: 18px; }
.design-workspace :deep(.flow-canvas-layout) { grid-template-columns: 158px minmax(0, 1fr) 340px; }
.inspector-empty { text-align: center; padding: 90px 22px; color: var(--muted); }
.inspector-empty span { font-size: 42px; color: #8fa8ce; }
.inspector-empty h2 { margin: 8px 0; color: var(--ink); font-size: 16px; }
.publish-issues { margin: 18px 18px 0; padding: 14px 18px; border: 1px solid #edc2c6; border-radius: 5px; background: #fff7f7; }
.publish-issues-head { display: flex; justify-content: space-between; gap: 10px; color: #96323c; }
.publish-issues-head span { font-size: 12px; }
.publish-issues ul { display: grid; gap: 5px; margin: 10px 0 0; padding-left: 20px; }
.publish-issues button { padding: 0; border: 0; background: none; color: #96323c; text-align: left; cursor: pointer; }
.publish-issues small { margin-left: 6px; color: var(--muted); }
.template-intro { padding: 28px 28px 12px; }
.template-intro h2 { font-size: 19px; }
.template-intro p { margin-top: 7px; color: var(--muted); }
.template-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px; padding: 12px 28px 28px; }
.template-card { display: grid; align-content: space-between; min-height: 180px; padding: 20px; border: 1px solid var(--line); border-radius: 7px; background: #fff; color: var(--ink); }
.template-card:hover { border-color: var(--blue); box-shadow: 0 8px 24px #1b4e9620; }
.template-card span { color: var(--blue); font-size: 11px; font-weight: 700; }
.template-card h3 { font-size: 18px; }
.template-card p { color: var(--muted); }
.template-card small { color: var(--blue); }
.template-card.blank { border-style: dashed; }
.template-switch, .version-panel { max-width: 960px; margin-top: 18px; padding: 22px; border: 1px solid var(--line); border-radius: 6px; background: #fff; }
.template-switch h2, .version-panel h2 { font-size: 16px; }
.template-switch p, .version-panel p { margin: 9px 0 15px; color: var(--muted); }
.template-switch .btn { margin: 0 8px 8px 0; }
.version-panel span { color: var(--blue); font-size: 11px; font-weight: 700; }
@media (max-width: 1150px) { .template-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .design-workspace :deep(.flow-canvas-layout) { grid-template-columns: 120px minmax(0, 1fr) 280px; } }
@media (max-width: 920px) { .designer-title { align-items: start; flex-direction: column; } }
@media (max-width: 720px) { .design-workspace :deep(.flow-canvas-layout) { grid-template-columns: 74px minmax(0, 1fr); grid-template-rows: minmax(320px, 1fr) 240px; } .design-workspace :deep(.flow-canvas-inspector) { display: block; grid-column: 1 / -1; grid-row: 2; border-left: 0; border-top: 1px solid var(--line); } }
@media (max-width: 620px) { .designer-header { padding: 18px; } .designer-tabs { padding: 0 18px; gap: 18px; } .designer-content { padding: 16px; } .design-workspace { padding: 12px; } .template-grid { grid-template-columns: 1fr; padding: 12px 18px; } .template-intro { padding: 22px 18px 8px; } }
</style>

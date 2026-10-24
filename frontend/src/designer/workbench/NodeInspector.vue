<script setup lang="ts">
import { computed, ref } from 'vue'
import type { FlowApprovalSettings, FlowAssignee, FlowAutomationSettings, FlowDataDefinition, FlowNode, FlowSubprocessSettings } from '../../model/flow.ts'
import { fieldReferences } from '../data/data-model.ts'
import NodeFieldPermissionsEditor from '../data/NodeFieldPermissionsEditor.vue'
import { actionsForNodeType, approvalActions, assigneeSources, defaultActionsForNodeType, nodeLabels } from './rule-options.ts'
import { defaultApproval } from './workbench-model.ts'

/**
 * 节点属性面板的输入。
 */
const props = defineProps<{
  /**
   * 当前选中的节点。
   */
  node: FlowNode
  /**
   * 全部节点，用于提示配对网关。
   */
  nodes: FlowNode[]
  /**
   * 审批数据定义，用于人员字段与权限。
   */
  data: FlowDataDefinition
  /**
   * 当前节点的发布问题。
   */
  issues: string[]
}>()
/**
 * 节点属性变化。
 */
const emit = defineEmits<{
  /**
   * 返回新的节点对象。
   */
  update: [value: FlowNode]
}>()
/**
 * 需要人工指定处理人的节点。
 */
const human = computed(() => ['approval', 'handling', 'cc'].includes(props.node.type))
/**
 * 分叉或汇合网关。
 */
const gateway = computed(() => props.node.type.endsWith('Gateway'))
/**
 * 可供人员来源引用的字段。
 */
const fields = computed(() => fieldReferences(props.data))
/**
 * 同类型反方向网关，供设计人员选择配对。
 */
const pairingCandidates = computed(() => props.nodes.filter(node => node.id !== props.node.id && node.type === props.node.type && node.gatewayDirection !== props.node.gatewayDirection))
/**
 * 节点的审批规则，缺失时使用安全默认值。
 */
const approval = computed(() => ({
  ...defaultApproval,
  ...props.node.approval,
  allowedActions: (props.node.approval?.allowedActions ?? defaultActionsForNodeType(props.node.type))
    .filter(action => actionsForNodeType(props.node.type).includes(action)),
}))
/**
 * 按节点职责展示可用操作；办理不能同意或拒绝，抄送不产生待办操作。
 */
const availableActions = computed(() => {
  const allowed = new Set(actionsForNodeType(props.node.type))
  return approvalActions.filter(action => allowed.has(action.value))
})
/**
 * 票签权重输入错误，避免把不合法 JSON 写进草稿。
 */
const voteError = ref('')

/**
 * 更新一个节点顶层属性。
 * @param key 要修改的属性。
 * @param value 新值。
 */
function setNode(key: 'name' | 'gatewayPairId' | 'gatewayDirection', value: string): void {
  emit('update', { ...props.node, [key]: value })
}
/**
 * 更新处理人来源设置。
 * @param key 配置属性。
 * @param value 输入值。
 */
function setAssignee(key: keyof FlowAssignee, value: string | number | boolean | null): void {
  emit('update', { ...props.node, assignee: { source: props.node.assignee?.source ?? 'member', ...props.node.assignee, [key]: value } })
}
/**
 * 更新审批规则。
 * @param key 规则属性。
 * @param value 新值。
 */
function setApproval(key: keyof FlowApprovalSettings, value: string | number | boolean | null | string[] | Record<string, number>): void {
  emit('update', { ...props.node, approval: { ...approval.value, [key]: value } })
}
/**
 * 切换节点允许的操作。
 * @param action 操作编码。
 * @param enabled 是否允许。
 */
function setAction(action: string, enabled: boolean): void {
  const available = new Set<string>(availableActions.value.map(item => item.value))
  const selected = new Set<string>(approval.value.allowedActions.filter(item => available.has(item)))
  if (enabled) selected.add(action)
  else selected.delete(action)
  setApproval('allowedActions', [...selected])
}
/**
 * 检查票签权重对象后更新节点。
 * @param text 权重 JSON 文本。
 */
function setVoteWeights(text: string): void {
  try {
    const parsed: unknown = JSON.parse(text)
    if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object' || Object.values(parsed).some(value => typeof value !== 'number' || !Number.isFinite(value) || value <= 0)) throw new Error('权重应为人员 ID 到正数的对象。')
    voteError.value = ''
    setApproval('voteWeights', parsed as Record<string, number>)
  } catch {
    voteError.value = '票签权重请输入 JSON 对象，例如 {"user-1": 2}。'
  }
}
/**
 * 更新子流程调用设置。
 * @param key 配置属性。
 * @param value 新值。
 */
function setSubprocess(key: keyof FlowSubprocessSettings, value: string | boolean | Record<string, string>): void {
  emit('update', { ...props.node, subprocess: { flowCode: '', waitForCompletion: true, dataMappings: {}, ...props.node.subprocess, [key]: value } })
}
/**
 * 更新自动节点设置。
 * @param key 配置属性。
 * @param value 新值。
 */
function setAutomation(key: keyof FlowAutomationSettings, value: string | boolean): void {
  emit('update', { ...props.node, automation: { actionCode: '', escalateToAdministrator: true, ...props.node.automation, [key]: value } })
}
/**
 * 把子流程字段映射从逐行文本转换为键值对。
 * @param text 每行一个“子流程字段=当前字段”。
 */
function setMappings(text: string): void {
  const entries = text.split(/\r?\n/).map(line => line.split('=')).filter(parts => parts.length === 2 && parts[0]?.trim() && parts[1]?.trim())
  setSubprocess('dataMappings', Object.fromEntries(entries.map(parts => [parts[0]!.trim(), parts[1]!.trim()])))
}
</script>

<template>
  <div class="inspector">
    <header class="inspector-head"><span class="kind">{{ nodeLabels[node.type] }}</span><h2>节点属性</h2><small>{{ node.id }}</small></header>
    <div v-if="issues.length" class="issue-box" role="alert"><strong>发布检查</strong><p v-for="issue in issues" :key="issue">{{ issue }}</p></div>
    <label class="form-row">节点名称<input :value="node.name" @input="setNode('name', ($event.target as HTMLInputElement).value)" /></label>
    <p v-if="node.type === 'start'" class="hint">业务系统通过接口发起。流程可用范围在“流程规则”页设置。</p>
    <p v-if="node.type === 'end'" class="hint">流程到达这里后结束，不需要业务系统在审批中心填写单据。</p>
    <template v-if="gateway">
      <h3>路线与汇合</h3>
      <label class="form-row">网关方向<select :value="node.gatewayDirection ?? ''" @change="setNode('gatewayDirection', ($event.target as HTMLSelectElement).value)"><option value="">选择方向</option><option value="split">分叉</option><option value="join">汇合</option></select></label>
      <label class="form-row">配对 ID<input :value="node.gatewayPairId ?? ''" list="gateway-pairs" placeholder="例如：purchase-review" @input="setNode('gatewayPairId', ($event.target as HTMLInputElement).value)" /></label>
      <datalist id="gateway-pairs"><option v-for="candidate in pairingCandidates" :key="candidate.id" :value="candidate.gatewayPairId || candidate.id" /></datalist>
      <p class="hint">{{ node.type === 'exclusiveGateway' ? '排他分支按优先级只走首个命中条件；返工回路可不配汇合。' : node.type === 'inclusiveGateway' ? '包容分支会走全部命中的条件，必须与同类型汇合网关配对。' : '并行分支会同时走所有出线，汇合会等待全部分支。' }} 条件与兜底在线上配置。</p>
    </template>
    <template v-if="human">
      <h3>找处理人</h3>
      <label class="form-row">来源<select :value="node.assignee?.source ?? ''" @change="setAssignee('source', ($event.target as HTMLSelectElement).value)"><option value="">选择来源</option><option v-for="source in assigneeSources" :key="source.value" :value="source.value">{{ source.label }}</option></select></label>
      <label v-if="['member','role','position','grade','divisionLeader','matrix','businessApi'].includes(node.assignee?.source ?? '')" class="form-row">来源编码<input :value="node.assignee?.value ?? ''" placeholder="成员、角色、矩阵或接口编码" @input="setAssignee('value', ($event.target as HTMLInputElement).value)" /></label>
      <label v-if="['dataPerson','dataDepartmentHead'].includes(node.assignee?.source ?? '')" class="form-row">业务字段<select :value="node.assignee?.fieldKey ?? ''" @change="setAssignee('fieldKey', ($event.target as HTMLSelectElement).value)"><option value="">选择字段</option><option v-for="field in fields" :key="field.key" :value="field.key">{{ field.label }}</option></select></label>
      <label v-if="['manager','managerChain','departmentHead'].includes(node.assignee?.source ?? '')" class="form-row">上级层级<input type="number" min="1" :value="node.assignee?.level ?? 1" @change="setAssignee('level', Number(($event.target as HTMLInputElement).value))" /></label>
      <label v-if="node.assignee?.source === 'role'" class="check-row"><input type="checkbox" :checked="node.assignee?.limitToInitiatingDepartment ?? false" @change="setAssignee('limitToInitiatingDepartment', ($event.target as HTMLInputElement).checked)" />只在发起部门内查找该角色</label>
      <label v-if="node.assignee?.source === 'managerChain'" class="form-row">截止职级编码<input :value="node.assignee?.stopAtGrade ?? ''" placeholder="留空则只按上级层级截止" @change="setAssignee('stopAtGrade', ($event.target as HTMLInputElement).value.trim() || null)" /></label>
      <h3>{{ node.type === 'cc' ? '抄送规则' : '审批规则' }}</h3>
      <template v-if="node.type !== 'cc'">
        <label class="form-row">多人方式<select :value="approval.mode" @change="setApproval('mode', ($event.target as HTMLSelectElement).value)"><option value="sequential">依次审批</option><option value="all">会签</option><option value="any">或签</option><option value="vote">票签</option></select></label>
        <label v-if="approval.mode === 'vote'" class="form-row">通过比例（0–1）<input type="number" min="0" max="1" step="0.05" :value="approval.voteThreshold ?? 0.5" @change="setApproval('voteThreshold', Number(($event.target as HTMLInputElement).value))" /></label>
        <label v-if="approval.mode === 'vote'" class="form-row">票签权重（JSON）<textarea rows="3" :value="JSON.stringify(approval.voteWeights)" @change="setVoteWeights(($event.target as HTMLTextAreaElement).value)" /></label><p v-if="voteError" class="input-error" role="alert">{{ voteError }}</p>
        <label v-if="approval.mode === 'all'" class="form-row">有人拒绝时<select :value="approval.rejectBehavior" @change="setApproval('rejectBehavior', ($event.target as HTMLSelectElement).value)"><option value="immediate">立即结束</option><option value="waitAll">等全部审完</option></select></label>
      </template>
      <label class="form-row">找不到处理人<select :value="approval.missingAssigneeBehavior" @change="setApproval('missingAssigneeBehavior', ($event.target as HTMLSelectElement).value)"><option value="block">阻止发起</option><option value="approve">自动通过</option><option value="administrator">流程管理员</option><option value="specified">指定人员</option></select></label>
      <label v-if="approval.missingAssigneeBehavior === 'specified'" class="form-row">指定人员 ID<input :value="approval.missingAssigneeUserId ?? ''" placeholder="输入组织人员 ID" @change="setApproval('missingAssigneeUserId', ($event.target as HTMLInputElement).value.trim() || null)" /></label>
      <label class="form-row">处理人就是发起人<select :value="approval.selfApprovalBehavior" @change="setApproval('selfApprovalBehavior', ($event.target as HTMLSelectElement).value)"><option value="skip">自动跳过</option><option value="self">本人审批</option><option value="manager">转交直属上级</option></select></label>
      <label class="form-row">重复出现同一人<select :value="approval.duplicateAssigneeBehavior" @change="setApproval('duplicateAssigneeBehavior', ($event.target as HTMLSelectElement).value)"><option value="adjacentOnce">相邻只审一次</option><option value="once">全流程只审一次</option><option value="everyTime">每次都审</option></select></label>
      <label class="form-row">处理人离职停用<select :value="approval.inactiveAssigneeBehavior" @change="setApproval('inactiveAssigneeBehavior', ($event.target as HTMLSelectElement).value)"><option value="manager">转直属上级</option><option value="administrator">转流程管理员</option></select></label>
      <label class="form-row">处理时限（分钟）<input type="number" min="1" :value="approval.dueMinutes ?? ''" placeholder="留空则不启用" @change="setApproval('dueMinutes', ($event.target as HTMLInputElement).value ? Number(($event.target as HTMLInputElement).value) : null)" /></label>
      <label class="check-row"><input type="checkbox" :checked="approval.commentRequired" @change="setApproval('commentRequired', ($event.target as HTMLInputElement).checked)" />审批意见必填</label>
      <label v-if="node.type === 'cc'" class="check-row"><input type="checkbox" :checked="approval.readReceiptRequired" @change="setApproval('readReceiptRequired', ($event.target as HTMLInputElement).checked)" />要求确认已阅</label>
      <template v-if="availableActions.length"><h3>允许的操作</h3><div class="action-grid"><label v-for="action in availableActions" :key="action.value" class="check-row"><input type="checkbox" :checked="approval.allowedActions.includes(action.value)" @change="setAction(action.value, ($event.target as HTMLInputElement).checked)" />{{ action.label }}</label></div></template>
      <NodeFieldPermissionsEditor :data="data" :permissions="node.fieldPermissions" @update:permissions="emit('update', { ...node, fieldPermissions: $event })" />
    </template>
    <template v-if="node.type === 'subprocess'"><h3>子流程</h3><label class="form-row">目标流程编码<input :value="node.subprocess?.flowCode ?? ''" @input="setSubprocess('flowCode', ($event.target as HTMLInputElement).value)" /></label><label class="check-row"><input type="checkbox" :checked="node.subprocess?.waitForCompletion ?? true" @change="setSubprocess('waitForCompletion', ($event.target as HTMLInputElement).checked)" />等待子流程结束</label><label class="form-row">字段映射<textarea rows="5" placeholder="子流程字段=当前流程字段，每行一条" :value="Object.entries(node.subprocess?.dataMappings ?? {}).map(([target, source]) => `${target}=${source}`).join('\n')" @change="setMappings(($event.target as HTMLTextAreaElement).value)" /></label></template>
    <template v-if="node.type === 'automatic'"><h3>自动动作</h3><label class="form-row">动作编码<input :value="node.automation?.actionCode ?? ''" placeholder="由接入应用配置的动作编码" @input="setAutomation('actionCode', ($event.target as HTMLInputElement).value)" /></label><label class="check-row"><input type="checkbox" :checked="node.automation?.escalateToAdministrator ?? true" @change="setAutomation('escalateToAdministrator', ($event.target as HTMLInputElement).checked)" />重试耗尽后交管理员</label></template>
  </div>
</template>

<style scoped>
.inspector{display:grid;gap:13px;padding:18px 16px}.inspector-head{display:grid;gap:2px;padding-bottom:12px;border-bottom:1px solid var(--line)}.inspector-head h2{font-size:17px}.inspector-head small{color:var(--muted);font:11px Consolas,monospace}.kind{color:var(--blue);font-size:11px;font-weight:700}.inspector h3{margin-top:9px;padding-top:12px;border-top:1px solid var(--line);font-size:13px}.form-row{display:grid;gap:5px;font-size:12px;font-weight:600}.form-row input,.form-row select,.form-row textarea{width:100%;min-height:34px;padding:6px 9px;border:1px solid #cbd5e1;border-radius:4px;background:#fff;color:var(--ink);font-weight:400}.form-row textarea{resize:vertical}.check-row{display:flex;align-items:center;gap:7px;font-size:12px}.check-row input{accent-color:var(--blue)}.action-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px}.hint{color:var(--muted);font-size:12px;line-height:1.6}.issue-box{padding:10px;border:1px solid #f0c8ca;border-radius:4px;background:#fff6f6;color:#9b3540}.issue-box p{margin-top:6px;font-size:12px}.input-error{color:var(--danger);font-size:12px}
</style>

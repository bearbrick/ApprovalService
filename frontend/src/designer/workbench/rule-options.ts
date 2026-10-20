import type { FlowNodeType } from '../../model/flow.ts'

/**
 * 处理人来源，与流程契约中的来源编码保持一致。
 */
export const assigneeSources = [
  { value: 'member', label: '指定成员' }, { value: 'role', label: '角色' },
  { value: 'position', label: '岗位' }, { value: 'grade', label: '职级' },
  { value: 'manager', label: '直属上级' }, { value: 'managerChain', label: '连续多级上级' },
  { value: 'departmentHead', label: '部门负责人' }, { value: 'dataDepartmentHead', label: '业务数据中的部门负责人' },
  { value: 'divisionLeader', label: '分管领导' }, { value: 'dataPerson', label: '业务数据中的人员' },
  { value: 'matrix', label: '审批矩阵' }, { value: 'businessApi', label: '业务系统接口' },
  { value: 'previousSelection', label: '上一步指定' }, { value: 'initiatorSelection', label: '发起人自选' },
  { value: 'initiator', label: '发起人本人' },
] as const

/**
 * 审批人可执行的操作；按钮能否使用仍须由服务端校验。
 */
export const approvalActions = [
  { value: 'approve', label: '同意' }, { value: 'reject', label: '拒绝' },
  { value: 'return', label: '退回' }, { value: 'transfer', label: '转交' },
  { value: 'addSigner', label: '加签' }, { value: 'removeSigner', label: '减签' },
  { value: 'consult', label: '征询意见' }, { value: 'requestSupplement', label: '要求补充' },
  { value: 'cc', label: '抄送' }, { value: 'withdrawApproval', label: '撤销同意' },
  { value: 'comment', label: '评论与 @人' }, { value: 'saveDraft', label: '暂存' },
  { value: 'batchApprove', label: '批量同意' }, { value: 'submit', label: '办理提交' },
] as const

/**
 * 按人工节点职责返回能启用的操作。
 * @param type 节点类型。
 * @returns 该节点可用的操作编码。
 */
export function actionsForNodeType(type: FlowNodeType): string[] {
  if (type === 'cc') return []
  if (type === 'handling') return approvalActions.filter(action => !['approve', 'reject', 'withdrawApproval', 'batchApprove'].includes(action.value)).map(action => action.value)
  return approvalActions.filter(action => action.value !== 'submit').map(action => action.value)
}

/**
 * 人工节点尚未设置审批规则时使用符合职责的默认操作。
 * @param type 节点类型。
 * @returns 默认允许的操作编码。
 */
export function defaultActionsForNodeType(type: FlowNodeType): string[] {
  if (type === 'handling') return ['submit']
  if (type === 'cc') return []
  return ['approve', 'reject']
}

/**
 * 人工节点类型的展示名称。
 */
export const nodeLabels: Readonly<Record<string, string>> = {
  start: '开始', end: '结束', approval: '审批', handling: '办理', cc: '抄送',
  exclusiveGateway: '排他网关', inclusiveGateway: '包容网关', parallelGateway: '并行网关',
  subprocess: '子流程', automatic: '自动节点',
}

/**
 * 与后端 BpmnFlowCodec 对应的浏览器 BPMN 导入导出；完整审批配置保存在本服务扩展里。
 */
import { flowSchemaVersion } from '../../model/flow.ts'
import type { FlowDefinition, FlowEdge, FlowNode } from '../../model/flow.ts'

/**
 * BPMN 标准模型命名空间。
 */
const bpmnNamespace = 'http://www.omg.org/spec/BPMN/20100524/MODEL'
/**
 * 审批服务扩展命名空间。
 */
const approvalNamespace = 'urn:approvalservice:flow:1'
/**
 * 最大导入文件字符数，与后端保持一致。
 */
const maxFileLength = 4 * 1024 * 1024

/**
 * 生成与后端属性声明顺序一致的非空 JSON 对象。
 * @param flow 前端流程定义。
 * @returns 用于跨端互导的规范对象。
 */
function canonicalFlow(flow: FlowDefinition): object {
  const omitNull = (value: Record<string, unknown>): Record<string, unknown> => Object.fromEntries(Object.entries(value).filter(([, item]) => item !== null && item !== undefined))
  const assignee = (value: FlowNode['assignee']): object | undefined => value ? omitNull({ source: value.source ?? '', value: value.value, fieldKey: value.fieldKey, level: value.level, limitToInitiatingDepartment: value.limitToInitiatingDepartment ?? false, stopAtGrade: value.stopAtGrade }) : undefined
  const approval = (value: FlowNode['approval']): object | undefined => value ? omitNull({ mode: value.mode ?? 'sequential', voteThreshold: value.voteThreshold, voteWeights: value.voteWeights ?? {}, rejectBehavior: value.rejectBehavior ?? 'immediate', missingAssigneeBehavior: value.missingAssigneeBehavior ?? 'block', missingAssigneeUserId: value.missingAssigneeUserId, selfApprovalBehavior: value.selfApprovalBehavior ?? 'skip', duplicateAssigneeBehavior: value.duplicateAssigneeBehavior ?? 'adjacentOnce', inactiveAssigneeBehavior: value.inactiveAssigneeBehavior ?? 'manager', allowedActions: value.allowedActions ?? [], commentRequired: value.commentRequired ?? false, readReceiptRequired: value.readReceiptRequired ?? false, dueMinutes: value.dueMinutes }) : undefined
  const nodes = flow.canvas.nodes.map(node => omitNull({ id: node.id ?? '', type: node.type ?? '', name: node.name ?? '', x: node.x ?? 0, y: node.y ?? 0, gatewayPairId: node.gatewayPairId, gatewayDirection: node.gatewayDirection, assignee: assignee(node.assignee), approval: approval(node.approval), subprocess: node.subprocess ? { flowCode: node.subprocess.flowCode ?? '', waitForCompletion: node.subprocess.waitForCompletion ?? true, dataMappings: node.subprocess.dataMappings ?? {} } : undefined, automation: node.automation ? { actionCode: node.automation.actionCode ?? '', escalateToAdministrator: node.automation.escalateToAdministrator ?? true } : undefined, fieldPermissions: (node.fieldPermissions ?? []).map(permission => ({ fieldKey: permission.fieldKey ?? '', access: permission.access ?? 'readOnly' })) }))
  const edges = flow.canvas.edges.map(edge => omitNull({ id: edge.id ?? '', sourceId: edge.sourceId ?? '', targetId: edge.targetId ?? '', priority: edge.priority ?? 0, isDefault: edge.isDefault ?? false, condition: edge.condition }))
  const fields = (items: FlowDefinition['data']['fields']): object[] => items.map(field => omitNull({ key: field.key ?? '', name: field.name ?? '', type: field.type ?? 'text', required: field.required ?? false, formula: field.formula, approverInput: field.approverInput ?? false, businessUpdateAllowed: field.businessUpdateAllowed ?? false, children: fields(field.children ?? []) }))
  return { schemaVersion: flow.schemaVersion ?? flowSchemaVersion, code: flow.code ?? '', name: flow.name ?? '', canvas: { nodes, edges }, data: { fields: fields(flow.data?.fields ?? []), summaryFields: flow.data?.summaryFields ?? [] }, detailLayout: omitNull({ mode: flow.detailLayout?.mode ?? 'auto', groups: (flow.detailLayout?.groups ?? []).map(group => ({ title: group.title ?? '', columns: group.columns ?? 2, items: (group.items ?? []).map(item => ({ fieldKey: item.fieldKey ?? '', columnSpan: item.columnSpan ?? 1, detailColumnWidths: item.detailColumnWidths ?? {}, showMoneyUppercase: item.showMoneyUppercase ?? false })) })), embedPageCode: flow.detailLayout?.embedPageCode }), printSettings: { orientation: flow.printSettings?.orientation ?? 'portrait', fieldKeys: flow.printSettings?.fieldKeys ?? [], showComments: flow.printSettings?.showComments ?? true, showSignatures: flow.printSettings?.showSignatures ?? true, includeReceiptPages: flow.printSettings?.includeReceiptPages ?? false, showLogo: flow.printSettings?.showLogo ?? true, showQrCode: flow.printSettings?.showQrCode ?? true, showWatermark: flow.printSettings?.showWatermark ?? false }, allowedClientApps: flow.allowedClientApps ?? [], allowedInitiatorDepartments: flow.allowedInitiatorDepartments ?? [], allowedInitiatorRoles: flow.allowedInitiatorRoles ?? [], isConfidential: flow.isConfidential ?? false, withdrawBeforeFirstDecision: flow.withdrawBeforeFirstDecision ?? false, returnMode: flow.returnMode ?? 'restart' }
}

/**
 * XML 属性与文本转义，避免流程名称和条件破坏文件结构。
 * @param value 原始文本。
 * @returns 已转义的 XML 文本。
 */
function escapeXml(value: string): string {
  return value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&apos;')
}

/**
 * 与后端一致，将标识 UTF-8 字节写为大写十六进制 XML ID。
 * @param prefix 类别前缀。
 * @param value 原始标识。
 * @returns XML ID。
 */
function xmlId(prefix: string, value: string): string {
  return `${prefix}_${[...new TextEncoder().encode(value)].map(byte => byte.toString(16).padStart(2, '0').toUpperCase()).join('')}`
}

/**
 * 把 UTF-8 字节编码为 Base64，避免直接对中文调用 btoa。
 * @param bytes 待编码字节。
 * @returns Base64 文本。
 */
function base64(bytes: Uint8Array): string {
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary)
}

/**
 * 将流程定义转成后端一致的 JSON 文本。
 * @param flow 流程定义。
 * @returns UTF-8 JSON 原文。
 */
function flowJson(flow: FlowDefinition): string {
  // System.Text.Json 允许中文直接输出，但仍会转义 HTML 敏感字符。
  return JSON.stringify(canonicalFlow(flow)).replace(/\\"/g, '\\u0022').replace(/[<>&'+]/g, char => ({ '<': '\\u003C', '>': '\\u003E', '&': '\\u0026', "'": '\\u0027', '+': '\\u002B' })[char]!)
}

/**
 * 计算 UTF-8 内容的 SHA-256 小写十六进制摘要。
 * @param bytes 待计算字节。
 * @returns 摘要。
 */
async function sha256(bytes: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', bytes as BufferSource)
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, '0')).join('')
}

/**
 * 生成一个 BPMN 节点元素。
 * @param node 画布节点。
 * @param edges 所有画布连线。
 * @returns XML 片段。
 */
function nodeXml(node: FlowNode, edges: FlowEdge[]): string {
  const element = ({ start: 'startEvent', end: 'endEvent', approval: 'userTask', handling: 'userTask', cc: 'sendTask', exclusiveGateway: 'exclusiveGateway', inclusiveGateway: 'inclusiveGateway', parallelGateway: 'parallelGateway', subprocess: 'callActivity', automatic: 'serviceTask' } as Record<string, string>)[node.type]
  if (!element) throw new Error(`未知的流程节点类型：${node.type}。`)
  let attributes = ` id="${xmlId('Node', node.id)}" name="${escapeXml(node.name)}"`
  if (node.type === 'subprocess' && node.subprocess?.flowCode?.trim()) attributes += ` calledElement="${xmlId('Process', node.subprocess.flowCode)}"`
  if (element.endsWith('Gateway')) {
    const direction = node.gatewayDirection === 'split' ? 'Diverging' : node.gatewayDirection === 'join' ? 'Converging' : 'Unspecified'
    attributes += ` gatewayDirection="${direction}"`
    const fallback = edges.find(edge => edge.sourceId === node.id && edge.isDefault)
    if (fallback) attributes += ` default="${xmlId('Flow', fallback.id)}"`
  }
  return `<bpmn:${element}${attributes} />`
}

/**
 * 生成一个 BPMN 顺序流元素。
 * @param edge 画布连线。
 * @returns XML 片段。
 */
function edgeXml(edge: FlowEdge): string {
  const attributes = ` id="${xmlId('Flow', edge.id)}" sourceRef="${xmlId('Node', edge.sourceId)}" targetRef="${xmlId('Node', edge.targetId)}"`
  return edge.condition?.trim() ? `<bpmn:sequenceFlow${attributes}><bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${escapeXml(edge.condition)}</bpmn:conditionExpression></bpmn:sequenceFlow>` : `<bpmn:sequenceFlow${attributes} />`
}

/**
 * 导出兼容后端的 BPMN 2.0 文件；摘要用于检查损坏，并不证明文件来源。
 * @param flow 完整流程定义。
 * @returns BPMN XML 内容。
 */
export async function exportBpmn(flow: FlowDefinition): Promise<string> {
  if (flow.schemaVersion !== flowSchemaVersion) throw new Error('流程定义版本不兼容。')
  const json = flowJson(flow)
  const bytes = new TextEncoder().encode(json)
  const fingerprint = await sha256(bytes)
  const payload = base64(bytes)
  const process = `<bpmn:process id="${xmlId('Process', flow.code)}" name="${escapeXml(flow.name)}" isExecutable="true"><bpmn:extensionElements><approval:flowDefinition producer="ApprovalService" formatVersion="1" schemaVersion="${flow.schemaVersion}" encoding="base64-utf8-json" sha256="${fingerprint}">${payload}</approval:flowDefinition></bpmn:extensionElements>${flow.canvas.nodes.map(node => nodeXml(node, flow.canvas.edges)).join('')}${flow.canvas.edges.map(edgeXml).join('')}</bpmn:process>`
  return `<?xml version="1.0" encoding="utf-8"?>\n<bpmn:definitions xmlns:bpmn="${bpmnNamespace}" xmlns:approval="${approvalNamespace}" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" id="${xmlId('Definitions', flow.code)}" targetNamespace="${approvalNamespace}" exporter="ApprovalService" exporterVersion="1">${process}</bpmn:definitions>`
}

/**
 * 比较 XML 元素的命名空间、属性及子元素，忽略可安全忽略的格式化空白。
 * @param actual 输入文件元素。
 * @param expected 重建的元素。
 * @returns 两棵树语义相同则为真。
 */
function sameXml(actual: Element, expected: Element): boolean {
  if (actual.namespaceURI !== expected.namespaceURI || actual.localName !== expected.localName) return false
  const attributes = (element: Element): string[] => [...element.attributes].filter(attribute => !attribute.name.startsWith('xmlns')).map(attribute => `${attribute.namespaceURI ?? ''}|${attribute.localName}|${attribute.value}`).sort()
  if (JSON.stringify(attributes(actual)) !== JSON.stringify(attributes(expected))) return false
  const content = (element: Element): Array<Element | string> => [...element.childNodes].filter(node => node.nodeType === 1 || node.nodeType === 3 && !!node.textContent?.trim()).map(node => node.nodeType === 1 ? node as Element : node.textContent ?? '')
  const left = content(actual)
  const right = content(expected)
  return left.length === right.length && left.every((item, index) => typeof item === 'string' ? item === right[index] : right[index] instanceof Element && sameXml(item, right[index]))
}

/**
 * 仅导入带兼容扩展且图形与配置一致的本服务 BPMN 文件。
 * @param xml 待导入文件内容。
 * @returns 文件内完整流程定义。
 */
export async function importBpmn(xml: string): Promise<FlowDefinition> {
  if (!xml.trim() || xml.length > maxFileLength) throw new Error('BPMN 文件为空或过大，只能导入审批服务导出的兼容文件。')
  if (/<!DOCTYPE|<!ENTITY/i.test(xml)) throw new Error('BPMN 文件不能包含外部实体或文档类型声明。')
  const parser = new DOMParser()
  const document = parser.parseFromString(xml, 'application/xml')
  if (document.getElementsByTagName('parsererror').length) throw new Error('BPMN 文件不是合法的 XML。')
  const root = document.documentElement
  const process = [...root.children].find(element => element.namespaceURI === bpmnNamespace && element.localName === 'process')
  const extension = process?.getElementsByTagNameNS(approvalNamespace, 'flowDefinition')[0]
  if (root.namespaceURI !== bpmnNamespace || root.localName !== 'definitions' || root.getAttribute('exporter') !== 'ApprovalService' || extension?.getAttribute('producer') !== 'ApprovalService') throw new Error('只能导入审批服务导出的 BPMN 2.0 文件。')
  if (extension.getAttribute('formatVersion') !== '1' || extension.getAttribute('schemaVersion') !== '1' || extension.getAttribute('encoding') !== 'base64-utf8-json') throw new Error('BPMN 文件版本不兼容，请使用审批服务支持的导出版本。')
  let flow: FlowDefinition
  try {
    const binary = atob(extension.textContent ?? '')
    const bytes = Uint8Array.from(binary, char => char.charCodeAt(0))
    const json = new TextDecoder('utf-8', { fatal: true }).decode(bytes)
    if (await sha256(bytes) !== extension.getAttribute('sha256')) throw new Error('摘要不匹配')
    flow = JSON.parse(json) as FlowDefinition
  } catch {
    throw new Error('BPMN 文件中的审批配置无效，文件可能已被修改。')
  }
  if (flow.schemaVersion !== flowSchemaVersion || !Array.isArray(flow.canvas?.nodes) || !Array.isArray(flow.canvas?.edges)) throw new Error('BPMN 文件中的流程定义版本不兼容。')
  let expected: Document
  try {
    expected = parser.parseFromString(await exportBpmn(flow), 'application/xml')
  } catch {
    throw new Error('BPMN 文件中的审批配置无效，无法还原流程图。')
  }
  // 兼容后端 JSON 编码细节：扩展原文已校验 SHA，标准 BPMN 图必须与该原文还原出的节点和连线一致。
  const expectedExtension = expected.getElementsByTagNameNS(approvalNamespace, 'flowDefinition')[0]
  if (!expectedExtension) throw new Error('BPMN 文件中的审批配置无效。')
  expectedExtension.setAttribute('sha256', extension.getAttribute('sha256') ?? '')
  expectedExtension.textContent = extension.textContent
  if (!sameXml(root, expected.documentElement)) throw new Error('BPMN 图与审批服务扩展定义不一致，文件可能已被修改。')
  return flow
}

/**
 * 画布 BPMN 文件的浏览器下载和读取；编解码仍由独立模块负责。
 */
import type { FlowDefinition } from '../../model/flow.ts'
import { exportBpmn, importBpmn } from './bpmn-codec.ts'

/**
 * 将当前定义下载为正式 BPMN 文件。
 * @param flow 当前完整流程定义。
 * @returns 文件下载完成后的空结果。
 */
export async function downloadCanvasBpmn(flow: FlowDefinition): Promise<void> {
  const xml = await exportBpmn(flow)
  const url = URL.createObjectURL(new Blob([xml], { type: 'application/xml;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `${flow.code || 'flow'}.bpmn`
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/**
 * 读取用户选中的 BPMN 文件。
 * @param file 输入文件。
 * @returns 文件内的完整流程定义。
 */
export async function readCanvasBpmn(file: File): Promise<FlowDefinition> {
  return importBpmn(await file.text())
}

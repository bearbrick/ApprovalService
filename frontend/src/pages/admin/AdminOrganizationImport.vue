<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { downloadOrganizationTemplate, uploadOrganizationFile } from '../../api/admin-adapter.ts'
import type { OrganizationImportIssue } from '../../api/admin-adapter.ts'

/**
 * 导入完成后通知管理页重读组织目录。
 */
const emit = defineEmits<{
  /**
   * 关闭导入面板。
   */
  close: []
  /**
   * 成功导入后刷新列表。
   */
  saved: []
}>()

/**
 * 当前选择的文件。
 */
const file = ref<File | null>(null)
/**
 * 已预检的文件；换文件后必须重新预检。
 */
const previewedFile = ref<File | null>(null)
/**
 * 预检反馈。
 */
const issues = ref<OrganizationImportIssue[]>([])
/**
 * 请求状态。
 */
const busy = ref(false)
/**
 * 操作反馈。
 */
const message = ref('')
/**
 * 当前文件请求的取消控制器。
 */
let request: AbortController | null = null

/**
 * 文件变化后清除旧预检结论。
 * @param event 文件控件事件。
 */
function selectFile(event: Event): void {
  file.value = (event.target as HTMLInputElement).files?.[0] ?? null
  previewedFile.value = null
  issues.value = []
  message.value = ''
}

/**
 * 下载服务端提供的 Excel 模板。
 * @returns 下载操作完成。
 */
async function downloadTemplate(): Promise<void> {
  busy.value = true
  message.value = ''
  try {
    const blob = await downloadOrganizationTemplate()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = 'organization-template.xlsx'
    document.body.append(link)
    link.click()
    link.remove()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  } catch (error) {
    message.value = error instanceof Error ? error.message : '下载组织模板失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 上传文件预检查；正式导入前必须没有问题。
 * @returns 预检操作完成。
 */
async function preview(): Promise<void> {
  if (!file.value || busy.value) return
  busy.value = true
  message.value = ''
  issues.value = []
  request?.abort()
  const controller = new AbortController()
  request = controller
  try {
    const result = await uploadOrganizationFile(file.value, true, controller.signal)
    if (controller.signal.aborted) return
    issues.value = result.errors
    previewedFile.value = result.errors.length === 0 ? file.value : null
    message.value = result.errors.length === 0 ? '预检查通过，可以正式导入。' : `发现 ${result.errors.length} 个问题，请修正文件后重试。`
  } catch (error) {
    if (!controller.signal.aborted) message.value = error instanceof Error ? error.message : '组织文件预检查失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 将预检查通过的同一文件正式导入，服务端会再次校验并记录来源。
 * @returns 导入操作完成。
 */
async function save(): Promise<void> {
  if (!file.value || previewedFile.value !== file.value || busy.value) return
  busy.value = true
  message.value = ''
  const controller = new AbortController()
  request = controller
  try {
    const result = await uploadOrganizationFile(file.value, false, controller.signal)
    if (controller.signal.aborted) return
    issues.value = result.errors
    if (!result.imported || result.errors.length > 0) {
      previewedFile.value = null
      message.value = '导入前数据已变化或文件仍有问题，请修正并重新预检查。'
      return
    }
    emit('saved')
    emit('close')
  } catch (error) {
    if (!controller.signal.aborted) message.value = error instanceof Error ? error.message : '组织文件导入失败。'
  } finally {
    busy.value = false
  }
}

onBeforeUnmount(() => request?.abort())
</script>

<template>
  <section class="admin-panel admin-import-panel" aria-label="导入组织数据">
    <div class="admin-panel-head"><div><h2>导入组织数据</h2><p>同一模板可导入部门、人员和所属关系。先预检查，再正式导入。</p></div><button class="btn" type="button" @click="emit('close')">关闭</button></div>
    <div class="admin-import-content"><button class="btn" type="button" :disabled="busy" @click="downloadTemplate">下载 Excel 模板</button><label>选择 CSV 或 XLSX 文件<input type="file" accept=".csv,.xlsx" :disabled="busy" @change="selectFile"></label><div class="admin-import-actions"><button class="btn" type="button" :disabled="!file || busy" @click="preview">预检查</button><button class="btn btn-primary" type="button" :disabled="!file || previewedFile !== file || busy" @click="save">正式导入</button></div><p v-if="message" role="status">{{ message }}</p><ul v-if="issues.length" class="admin-import-issues"><li v-for="(issue, index) in issues" :key="`${issue.field}-${index}`"><strong>{{ issue.field }}</strong> {{ issue.message }}</li></ul></div>
  </section>
</template>

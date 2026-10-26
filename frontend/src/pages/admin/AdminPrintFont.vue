<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { printingRequest } from '../../api/printing.ts'
/**
 * 当前字体名。
 */
const name = ref('')
/**
 * 待上传文件。
 */
const file = ref<File | null>(null)
/**
 * 字体授权确认。
 */
const confirmed = ref(false)
/**
 * 上传状态。
 */
const busy = ref(false)
/**
 * 操作提示。
 */
const message = ref('')
/**
 * 读取当前字体摘要。
 * @returns 读取完成。
 */
async function refresh(): Promise<void> {
  try {
    const response = await printingRequest('/api/v1/admin/printing/font', true)
    const value = await response.json() as { fileName?: string }
    name.value = value.fileName ?? ''
  } catch (error) { message.value = error instanceof Error ? error.message : '读取字体失败。' }
}
/**
 * 保存文件选择。
 * @param event 文件选择事件。
 */
function choose(event: Event): void { file.value = (event.target as HTMLInputElement).files?.[0] ?? null }
/**
 * 上传明确确认授权的字体。
 * @returns 上传完成。
 */
async function upload(): Promise<void> {
  if (!file.value || !confirmed.value || busy.value) return
  busy.value = true
  try {
    if (!file.value.name.toLowerCase().endsWith('.ttf') || file.value.size > 20 * 1024 * 1024) throw new Error('请选择最大 20 MiB 的 .ttf 字体。')
    await printingRequest(`/api/v1/admin/printing/font?fileName=${encodeURIComponent(file.value.name)}&licenseConfirmed=true`, true, file.value)
    await refresh()
    message.value = '字体已上传，可生成审批单 PDF。'
  } catch (error) { message.value = error instanceof Error ? error.message : '上传字体失败。' }
  finally { busy.value = false }
}
onMounted(refresh)
</script>
<template>
  <section class="admin-panel" aria-label="审批单中文字体">
    <h2>审批单中文字体</h2>
    <p>当前字体：{{ name || '尚未上传' }}。请选择含中文字形的独立 TrueType 字体（.ttf），最大 20 MiB。</p>
    <input type="file" accept=".ttf" :disabled="busy" aria-label="选择打印字体" @change="choose">
    <label><input v-model="confirmed" type="checkbox" :disabled="busy">我确认单位拥有此字体的使用和 PDF 内嵌授权</label>
    <button class="btn" type="button" :disabled="busy || !file || !confirmed" @click="upload">{{ busy ? '正在上传…' : '上传字体' }}</button>
    <p v-if="message" role="status">{{ message }}</p>
  </section>
</template>

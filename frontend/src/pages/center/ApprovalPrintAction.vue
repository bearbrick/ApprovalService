<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { printingRequest } from '../../api/printing.ts'
/**
 * 当前审批单。
 */
const props = defineProps<{
  /**
   * 已授权审批单 ID。
   */
  instanceId: string
}>()
/**
 * 生成状态。
 */
const busy = ref(false)
/**
 * 操作提示。
 */
const error = ref('')
/**
 * 验权后生成的本地 PDF 地址。
 */
const url = ref('')
/**
 * 防止切换审批单后显示过期文件。
 */
let generation = 0
/**
 * 释放旧文件。
 * @returns 清理完成。
 */
function clear(): void {
  generation++
  if (url.value) URL.revokeObjectURL(url.value)
  url.value = ''
  error.value = ''
  busy.value = false
}
/**
 * 用户显式生成一次 PDF。
 * @returns 生成完成。
 */
async function generate(): Promise<void> {
  if (busy.value) return
  clear()
  const current = generation
  busy.value = true
  try {
    const response = await printingRequest(`/api/v1/instances/${encodeURIComponent(props.instanceId)}/print`, false)
    const pdf = await response.blob()
    if (current === generation) url.value = URL.createObjectURL(pdf)
  } catch (reason) {
    if (current === generation) error.value = reason instanceof Error ? reason.message : 'PDF 生成失败。'
  } finally { if (current === generation) busy.value = false }
}
watch(() => props.instanceId, clear)
onBeforeUnmount(clear)
</script>
<template>
  <div>
    <button class="btn" type="button" :disabled="busy" @click="generate">{{ busy ? '正在生成 PDF…' : '生成审批单 PDF' }}</button>
    <template v-if="url"><a class="btn" :href="url" target="_blank" rel="noopener">预览并打印</a><a class="btn" :href="url" download="审批单.pdf">保存 PDF</a></template>
    <p v-if="url" role="status">本次生成已记入打印记录。预览中可使用浏览器的打印功能。</p>
    <p v-if="error" role="alert">{{ error }}</p>
  </div>
</template>

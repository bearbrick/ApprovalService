<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { fetchCenterSignature, latestCenterSignature } from '../../api/center-signatures.ts'
import type { CenterSignature } from '../../api/center-signatures.ts'
import SignaturePad from './SignaturePad.vue'

/**
 * 最近保存的个人签名元数据。
 */
const latest = ref<CenterSignature | null>(null)
/**
 * 当前可预览的图片地址。
 */
const previewUrl = ref('')
/**
 * 状态说明。
 */
const message = ref('')

/**
 * 读取本人个人签名并刷新图片，旧资产仍可用于既有审计。
 * @returns 读取完成。
 */
async function load(): Promise<void> {
  try {
    const asset = await latestCenterSignature()
    latest.value = asset
    if (previewUrl.value) URL.revokeObjectURL(previewUrl.value)
    previewUrl.value = asset ? URL.createObjectURL(await fetchCenterSignature(asset.id)) : ''
    message.value = ''
  } catch (cause) {
    message.value = cause instanceof Error ? cause.message : '个人签名读取失败。'
  }
}

/**
 * 上传成功后更新个人签名预览。
 * @param id 新资产 ID。
 * @returns 刷新完成。
 */
async function saved(id: string): Promise<void> {
  await load()
  if (latest.value?.id === id) message.value = '个人签名已保存。每次审批仍须明确选择使用。'
}

onMounted(() => { void load() })
onBeforeUnmount(() => { if (previewUrl.value) URL.revokeObjectURL(previewUrl.value) })
</script>

<template>
  <section class="center-card">
    <h3>个人手写签名</h3>
    <p>个人签名仅供以后审批时选择，不会自动附在任何操作上。</p>
    <img v-if="previewUrl" :src="previewUrl" alt="当前个人手写签名" class="signature-setting-preview">
    <p v-else>尚未保存个人签名。</p>
    <SignaturePad :personal="true" @saved="saved" />
    <p v-if="message" role="status">{{ message }}</p>
  </section>
</template>

<style scoped>
.signature-setting-preview { display: block; max-width: 300px; max-height: 120px; margin: 10px 0; border: 1px solid #cad5e2; border-radius: 6px; background: white; }
</style>

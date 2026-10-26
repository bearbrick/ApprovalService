<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { deleteSystemLogo, loadSystemBranding, loadSystemLogoUrl, saveCompanyName, uploadSystemLogo } from '../../api/system-branding.ts'
import type { SystemBranding } from '../../api/system-branding.ts'

/**
 * 当前服务端设置。
 */
const value = ref<SystemBranding | null>(null)
/**
 * 当前表单中的单位名称。
 */
const companyName = ref('')
/**
 * 本次操作的原因。
 */
const reason = ref('')
/**
 * 当前图片预览 URL。
 */
const logoUrl = ref<string | null>(null)
/**
 * 读取或保存操作的状态。
 */
const busy = ref(false)
/**
 * 操作结果或错误信息。
 */
const message = ref('')
/**
 * 当前选择但尚未上传的文件。
 */
const selectedFile = ref<File | null>(null)
/**
 * 原因符合审计约束时才允许操作。
 */
const reasonValid = computed(() => reason.value.trim().length >= 2 && reason.value.trim().length <= 200)

/**
 * 重新读取设置并释放旧的图片地址。
 * @returns 读取完成。
 */
async function refresh(): Promise<void> {
  busy.value = true
  try {
    const next = await loadSystemBranding()
    value.value = next
    companyName.value = next.companyName
    if (logoUrl.value) URL.revokeObjectURL(logoUrl.value)
    logoUrl.value = next.logoSha256 ? await loadSystemLogoUrl() : null
  } catch (error) {
    message.value = error instanceof Error ? error.message : '读取系统信息失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 保存单位名称。
 * @returns 保存完成。
 */
async function saveName(): Promise<void> {
  if (!value.value || !reasonValid.value || busy.value) return
  busy.value = true
  message.value = ''
  try {
    value.value = await saveCompanyName(companyName.value.trim(), value.value.revision, reason.value.trim())
    reason.value = ''
    message.value = '单位名称已保存。'
  } catch (error) {
    message.value = error instanceof Error ? error.message : '保存单位名称失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 上传 Logo 并刷新带鉴权的预览。
 * @returns 上传完成。
 */
async function saveLogo(): Promise<void> {
  if (!value.value || !selectedFile.value || !reasonValid.value || busy.value) return
  busy.value = true
  message.value = ''
  try {
    value.value = await uploadSystemLogo(selectedFile.value, value.value.revision, reason.value.trim())
    selectedFile.value = null
    reason.value = ''
    if (logoUrl.value) URL.revokeObjectURL(logoUrl.value)
    logoUrl.value = await loadSystemLogoUrl()
    message.value = '单位 Logo 已上传。'
  } catch (error) {
    message.value = error instanceof Error ? error.message : '上传 Logo 失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 移除图片并保留审计。
 * @returns 删除完成。
 */
async function removeLogo(): Promise<void> {
  if (!value.value?.logoSha256 || !reasonValid.value || busy.value) return
  busy.value = true
  message.value = ''
  try {
    value.value = await deleteSystemLogo(value.value.revision, reason.value.trim())
    if (logoUrl.value) URL.revokeObjectURL(logoUrl.value)
    logoUrl.value = null
    reason.value = ''
    message.value = '单位 Logo 已移除。'
  } catch (error) {
    message.value = error instanceof Error ? error.message : '移除 Logo 失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 选择尚未上传的文件。
 * @param event 原生文件选择事件。
 */
function chooseFile(event: Event): void {
  selectedFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
}

onBeforeUnmount(() => { if (logoUrl.value) URL.revokeObjectURL(logoUrl.value) })
void refresh()
</script>

<template>
  <section class="admin-panel" aria-label="系统信息设置">
    <div class="admin-panel-head"><div><h2>打印页眉品牌</h2><p>单位名称和 Logo 将用于审批单打印页眉。每次修改都需填写原因并记入操作日志。</p></div><button class="btn" type="button" :disabled="busy" @click="refresh">刷新</button></div>
    <div v-if="value" class="admin-password-content">
      <label>单位名称<input v-model="companyName" maxlength="60" placeholder="请输入单位名称" :disabled="busy"></label>
      <div><strong>单位 Logo</strong><p>支持 PNG，最大 1 MiB。图片上传后由服务端校验并保存。</p><img v-if="logoUrl" :src="logoUrl" alt="当前单位 Logo" style="max-width: 280px; max-height: 100px; object-fit: contain; display: block; margin: 12px 0"><p v-else>尚未上传 Logo</p><input type="file" accept="image/png" :disabled="busy" @change="chooseFile"></div>
      <label>修改原因<textarea v-model="reason" maxlength="200" rows="3" placeholder="填写 2–200 字原因" :disabled="busy"></textarea></label>
      <div class="admin-import-actions"><button class="btn btn-primary" type="button" :disabled="busy || !reasonValid || !companyName.trim()" @click="saveName">保存单位名称</button><button class="btn" type="button" :disabled="busy || !reasonValid || !selectedFile" @click="saveLogo">上传 Logo</button><button class="btn" type="button" :disabled="busy || !reasonValid || !value.logoSha256" @click="removeLogo">移除 Logo</button></div>
      <p v-if="message" role="status">{{ message }}</p>
    </div>
    <p v-else role="status">{{ message || '正在读取系统信息…' }}</p>
  </section>
</template>

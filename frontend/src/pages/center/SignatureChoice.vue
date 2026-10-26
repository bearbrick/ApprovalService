<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { centerSession } from '../../api/center-session.ts'
import { latestCenterSignature } from '../../api/center-signatures.ts'
import type { CenterSignature } from '../../api/center-signatures.ts'
import SignaturePad from './SignaturePad.vue'

/**
 * 本次任务明确选用的签名 ID。
 */
const props = defineProps<{
  /**
   * 父表单保存的本次签名 ID；空表示未使用。
   */
  modelValue: string
  /**
   * 成功推进当前节点时是否必须选用。
   */
  required?: boolean
}>()

/**
 * 本次签名选择改变。
 */
const emit = defineEmits<{
  /**
   * 更新本次签名 ID。
   */
  'update:modelValue': [id: string]
}>()

/**
 * 本地会话最近保存的个人签名。
 */
const personal = ref<CenterSignature | null>(null)
/**
 * 在任务中刚上传的单次签名 ID。
 */
const oneTimeId = ref('')
/**
 * 读取或上传的提示。
 */
const message = ref('')

/**
 * 嵌入会话不能查看或修改默认个人资产。
 */
onMounted(async () => {
  if (centerSession()?.targetPath) return
  try { personal.value = await latestCenterSignature() } catch {
    // 个人签名读取失败不妨碍本次重新手写。
  }
})

/**
 * 保存本次资产后要求用户再点击“本次使用”。
 * @param id 新签名 ID。
 */
function saved(id: string): void {
  oneTimeId.value = id
  message.value = '签名已保存。请点击“本次使用新签名”后再提交审批。'
  emit('update:modelValue', '')
}

/**
 * 明确选择一个已上传的本人资产。
 * @param id 本次签名 ID。
 */
function choose(id: string): void {
  emit('update:modelValue', id)
  message.value = '已选用手写签名，本次提交后会冻结在审批记录中。'
}
</script>

<template>
  <fieldset class="signature-choice">
    <legend>本次手写签名{{ required ? '（成功办理时必填）' : '（可选）' }}</legend>
    <p>个人签名不会自动使用；每次操作都要在这里明确选择。</p>
    <div class="signature-choice-actions">
      <button v-if="personal" class="btn" type="button" :aria-pressed="props.modelValue === personal.id" @click="choose(personal.id)">本次使用个人签名</button>
      <button v-if="oneTimeId" class="btn" type="button" :aria-pressed="props.modelValue === oneTimeId" @click="choose(oneTimeId)">本次使用新签名</button>
      <button v-if="props.modelValue" class="btn" type="button" @click="choose('')">本次不使用</button>
    </div>
    <SignaturePad :personal="false" @saved="saved" />
    <p v-if="message" role="status">{{ message }}</p>
  </fieldset>
</template>

<style scoped>
.signature-choice { border: 1px solid #d3dce8; border-radius: 8px; padding: 12px; margin: 12px 0; min-width: 0; }
.signature-choice legend { font-weight: 600; padding: 0 5px; }
.signature-choice-actions { display: flex; flex-wrap: wrap; gap: 8px; margin: 8px 0; }
.signature-choice p { color: #53647b; font-size: 13px; }
</style>

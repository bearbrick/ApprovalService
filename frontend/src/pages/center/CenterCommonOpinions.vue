<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ApiError } from '../../api/http-types.ts'
import { centerSession } from '../../api/center-session.ts'
import { createCenterOpinion, deleteCenterOpinion, loadCenterOpinions, updateCenterOpinion } from '../../api/center-opinions.ts'
import type { CenterOpinion } from '../../api/center-opinions.ts'

/**
 * 身份失效时由中心页面统一退出登录。
 */
const emit = defineEmits<{
  /**
   * 当前会话失效。
   */
  unauthorized: []
}>()

/**
 * 嵌入会话只可读取并选择，不能管理个人设置。
 */
const readOnly = Boolean(centerSession()?.targetPath)
/**
 * 服务端保存的本人意见。
 */
const opinions = ref<CenterOpinion[]>([])
/**
 * 新建或修改中的意见正文。
 */
const draft = ref('')
/**
 * 正在修改的意见标识，空值表示新建。
 */
const editingId = ref('')
/**
 * 列表请求状态。
 */
const loading = ref(true)
/**
 * 写操作状态。
 */
const busy = ref(false)
/**
 * 当前错误提示。
 */
const error = ref('')
/**
 * 服务端确认后的提示。
 */
const saved = ref('')

/**
 * 重新读取本人意见，避免展示臆测的持久结果。
 * @returns 读取完成。
 */
async function reload(): Promise<void> {
  loading.value = true
  error.value = ''
  try {
    opinions.value = await loadCenterOpinions()
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '常用意见读取失败。'
  } finally {
    loading.value = false
  }
}

/**
 * 选择一条已保存意见准备编辑。
 * @param item 已保存的本人意见。
 * @returns 编辑框更新完成。
 */
function edit(item: CenterOpinion): void {
  editingId.value = item.id
  draft.value = item.content
  error.value = ''
  saved.value = ''
}

/**
 * 清除编辑状态。
 * @returns 编辑状态清除完成。
 */
function cancel(): void {
  editingId.value = ''
  draft.value = ''
}

/**
 * 将新增或修改提交给服务端。
 * @returns 保存完成。
 */
async function save(): Promise<void> {
  if (readOnly || busy.value) return
  const content = draft.value.trim()
  if (!content || content.length > 500) {
    error.value = '常用意见必须填写，且不能超过五百字。'
    return
  }
  busy.value = true
  error.value = ''
  saved.value = ''
  try {
    if (editingId.value) {
      const item = await updateCenterOpinion(editingId.value, content)
      opinions.value = opinions.value.map(old => old.id === item.id ? item : old)
    } else {
      const item = await createCenterOpinion(content)
      opinions.value = [...opinions.value, item]
    }
    cancel()
    saved.value = '常用意见已保存。'
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '常用意见保存失败。'
  } finally {
    busy.value = false
  }
}

/**
 * 删除本人一条意见，只有服务端成功后更新列表。
 * @param id 意见标识。
 * @returns 删除完成。
 */
async function remove(id: string): Promise<void> {
  if (readOnly || busy.value) return
  busy.value = true
  error.value = ''
  saved.value = ''
  try {
    await deleteCenterOpinion(id)
    opinions.value = opinions.value.filter(item => item.id !== id)
    if (editingId.value === id) cancel()
    saved.value = '常用意见已删除。'
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) emit('unauthorized')
    error.value = cause instanceof Error ? cause.message : '常用意见删除失败。'
  } finally {
    busy.value = false
  }
}

onMounted(() => { void reload() })
</script>

<template>
  <section class="center-card center-settings center-opinions">
    <header><h2>常用意见</h2><p>最多保存 30 条，每条不超过 500 字。选择常用意见只会填入审批意见栏，提交仍需确认。</p></header>
    <div class="center-opinions-body">
      <p v-if="loading">正在读取常用意见…</p>
      <template v-else>
        <p v-if="!opinions.length">尚未保存常用意见。</p>
        <ul v-else class="center-opinions-list"><li v-for="item in opinions" :key="item.id"><span>{{ item.content }}</span><div v-if="!readOnly"><button class="btn" type="button" :disabled="busy" @click="edit(item)">编辑</button><button class="btn" type="button" :disabled="busy" @click="remove(item.id)">删除</button></div></li></ul>
        <form v-if="!readOnly" @submit.prevent="save"><label>{{ editingId ? '修改意见' : '新增意见' }}<textarea v-model="draft" maxlength="500" rows="3" :disabled="busy || (!editingId && opinions.length >= 30)" placeholder="填写常用审批意见"></textarea></label><div><button class="btn btn-primary" type="submit" :disabled="busy || (!editingId && opinions.length >= 30)">{{ editingId ? '保存修改' : '保存意见' }}</button><button v-if="editingId" class="btn" type="button" :disabled="busy" @click="cancel">取消</button><span>{{ opinions.length }}/30 条</span></div></form>
        <p v-else>嵌入会话只能读取常用意见。</p>
      </template>
      <p v-if="error" role="alert" class="center-login-error">{{ error }}</p><p v-if="saved" role="status" class="center-settings-saved">{{ saved }}</p>
    </div>
  </section>
</template>

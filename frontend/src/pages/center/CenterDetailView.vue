<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import { visibleCenterActions } from '../../model/center.ts'
import type { CenterAction, CenterDetail } from '../../model/center.ts'

/**
 * 审批详情已授权数据与查询状态。
 */
const props = defineProps<{
  /**
   * 服务端返回的只读详情。
   */
  detail: CenterDetail | null
  /**
   * 加载、可用或失败。
   */
  state: 'loading' | 'ready' | 'error' | 'unavailable'
  /**
   * 查询失败的中文说明。
   */
  error: string
  /**
   * 路由上的审批单标识。
   */
  requestedId: string
}>()

/**
 * 详情页请求重试。
 */
const emit = defineEmits<{
  /**
   * 用户主动刷新详情。
   */
  reload: []
}>()

/**
 * 当前的只读详情页签。
 */
const tab = ref<'data' | 'progress'>('data')
/**
 * 仅展示服务端明确许可的操作。
 */
const actions = computed(() => props.detail ? visibleCenterActions(props.detail.item) : [])
/**
 * 操作名称与设计书术语保持一致。
 */
const actionLabels: Readonly<Record<CenterAction, string>> = {
  approve: '同意', reject: '拒绝', return: '退回', transfer: '转交', addSign: '加签', removeSign: '减签',
  consult: '征询意见', supplement: '要求补充', copy: '抄送', undoApproval: '撤销同意', comment: '评论', remind: '催办',
}

watch(() => props.requestedId, () => { tab.value = 'data' })
</script>

<template>
  <div class="center-detail">
    <div class="center-detail-top"><a class="center-back" href="#/"><UiIcon name="arrow" />返回审批中心</a><button class="btn" type="button" :disabled="state === 'loading'" @click="emit('reload')"><UiIcon name="refresh" />刷新</button></div>
    <template v-if="detail && state === 'ready'">
      <header class="center-detail-title"><span class="center-detail-icon"><UiIcon name="center" /></span><div><h2>{{ detail.item.title }} <span v-if="detail.item.urgent" class="center-urgent">加急</span></h2><p>{{ detail.item.flowName }} · {{ detail.item.serialNo }} · {{ detail.item.status }}</p></div></header>
      <div class="center-detail-grid">
        <div class="center-detail-primary">
          <nav class="center-detail-tabs" aria-label="审批详情页签"><button type="button" :aria-current="tab === 'data' ? 'page' : undefined" @click="tab = 'data'">审批数据</button><button type="button" :aria-current="tab === 'progress' ? 'page' : undefined" @click="tab = 'progress'">流程进度</button></nav>
          <section v-if="tab === 'data'" class="center-card"><h3>业务数据快照</h3><dl v-if="detail.fields.length" class="center-data-grid"><div v-for="field in detail.fields" :key="field.key"><dt>{{ field.label }}</dt><dd>{{ field.value }}</dd></div></dl><p v-else class="center-blank">这张审批单没有可展示的业务字段。</p></section>
          <section v-if="tab === 'progress'" class="center-card"><h3>审批经过</h3><ol v-if="detail.timeline.length" class="center-timeline"><li v-for="entry in detail.timeline" :key="entry.id" :class="{ current: entry.current }"><strong>{{ entry.title }}</strong><p>{{ entry.actor }} <span v-if="entry.occurredAt">· {{ entry.occurredAt }}</span></p><blockquote v-if="entry.opinion">{{ entry.opinion }}</blockquote></li></ol><p v-else class="center-blank">暂无可展示的流程记录。</p></section>
          <section class="center-card"><h3>附件</h3><ul v-if="detail.attachmentNames.length" class="center-attachment-list"><li v-for="name in detail.attachmentNames" :key="name">{{ name }} <span>下载与预览待权限接口接入</span></li></ul><p v-else class="center-blank">当前详情接口尚未返回附件信息。</p></section>
        </div>
        <aside class="center-detail-side"><section class="center-card"><h3>审批信息</h3><dl class="center-meta"><dt>发起人</dt><dd>{{ detail.item.initiatorName }}</dd><dt>来源系统</dt><dd>{{ detail.item.sourceName }}</dd><dt>业务单号</dt><dd>{{ detail.item.businessKey }}</dd><dt>当前环节</dt><dd>{{ detail.item.currentStep }}</dd><dt>发起时间</dt><dd>{{ detail.item.createdAt }}</dd></dl></section><section class="center-card"><h3>当前进度</h3><ol v-if="detail.timeline.length" class="center-timeline"><li v-for="entry in detail.timeline" :key="entry.id" :class="{ current: entry.current }"><strong>{{ entry.title }}</strong><p>{{ entry.actor }}</p></li></ol><p v-else class="center-blank">暂无进度记录。</p></section></aside>
      </div>
      <div v-if="actions.length" class="center-action-bar"><p>可用操作由服务端逐单确认。操作提交接口接入后在此办理。</p><div><button v-for="action in actions" :key="action" class="btn" type="button" disabled :title="`${actionLabels[action]}接口尚未接入`">{{ actionLabels[action] }}</button></div></div>
    </template>
    <div v-else class="center-card center-detail-empty">
      <UiEmptyState v-if="state === 'loading'" title="正在读取审批详情" description="请稍候。" icon="refresh" />
      <UiEmptyState v-else-if="state === 'unavailable'" title="审批详情尚未接入" description="详情页面已经就绪，登录与审批中心接口接入后才能读取真实数据。" icon="info" />
      <UiEmptyState v-else title="读取审批详情失败" :description="error" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
    </div>
  </div>
</template>

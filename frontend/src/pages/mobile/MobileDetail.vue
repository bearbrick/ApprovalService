<script setup lang="ts">
import { computed } from 'vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import { visibleCenterActions } from '../../model/center.ts'
import type { CenterDetail } from '../../model/center.ts'
import { mobileActionLabels } from '../../model/mobile.ts'

/**
 * 手机审批详情的只读数据和查询状态。
 */
const props = defineProps<{
  /**
   * 当前账号有权查看的审批详情。
   */
  detail: CenterDetail | null
  /**
   * 详情读取状态。
   */
  state: 'loading' | 'ready' | 'unavailable' | 'offline' | 'error'
  /**
   * 失败时的中文说明。
   */
  error: string
}>()

/**
 * 手动刷新详情。
 */
const emit = defineEmits<{
  /**
   * 请求重新读取详情。
   */
  reload: []
}>()

/**
 * 只显示服务端明确允许的动作；提交接口接入前按钮始终禁用。
 */
const actions = computed(() => props.detail ? visibleCenterActions(props.detail.item) : [])
</script>

<template>
  <div class="mobile-detail">
    <div class="mobile-detail-tools"><a href="#/m"><UiIcon name="arrow" />返回列表</a><button type="button" :disabled="state === 'loading'" @click="emit('reload')"><UiIcon name="refresh" />刷新</button></div>
    <template v-if="detail && state === 'ready'"><header class="mobile-detail-heading"><p>{{ detail.item.flowName }} · {{ detail.item.serialNo }}</p><h1 id="page-title" tabindex="-1">{{ detail.item.title }}</h1><div><span class="mobile-status">{{ detail.item.status }}</span><em v-if="detail.item.urgent">加急</em></div></header>
      <section class="mobile-detail-section"><h2>基本信息 <small>业务数据只读</small></h2><dl><div><dt>发起人</dt><dd>{{ detail.item.initiatorName }}</dd></div><div><dt>来源系统</dt><dd>{{ detail.item.sourceName }}</dd></div><div><dt>业务单号</dt><dd>{{ detail.item.businessKey }}</dd></div><div><dt>当前环节</dt><dd>{{ detail.item.currentStep }}</dd></div><div><dt>发起时间</dt><dd>{{ detail.item.createdAt }}</dd></div></dl></section>
      <section class="mobile-detail-section"><h2>审批数据</h2><dl v-if="detail.fields.length"><div v-for="field in detail.fields" :key="field.key"><dt>{{ field.label }}</dt><dd>{{ field.value }}</dd></div></dl><p v-else>没有可展示的业务字段。</p></section>
      <section class="mobile-detail-section"><h2>附件</h2><ul v-if="detail.attachmentNames.length"><li v-for="name in detail.attachmentNames" :key="name">{{ name }}<small>下载与预览待权限接口接入</small></li></ul><p v-else>没有可展示的附件。</p></section>
      <section class="mobile-detail-section"><h2>审批进度</h2><ol v-if="detail.timeline.length" class="mobile-timeline"><li v-for="entry in detail.timeline" :key="entry.id" :class="{ current: entry.current }"><strong>{{ entry.title }}</strong><small>{{ entry.actor }}<template v-if="entry.occurredAt"> · {{ entry.occurredAt }}</template></small><p v-if="entry.opinion">{{ entry.opinion }}</p></li></ol><p v-else>暂无可展示的进度记录。</p></section>
      <div v-if="actions.length" class="mobile-detail-actions"><p>操作接口尚未接入，当前无法提交审批。</p><div><button v-for="action in actions" :key="action" type="button" disabled :title="`${mobileActionLabels[action]}接口尚未接入`">{{ mobileActionLabels[action] }}</button></div></div>
    </template>
    <UiEmptyState v-else-if="state === 'loading'" title="正在读取审批详情" description="请稍候。" icon="refresh" />
    <UiEmptyState v-else-if="state === 'unavailable'" title="手机详情尚未接入" description="真实登录与审批中心接口接入后才能读取这张审批单。" icon="info" />
    <UiEmptyState v-else-if="state === 'offline'" title="当前没有网络" description="联网后会自动重试，也可以手动刷新。" icon="alert"><template #action><button class="btn" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
    <UiEmptyState v-else title="读取审批详情失败" :description="error" icon="alert"><template #action><button class="btn btn-primary" type="button" @click="emit('reload')">重试</button></template></UiEmptyState>
  </div>
</template>

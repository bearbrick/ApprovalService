<script setup lang="ts">
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
</script>

<template>
  <div class="mobile-me"><p class="mobile-eyebrow">审批中心</p><h1 id="page-title" tabindex="-1">我的</h1><section class="mobile-detail-section"><h2>账号与偏好</h2><UiEmptyState title="个人信息尚未接入" description="账号、委托、签名、常用语和提醒偏好将在身份与审批接口接入后显示。当前不会保存设置。" icon="info" /></section><p class="mobile-list-note">如需发起或修改业务单据，请返回来源业务系统。</p></div>
</template>

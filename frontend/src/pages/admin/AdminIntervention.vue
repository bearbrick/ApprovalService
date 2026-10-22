<script setup lang="ts">
/**
 * 管理员干预入口仅说明必需字段，正式权限与审计接口接入前不可提交。
 */
</script>

<template>
  <section class="admin-panel admin-intervention"><div class="admin-panel-head"><div><h2>管理员干预</h2><p>更换审批人、跳转、终止、离职交接和重发都必须填写原因并写入审计记录。</p></div><span class="badge">操作未接入</span></div><div class="admin-intervention-grid"><label>操作类型<select disabled><option>请先选择有权管理的审批实例</option></select></label><label>干预原因<textarea disabled rows="3" placeholder="原因必填，正式接入后写入操作记录"></textarea></label></div><div class="admin-intervention-foot"><span>当前页面不会改变任何在途审批。</span><button class="btn btn-primary" type="button" disabled>提交干预</button></div></section>
</template>

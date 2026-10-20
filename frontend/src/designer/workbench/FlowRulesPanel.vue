<script setup lang="ts">
import type { FlowDefinition } from '../../model/flow.ts'
import { copyFlow } from './workbench-model.ts'

/**
 * 流程元信息及发起范围设置。
 */
const props = defineProps<{
  /**
   * 当前流程草稿。
   */
  definition: FlowDefinition
}>()
/**
 * 流程配置变化。
 */
const emit = defineEmits<{
  /**
   * 返回独立的新流程对象。
   */
  update: [value: FlowDefinition]
}>()

/**
 * 更新流程编码或名称。
 * @param key 要修改的元信息。
 * @param value 新值。
 */
function setText(key: 'code' | 'name', value: string): void {
  const next = copyFlow(props.definition)
  next[key] = value
  emit('update', next)
}
/**
 * 将每行或逗号分隔的编码存成去重列表。
 * @param key 可用范围字段。
 * @param value 输入文本。
 */
function setScope(key: 'allowedClientApps' | 'allowedInitiatorDepartments' | 'allowedInitiatorRoles', value: string): void {
  const next = copyFlow(props.definition)
  next[key] = [...new Set(value.split(/[\n,，]/).map(part => part.trim()).filter(Boolean))]
  emit('update', next)
}
</script>

<template>
  <section class="rules-panel">
    <header><span>流程设置</span><h2>基本信息与可用范围</h2><p>流程由业务系统通过接口发起。范围限制只筛选允许使用的应用和发起人，不会在审批中心创建业务单据。</p></header>
    <div class="rules-grid"><label>流程编码<input :value="definition.code" autocomplete="off" @input="setText('code', ($event.target as HTMLInputElement).value)" /><small>作为业务系统调用时的稳定编码。</small></label><label>流程名称<input :value="definition.name" @input="setText('name', ($event.target as HTMLInputElement).value)" /><small>展示在审批中心和通知中。</small></label></div>
    <h3>谁能发起</h3>
    <div class="rules-grid"><label>允许接入应用<textarea rows="4" :value="definition.allowedClientApps.join('\n')" placeholder="每行一个应用编码；留空不额外限制" @change="setScope('allowedClientApps', ($event.target as HTMLTextAreaElement).value)" /></label><label>允许部门<textarea rows="4" :value="definition.allowedInitiatorDepartments.join('\n')" placeholder="每行一个部门编码；留空不额外限制" @change="setScope('allowedInitiatorDepartments', ($event.target as HTMLTextAreaElement).value)" /></label><label>允许角色<textarea rows="4" :value="definition.allowedInitiatorRoles.join('\n')" placeholder="每行一个角色编码；留空不额外限制" @change="setScope('allowedInitiatorRoles', ($event.target as HTMLTextAreaElement).value)" /></label></div>
    <p class="notice">实际接入授权和人员权限仍由服务端校验。草稿在当前页面编辑；正式版本需经过服务端发布。</p>
  </section>
</template>

<style scoped>
.rules-panel{max-width:960px;padding:24px;border:1px solid var(--line);border-radius:6px;background:#fff}.rules-panel header span{font-size:11px;font-weight:700;color:var(--blue)}.rules-panel h2{margin:4px 0 8px;font-size:19px}.rules-panel header p,.notice{color:var(--muted);line-height:1.7}.rules-panel h3{margin:24px 0 12px;padding-top:20px;border-top:1px solid var(--line);font-size:15px}.rules-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px;margin-top:18px}.rules-grid label{display:grid;align-content:start;gap:6px;font-weight:600}.rules-grid input,.rules-grid textarea{width:100%;min-width:0;padding:9px 11px;border:1px solid #cbd5e1;border-radius:5px;font-weight:400}.rules-grid textarea{resize:vertical}.rules-grid small{color:var(--muted);font-weight:400}.notice{margin-top:20px;padding:12px;background:#f8faff;border-left:3px solid var(--blue)}@media(max-width:700px){.rules-grid{grid-template-columns:1fr}}
</style>

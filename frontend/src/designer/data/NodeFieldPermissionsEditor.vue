<script setup lang="ts">
import type { FlowDataDefinition, FlowFieldAccessType, FlowFieldPermission } from '../../model/flow.ts'
import { fieldReferences } from './data-model.ts'

/**
 * 节点字段权限编辑器的输入。
 */
const props = defineProps<{
  /**
   * 流程数据定义。
   */
  data: FlowDataDefinition
  /**
   * 当前人工节点的字段权限。
   */
  permissions: FlowFieldPermission[]
}>()

/**
 * 把新权限数组传回所属节点编辑器。
 */
const emit = defineEmits<{
  /**
   * 节点字段权限变化。
   */
  'update:permissions': [value: FlowFieldPermission[]]
}>()

/**
 * 为字段查找当前显式权限。
 * @param fieldKey 字段路径。
 * @returns 当前设置，空字符串表示未单独设置。
 */
function accessFor(fieldKey: string): FlowFieldAccessType | '' {
  return props.permissions.find(item => item.fieldKey === fieldKey)?.access ?? ''
}

/**
 * 修改或移除字段的显式权限。
 * @param fieldKey 字段路径。
 * @param access 新权限；空值表示删除此节点的单独设置。
 */
function setAccess(fieldKey: string, access: string): void {
  const next = props.permissions.filter(item => item.fieldKey !== fieldKey).map(item => ({ ...item }))
  if (access) next.push({ fieldKey, access: access as FlowFieldAccessType })
  emit('update:permissions', next)
}
</script>

<template>
  <section class="permission-panel">
    <header><h3>节点字段权限</h3><p>可对当前节点隐藏、只读或开放填写。审批人填写的字段仍须获授权节点才能填写。</p></header>
    <div v-if="!data.fields.length" class="permission-empty">请先定义审批数据字段。</div>
    <div v-for="reference in fieldReferences(data)" :key="reference.key" class="permission-row"><div><strong>{{ reference.label }}</strong><small>{{ reference.key }}</small></div><select :aria-label="`${reference.label}的节点权限`" :value="accessFor(reference.key)" @change="setAccess(reference.key, ($event.target as HTMLSelectElement).value)"><option value="">不单独设置</option><option value="readOnly">只读</option><option value="hidden">隐藏</option><option v-if="reference.field.approverInput" value="editable">允许审批人填写</option></select></div>
  </section>
</template>

<style scoped>
.permission-panel{border:1px solid var(--line);border-radius:5px;background:#fff}.permission-panel header{padding:14px 16px;background:#f8faff;border-bottom:1px solid var(--line)}.permission-panel h3{font-size:14px}.permission-panel p{color:var(--muted);margin-top:5px}.permission-row{display:flex;align-items:center;justify-content:space-between;gap:14px;padding:10px 16px;border-top:1px solid var(--line)}.permission-row:first-of-type{border-top:0}.permission-row strong,.permission-row small{display:block}.permission-row small{color:var(--muted);font:11px Consolas,monospace}.permission-row select{min-width:150px;min-height:34px;padding:6px 8px;border:1px solid #cbd5e1;border-radius:4px;background:#fff}.permission-empty{padding:24px;color:var(--muted)}@media(max-width:560px){.permission-row{align-items:stretch;flex-direction:column}.permission-row select{width:100%}}
</style>

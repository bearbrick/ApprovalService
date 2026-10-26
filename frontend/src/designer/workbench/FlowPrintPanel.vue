<script setup lang="ts">
import { computed } from 'vue'
import type { FlowDataDefinition, FlowPrintSettings } from '../../model/flow.ts'
import { fieldReferences } from '../data/data-model.ts'

/**
 * 打印设置面板的输入。
 */
const props = defineProps<{
  /**
   * 当前审批单打印设置。
   */
  settings: FlowPrintSettings
  /**
   * 可打印的审批字段。
   */
  data: FlowDataDefinition
}>()
/**
 * 打印设置变化。
 */
const emit = defineEmits<{
  /**
   * 返回独立的新配置。
   */
  update: [value: FlowPrintSettings]
}>()
/**
 * 可打印字段列表。
 */
const fields = computed(() => fieldReferences(props.data).filter(item => !item.key.includes('.')))
/**
 * 打印附加内容开关。
 */
const toggles = [
  { key: 'showComments', label: '审批意见' }, { key: 'showSignatures', label: '签名' },
  { key: 'includeReceiptPages', label: '票据图片页' }, { key: 'showLogo', label: '单位 Logo' },
  { key: 'showQrCode', label: '二维码' }, { key: 'showWatermark', label: '打印水印' },
] as const

/**
 * 修改一个打印配置项。
 * @param key 配置属性。
 * @param value 新值。
 */
function setSetting(key: keyof FlowPrintSettings, value: string | boolean | string[]): void {
  emit('update', { ...props.settings, [key]: value })
}
/**
 * 选择一个要打印的审批字段。
 * @param key 字段 key。
 * @param enabled 是否打印。
 */
function selectField(key: string, enabled: boolean): void {
  const fieldKeys = enabled ? [...props.settings.fieldKeys, key] : props.settings.fieldKeys.filter(item => item !== key)
  setSetting('fieldKeys', [...new Set(fieldKeys)])
}
</script>

<template>
  <section class="print-panel"><header><span>审批单</span><h2>A4 打印设置</h2><p>只配置审批服务保存的数据与审批记录，不改变业务系统原单。中文打印字体由单位在管理后台上传。</p></header>
    <label class="orientation">纸张方向<select :value="settings.orientation" @change="setSetting('orientation', ($event.target as HTMLSelectElement).value)"><option value="portrait">纵向</option><option value="landscape">横向</option></select></label>
    <h3>打印字段</h3><div class="field-grid"><label v-for="field in fields" :key="field.key"><input type="checkbox" :checked="settings.fieldKeys.includes(field.key)" @change="selectField(field.key, ($event.target as HTMLInputElement).checked)" />{{ field.label }}</label><p v-if="!fields.length">请先在“审批数据”页定义字段。</p></div>
    <h3>附加内容</h3><div class="field-grid"><label v-for="toggle in toggles" :key="toggle.key"><input type="checkbox" :checked="settings[toggle.key]" @change="setSetting(toggle.key, ($event.target as HTMLInputElement).checked)" />{{ toggle.label }}</label></div>
  </section>
</template>

<style scoped>
.print-panel{width:100%;min-width:0;padding:24px;border:1px solid var(--line);border-radius:6px;background:#fff}.print-panel header span{color:var(--blue);font-size:11px;font-weight:700}.print-panel h2{margin:4px 0 8px;font-size:19px}.print-panel header p,.field-grid p{color:var(--muted);line-height:1.7}.print-panel h3{margin:24px 0 12px;padding-top:18px;border-top:1px solid var(--line);font-size:15px}.orientation{display:grid;gap:6px;max-width:260px;margin-top:22px;font-weight:600}.orientation select{padding:8px;border:1px solid #cbd5e1;border-radius:5px;background:#fff}.field-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:13px}.field-grid label{display:flex;align-items:center;gap:7px}.field-grid input{accent-color:var(--blue)}@media(max-width:700px){.field-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:460px){.field-grid{grid-template-columns:1fr}}
</style>

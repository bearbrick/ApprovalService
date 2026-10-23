<script setup lang="ts">
import { computed } from 'vue'
import type { FlowDataDefinition, FlowEdge, FlowNode } from '../../model/flow.ts'
import ConditionEditor from './ConditionEditor.vue'

/**
 * 连线属性面板的输入。
 */
const props = defineProps<{
  /**
   * 当前选中的连线。
   */
  edge: FlowEdge
  /**
   * 全部节点，用于显示起止位置和分支规则。
   */
  nodes: FlowNode[]
  /**
   * 可供条件引用的字段。
   */
  data: FlowDataDefinition
  /**
   * 当前连线的发布问题。
   */
  issues: string[]
}>()
/**
 * 连线配置变化。
 */
const emit = defineEmits<{
  /**
   * 返回更新后的连线对象。
   */
  update: [value: FlowEdge]
}>()
/**
 * 起点网关决定连线是否支持条件。
 */
const source = computed(() => props.nodes.find(node => node.id === props.edge.sourceId))
/**
 * 目标节点，供面板展示。
 */
const target = computed(() => props.nodes.find(node => node.id === props.edge.targetId))
/**
 * 排他或包容分支的连线可以设置条件。
 */
const conditional = computed(() => source.value?.gatewayDirection === 'split' && (source.value.type === 'exclusiveGateway' || source.value.type === 'inclusiveGateway'))

/**
 * 更新连线属性。
 * @param key 要修改的属性。
 * @param value 新值。
 */
function setEdge(key: 'priority' | 'isDefault' | 'condition', value: number | boolean | string | null): void {
  const next = { ...props.edge, [key]: value }
  // 兜底线必须没有条件；取消兜底后由设计人员补条件。
  if (key === 'isDefault' && value === true) next.condition = null
  emit('update', next)
}
</script>

<template>
  <div class="edge-inspector">
    <header><span class="kind">连线</span><h2>路线属性</h2><small>{{ edge.id }}</small></header>
    <div v-if="issues.length" class="issue-box" role="alert"><strong>发布检查</strong><p v-for="issue in issues" :key="issue">{{ issue }}</p></div>
    <dl class="edge-path"><dt>从</dt><dd>{{ source?.name ?? edge.sourceId }}</dd><dt>到</dt><dd>{{ target?.name ?? edge.targetId }}</dd></dl>
    <template v-if="conditional">
      <label class="check-row"><input type="checkbox" :checked="edge.isDefault" @change="setEdge('isDefault', ($event.target as HTMLInputElement).checked)" />设为“其他情况”兜底线</label>
      <template v-if="!edge.isDefault"><label class="form-row">优先级（数字越小越先尝试）<input type="number" :value="edge.priority" @change="setEdge('priority', Number(($event.target as HTMLInputElement).value))" /></label><ConditionEditor :condition="edge.condition ?? ''" :data="data" @update:condition="setEdge('condition', $event)" /></template>
      <p v-else class="hint">每个条件分叉恰好保留一条兜底线；它不写条件。包容分支先走所有命中路线，均未命中时才走兜底。</p>
    </template>
    <p v-else class="hint">该路线直接连接两个节点。请在排他或包容分叉的出线上配置条件和兜底。</p>
  </div>
</template>

<style scoped>
.edge-inspector{display:grid;gap:14px;padding:18px 16px}.edge-inspector header{padding-bottom:12px;border-bottom:1px solid var(--line)}.edge-inspector h2{font-size:17px}.edge-inspector small{display:block;color:var(--muted);font:11px Consolas,monospace}.kind{color:var(--blue);font-size:11px;font-weight:700}.edge-path{display:grid;grid-template-columns:32px 1fr;gap:8px;padding:12px;margin:0;background:#f8faff;border:1px solid var(--line);border-radius:4px}.edge-path dt{color:var(--muted)}.edge-path dd{margin:0;font-weight:600}.form-row{display:grid;gap:5px;font-size:12px;font-weight:600}.form-row input{width:100%;padding:7px;border:1px solid #cbd5e1;border-radius:4px}.check-row{display:flex;align-items:center;gap:7px}.hint{color:var(--muted);font-size:12px;line-height:1.6}.issue-box{padding:10px;border:1px solid #f0c8ca;border-radius:4px;background:#fff6f6;color:#9b3540}.issue-box p{margin-top:6px;font-size:12px}
</style>

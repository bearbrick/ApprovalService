<script setup lang="ts">
/**
 * 统一展示尚无内容、未配置或查询无结果时的说明。
 */
import type { VNode } from 'vue'
import UiIcon from './UiIcon.vue'
import type { IconName } from './icons.ts'

/**
 * 空状态的标题、说明和可选图标。
 */
withDefaults(defineProps<{
  /**
   * 简洁说明当前为空的原因。
   */
  title: string
  /**
   * 告知用户当前可以做什么或下一步如何继续。
   */
  description: string
  /**
   * 装饰图标，默认使用通用说明图标。
   */
  icon?: IconName
}>(), { icon: 'info' })

/**
 * 可选操作区，由调用方传入已有的按钮或链接。
 */
defineSlots<{
  /**
   * 提供当前空状态下可以执行的操作。
   * @returns 操作按钮或链接节点。
   */
  action?(): VNode[]
}>()
</script>

<template>
  <section class="ui-empty-state">
    <div class="ui-empty-state__icon"><UiIcon :name="icon" /></div>
    <h2 class="ui-empty-state__title">{{ title }}</h2>
    <p class="ui-empty-state__description">{{ description }}</p>
    <div v-if="$slots.action" class="ui-empty-state__action"><slot name="action" /></div>
  </section>
</template>

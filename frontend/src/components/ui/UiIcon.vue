<script setup lang="ts">
/**
 * 只渲染本地审核过的图标标记，不接收任意 SVG 内容。
 */
import { ICON_MARKUP, type IconName } from './icons.ts'

/**
 * 图标属性；无文字标签时视为装饰图形。
 */
defineProps<{
  /**
   * 最小图标集合中的名称。
   */
  name: IconName
  /**
   * 独立图像的可访问名称；按钮内的装饰图标不需要提供。
   */
  label?: string
}>()
</script>

<template>
  <svg
    class="ui-icon"
    viewBox="0 0 16 16"
    focusable="false"
    :role="label ? 'img' : undefined"
    :aria-label="label"
    :aria-hidden="label ? undefined : true"
    v-html="ICON_MARKUP[name]"
  />
</template>

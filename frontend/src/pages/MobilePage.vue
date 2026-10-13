<script setup lang="ts">
/**
 * 手机端审批中心（#/m）：按手机单独设计的布局，底部四个标签。骨架阶段只有占位说明。
 */

/**
 * 底部标签，按显示顺序。
 */
const tabs = ['待办', '已办', '抄送', '我的']
</script>

<template>
  <div class="mobile">
    <header>审批</header>
    <main>
      <section class="panel">
        <h1>手机端</h1>
        <p>和电脑端用同一套接口；拍照加附件、手写签名在这里做。</p>
      </section>
    </main>
    <nav class="tabs">
      <span v-for="(tab, index) in tabs" :key="tab" :class="{ active: index === 0 }">{{ tab }}</span>
    </nav>
  </div>
</template>

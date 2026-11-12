<script setup lang="ts">
/**
 * PageHead —— 页面标题栏（标题 + 副标题/口径 + 右侧操作）
 *
 * 为什么要统一它：原来各页面自己写 <h3> + 各种间距，字号 16/18/20 混用，
 * 间距 8/12/16 不一，20 多个页面看起来像 20 个系统。统一到一个组件后，
 * 全站页头的字号、间距、图标颜色只改这一处。
 */
withDefaults(
  defineProps<{
    title: string
    sub?: string
    icon?: string
  }>(),
  { sub: '', icon: '' }
)
</script>

<template>
  <div class="page-head">
    <div class="page-head__main">
      <div class="page-head__title">
        <el-icon v-if="icon"><component :is="icon" /></el-icon>
        <span>{{ title }}</span>
      </div>
      <div v-if="sub" class="page-head__sub">{{ sub }}</div>
    </div>

    <div v-if="$slots.actions" class="page-head__actions">
      <slot name="actions" />
    </div>
  </div>
</template>

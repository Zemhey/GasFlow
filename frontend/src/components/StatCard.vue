<script setup lang="ts">
/**
 * StatCard —— 指标卡（看板 / 报表顶部 KPI）
 *
 * 视觉层次：左侧语义色条（--stat-color 由 tone 决定）→ 标签（小、灰）
 * → 数值（大、等宽、最重）→ 说明（小、最灰）。四级层次让"数字"最醒目。
 */
withDefaults(
  defineProps<{
    label: string
    value: string | number
    unit?: string
    sub?: string
    /** 语义色：决定左侧色条，也是唯一用颜色的地方 */
    tone?: 'brand' | 'success' | 'warning' | 'danger' | 'info'
    icon?: string
    /** 趋势指示：涨/跌，配合 trendText */
    trend?: 'up' | 'down' | ''
    trendText?: string
  }>(),
  {
    unit: '',
    sub: '',
    tone: 'brand',
    icon: '',
    trend: '',
    trendText: ''
  }
)
</script>

<template>
  <div class="stat-card" :class="tone !== 'brand' ? `stat-card--${tone}` : ''">
    <div class="stat-card__label">
      <el-icon v-if="icon"><component :is="icon" /></el-icon>
      <span>{{ label }}</span>
    </div>

    <div class="stat-card__value">
      {{ value }}
      <small v-if="unit">{{ unit }}</small>
    </div>

    <div v-if="sub" class="stat-card__sub" v-html="sub" />

    <div v-if="trend" class="stat-card__trend" :class="trend">
      <el-icon><Top v-if="trend === 'up'" /><Bottom v-else /></el-icon>
      {{ trendText }}
    </div>
  </div>
</template>

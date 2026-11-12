<script setup lang="ts">
/**
 * MoneyText —— 金额/数字显示（等宽、千分位、正负色、支持单位）
 *
 * 为什么需要它：金额在原来各页面用 toLocaleString 各写一遍（有的保留 2 位、
 * 有的不保留、有的右对齐有的不右），导致同一张表里"一列钱"都不齐。
 * 这里统一：等宽字体 + 固定小数位 + 千分位 + 符号着色。
 */
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    value: number | string | null | undefined
    /** 小数位（金额默认 2；数量传 0） */
    digits?: number
    /** 是否显示 ¥ 前缀 */
    prefix?: boolean
    /** 单位后缀，如 "kg" / "只" */
    unit?: string
    /** 按正负着色：正绿负红（库存变动、毛利用） */
    colored?: boolean
    /** 是否带正号（变动量用） */
    signed?: boolean
    /** 是否千分位 */
    grouped?: boolean
    /** 空值显示 */
    placeholder?: string
  }>(),
  {
    digits: 2,
    prefix: false,
    unit: '',
    colored: false,
    signed: false,
    grouped: true,
    placeholder: '—'
  }
)

const 数值 = computed(() => {
  const n = Number(props.value)
  return Number.isFinite(n) ? n : null
})

const 文本 = computed(() => {
  const n = 数值.value
  if (n === null) return props.placeholder
  const 体 = Math.abs(n).toLocaleString('zh-CN', {
    minimumFractionDigits: props.digits,
    maximumFractionDigits: props.digits,
    useGrouping: props.grouped
  })
  const 符号 = n < 0 ? '-' : props.signed && n > 0 ? '+' : ''
  return `${符号}${props.prefix ? '¥' : ''}${体}`
})

const 色调 = computed(() => {
  if (!props.colored) return ''
  const n = 数值.value
  if (n === null || n === 0) return 'flat'
  return n > 0 ? 'up' : 'down'
})
</script>

<template>
  <span class="num" :class="色调">
    {{ 文本 }}<small v-if="unit" class="unit">{{ unit }}</small>
  </span>
</template>

<style scoped>
.unit {
  margin-left: 2px;
  font-family: var(--font-sans);
  font-size: 0.85em;
  color: var(--c-ink-400);
}
</style>

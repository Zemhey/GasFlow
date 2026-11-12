<script setup lang="ts">
/**
 * DataState —— 数据状态包装（加载中 / 失败 / 空 / 有数据）
 *
 * 把"四种状态"收进一个组件，避免每个页面各写一套 v-loading + v-if="!行.length"
 * + el-alert（原来 20 个页面有 20 种写法，失败时有的只弹 toast，页面一片空白）。
 *
 * 用法：
 *   <DataState :loading="加载中" :error="错误" :empty="!行.length" @retry="加载">
 *     <el-table :data="行" />
 *   </DataState>
 */
withDefaults(
  defineProps<{
    loading?: boolean
    error?: string
    /** 是否空数据（由调用方判断，因为"空"的语义各页不同） */
    empty?: boolean
    /** 空态文案（说明"为什么空 + 下一步做什么"） */
    emptyTitle?: string
    emptyDesc?: string
    /** 空态图标 */
    emptyIcon?: string
    /** 是否显示骨架屏（默认 true）；表格类页面建议开启，避免布局跳动 */
    skeleton?: boolean
    /** 骨架行数 */
    skeletonRows?: number
    /** 骨架类型：table 三列、stat 指标卡 */
    skeletonType?: 'table' | 'stat'
  }>(),
  {
    loading: false,
    error: '',
    empty: false,
    emptyTitle: '暂无数据',
    emptyDesc: '',
    emptyIcon: 'DocumentDelete',
    skeleton: true,
    skeletonRows: 8,
    skeletonType: 'table'
  }
)

defineEmits<{ retry: [] }>()
</script>

<template>
  <!-- 1) 失败：就地展示原因 + 重试，不用 toast 一闪而过 -->
  <div v-if="error" class="error-panel">
    <el-icon><CircleCloseFilled /></el-icon>
    <div class="error-panel__body">
      <div>{{ error }}</div>
      <div v-if="$slots.errorDetail" class="text-xs mt-1">
        <slot name="errorDetail" />
      </div>
    </div>
    <el-button class="error-panel__retry" size="small" @click="$emit('retry')">
      <el-icon><Refresh /></el-icon> 重试
    </el-button>
  </div>

  <!-- 2) 加载中：骨架屏，保持与真实内容同高，加载完不跳动 -->
  <template v-else-if="loading">
    <slot v-if="!skeleton" name="loading">
      <div v-loading="true" class="skeleton-table" style="min-height: 200px" />
    </slot>

    <div v-else-if="skeletonType === 'stat'" class="stat-grid">
      <div v-for="i in 4" :key="i" class="skeleton-stat">
        <div class="skeleton" />
        <div class="skeleton" />
      </div>
    </div>

    <div v-else class="skeleton-table">
      <div v-for="i in skeletonRows" :key="i" class="skeleton-row">
        <div class="skeleton" />
        <div class="skeleton" />
        <div class="skeleton" />
      </div>
    </div>
  </template>

  <!-- 3) 空：图标 + 主文案 + 说明 + 可选操作 -->
  <div v-else-if="empty" class="empty-state">
    <div class="empty-state__icon">
      <el-icon><component :is="emptyIcon" /></el-icon>
    </div>
    <div class="empty-state__title">{{ emptyTitle }}</div>
    <div v-if="emptyDesc" class="empty-state__desc">{{ emptyDesc }}</div>
    <div v-if="$slots.emptyAction" class="mt-2">
      <slot name="emptyAction" />
    </div>
  </div>

  <!-- 4) 有数据 -->
  <slot v-else />
</template>

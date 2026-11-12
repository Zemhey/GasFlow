# 前端设计规范（v3 改版 · 所有页面必须遵守）

> 这份文档是**唯一**的样式契约。写页面前先读它；
> 页面里只允许用这里列出的**令牌**与**全局类**，不允许再自己写颜色值和间距魔法数字。

---

## 1. 分层与职责

| 文件 | 负责什么 | 改它会影响 |
|---|---|---|
| `styles/01-设计令牌.css` | 颜色 / 字体 / 间距 / 圆角 / 阴影 / 动效 / 层级 | 全站配色与节奏 |
| `styles/02-基础与重置.css` | reset、排版基线、通用原子类 | 全局 |
| `styles/03-组件主题.css` | Element Plus 变量映射 + 组件细节 | 所有 el-* 组件 |
| `styles/04-页面模式.css` | 页头 / 工具条 / 指标卡 / 合计条 / 打印 | 页面骨架 |
| `styles/07-布局.css` | 侧栏 / 顶栏 / 内容区 / 抽屉 | 应用外壳 |
| `styles/05-响应式.css` | 断点降级 | 全站 |
| `styles/06-交互反馈.css` | 骨架屏 / 错误 / 空态 / 微交互 | 全站 |

**页面 `<style scoped>` 里只允许**：
1. 该页面**独有**的布局（例如某个特殊面板的 grid）；
2. 对 Element Plus 深层的局部微调（用 `:deep()`）；
3. 引用令牌（`var(--sp-4)`、`var(--c-ink-400)`…）。

**不允许**在页面里重复定义这些全局类：`.toolbar` `.filter-bar` `.stat-grid`
`.stat-card` `.sum-bar` `.caliber-note` `.table-wrap` `.empty-state` `.row`
`.col` `.spacer` `.page-head` `.legend` `.chart-box`。

---

## 2. 可用令牌（节选，完整见 01 号文件）

```
颜色    --c-brand-500(主色) --c-accent-500(琥珀) --c-ink-900/700/500/400/300/200/100/50
        --c-success-500 --c-warning-500 --c-danger-500 --c-info-500
        --c-up(绿) --c-down(红) --c-flat(灰) --c-debit(蓝) --c-credit(红)
        --bg-page --bg-surface --bg-surface-alt --bg-sunken --bg-hover --bg-far(退后的表面)
光色    --lit-warm(受光顶边) --rim-lit(边缘光，代替硬描边) --surface-sheen(受光面)
        --glow-brand --glow-accent(关键元素用透光，不用描边)
        --sh-ink(有色阴影) --page-wash/--page-brushes(页面底的大气层，只在 02/07 用)
间距    --sp-1(4) --sp-2(8) --sp-3(12) --sp-4(16) --sp-5(20) --sp-6(24) --sp-8(32) --sp-10(40)
字号    --fs-xs(12) --fs-sm(13) --fs-base(14) --fs-md(15) --fs-lg(16) --fs-xl(18) --fs-2xl(22)
字重    --fw-normal/medium(500)/semibold(600)/bold(700)
圆角    --r-xs(4) --r-sm(6) --r-md(8) --r-lg(12) --r-xl(16，卡片/对话框这类浮起的面) --r-full
阴影    --sh-xs --sh-sm --sh-md --sh-lg --sh-xl
动效    --dur-fast(120ms) --dur-base(200ms) --ease-out --ease-in-out
字体    --font-sans --font-num(等宽，数字/金额专用)
```

> 光色那几条不是装饰性可选项：新页面里的卡片/面板请一律用
> `background: var(--surface-sheen), var(--bg-surface)` + `box-shadow: var(--sh-sm), var(--rim-lit)`
> 这一组，别再写平涂白 + 硬描边 —— 那是"每个色块各自打光"，整页没有同一束光。

## 3. 可用全局类

| 类 | 用途 |
|---|---|
| `.page-head` / `.page-head__title` / `__sub` / `__actions` | 页头（**优先用 `<PageHead>` 组件**） |
| `.toolbar` / `.filter-bar` / `.filter-bar__label` | 工具条 / 筛选条（`filter-bar` 是浅底卡片） |
| `.stat-grid` / `.stat-card` / `--success/--warning/--danger/--info` | 指标卡网格（**优先用 `<StatCard>`**） |
| `.sum-bar` / `.sum-bar__danger` | 表格下方合计条 |
| `.caliber-note` | 页面底部口径说明（小字、左竖线） |
| `.table-wrap` / `.table-head` / `.table-scroll-hint` | 表格外框（圆角裁切 + 窄屏横滚渐隐） |
| `.legend` / `.legend__item` / `.legend__swatch` | 颜色图例 |
| `.empty-state` / `.empty-state__icon` / `__title` / `__desc` | 空态（**优先用 `<DataState>`**） |
| `.error-panel` / `.error-panel__body` / `__retry` | 就地错误 + 重试（**用 `<DataState>` 的 error 属性**） |
| `.skeleton` / `.skeleton-table` / `.skeleton-row` / `.skeleton-stat` | 骨架屏（`<DataState>` 内置） |
| `.num` / `.up` / `.down` / `.flat` / `.text-muted` / `.text-xs` / `.hint` | 数字等宽 / 增减色 / 文本层级 |
| `.row` / `.row-wrap` / `.col` / `.spacer` | 弹性布局三件套 + 撑开 |
| `.mt-1..4` / `.mb-2..4` / `.gap-1..3` | 间距工具类 |
| `.chart-box` / `--sm` / `--lg` / `.chart-empty` | 图表容器与图表空态 |
| `.mobile-filter-toggle` / `.filter-collapsible` / `.is-open` | 移动端筛选抽屉（≤767px 才出现开关） |
| `.icon-btn` | 顶栏图标按钮（仅布局用） |
| `.back-top` `.badge` `.status-dot` `.info-dot` `.link-text` `.clickable` | 微交互件 |

## 4. 可用组件

| 组件 | 属性 | 说明 |
|---|---|---|
| `@/components/PageHead.vue` | `title` `sub` `icon` + `#actions` | 页面标题栏 |
| `@/components/StatCard.vue` | `label` `value` `unit` `sub` `tone` `icon` `trend` `trendText` | 指标卡（`sub` 支持 HTML，`<b>` 包数字） |
| `@/components/DataState.vue` | `loading` `error` `empty` `emptyTitle` `emptyDesc` `emptyIcon` `skeleton` `skeletonType` `skeletonRows`，`@retry`，`#emptyAction` `#errorDetail` | 四态包装（加载/失败/空/有数据） |
| `@/components/MoneyText.vue` | `value` `digits` `prefix` `unit` `colored` `signed` `grouped` | 金额/数字（等宽+千分位+正负色） |

---

## 5. 标准页面骨架（照抄改内容）

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import PageHead from '@/components/PageHead.vue'
import DataState from '@/components/DataState.vue'
import StatCard from '@/components/StatCard.vue'

const 行 = ref<any[]>([])
const 加载中 = ref(false)
const 错误 = ref('')

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    行.value = (await api.xxx()) ?? []
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '加载失败，请稍后重试'
    行.value = []
  } finally {
    加载中.value = false
  }
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead title="标题" icon="Document" sub="副标题/口径说明">
      <template #actions>
        <el-button @click="加载">刷新</el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard label="指标" :value="0" sub="说明 <b>数字</b>" />
    </div>

    <el-card shadow="never">
      <div class="filter-bar">
        <el-input v-model="关键词" placeholder="搜索" clearable style="width: 220px">
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <el-button type="primary" @click="加载">查询</el-button>
        <span class="spacer" />
        <span class="hint">共 {{ 行.length }} 条</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        empty-title="暂无数据"
        empty-desc="说明为什么空 + 下一步做什么"
        empty-icon="Document"
        @retry="加载"
      >
        <div class="table-wrap table-scroll-hint">
          <el-table :data="行" size="small" stripe max-height="620">
            <el-table-column prop="列名" label="列" min-width="120" show-overflow-tooltip />
          </el-table>
        </div>
        <el-pagination class="mt-3" style="justify-content: flex-end" background
                       layout="total, sizes, prev, pager, next" :total="总数"
                       v-model:current-page="页" v-model:page-size="尺寸" />
      </DataState>

      <div class="caliber-note"><b>口径</b>：写清每个数字从哪来。</div>
    </el-card>
  </div>
</template>
```

### 硬性要求

1. **禁止** `height="calc(100vh - 268px)"`（侧栏折叠/移动端会算错）—— 用
   `max-height="620"` 或 `max-height="calc(100vh - 320px)"`。
2. **金额/数量列**：`align="right"` + 类 `num`（或直接用 `<MoneyText>`）。
   这不是审美问题：等宽才能让一列钱对齐。
3. **状态列**用 `<el-tag :type="..." size="small" effect="light">`，
   颜色语义与 `单据状态` 一致（0 草稿 info / 1 已提交 primary / 2 已审核 warning /
   3 已过账 success / 9 已冲销 danger）。
4. **空态必须说明原因**并给下一步（`emptyDesc` + `#emptyAction`），不要只显示"无数据"。
5. **失败必须就地显示 + 可重试**（`error` 属性），不要只弹一个 toast 就留白页。
6. **加载中用骨架屏**（默认行为），不要用整页 `v-loading` 遮罩 —— 避免布局跳动。
7. 移动端：宽表格允许横滚（`.table-wrap` + `table-scroll-hint`），
   筛选条用 `.filter-collapsible` + `.mobile-filter-toggle` 收起。
8. **不改任何接口调用、字段名、业务逻辑**；只动呈现层。
   允许补：格式化、空/错/载状态、导出 CSV、图例、口径说明、键盘可达性。
9. 中文文案要**准确、专业、简洁**：说明"这个数从哪来"，不写"数据"之类空话。
10. 写完全部跑：`npx vue-tsc --noEmit` 和 `npx vite build`，**必须都通过**。

---

## 6. ECharts 图表规范

```ts
// 颜色必须从令牌取（暗色模式才跟着变），禁止写死十六进制
function 取色(变量名: string, 兜底: string) {
  return getComputedStyle(document.documentElement).getPropertyValue(变量名).trim() || 兜底
}
// 主题切换要重绘：MutationObserver 监听 documentElement 的 class 变化
```
- `grid` 留白足够（左 60+ / 右 40+ / 上 40 / 下 30）。
- 双 Y 轴时右轴 `splitLine: { show: false }`。
- 用 `--c-brand-500`（线/主）与 `--c-accent-500`（柱/对比）两色语义。
- `label` 字号 11，`xAxis.axisTick.show = false`。
- 数据为空时渲染 `.chart-empty`（带图标 + 原因），不要画一个空坐标系。

---

## 7. 响应式断点（已在 05 号文件实现，页面不要重复写）

| 断点 | 行为 |
|---|---|
| ≥1600 | 内容限宽居中（`.app-main__inner`） |
| ≤1199 | 压缩内边距、指标卡变小 |
| ≤991 | 侧栏变抽屉；工具条换行；输入框自适应宽度；表格横滚 |
| ≤767 | 指标卡两列；工具条纵向铺满；分页只留前后页；对话框全屏；筛选条收起 |

页面只负责：**内容在窄屏下仍然可读**（不要写死像素宽度的容器、不要横向溢出）。

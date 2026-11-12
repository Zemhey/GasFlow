<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import * as echarts from 'echarts'
import api, { type 看板数据 } from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'
import MoneyText from '@/components/MoneyText.vue'

const router = useRouter()
const 数据 = ref<看板数据 | null>(null)
const 加载中 = ref(true)
const 错误 = ref('')
const 刷新时间 = ref('')

const 趋势容器 = shallowRef<HTMLElement | null>(null)
const 规格容器 = shallowRef<HTMLElement | null>(null)
/** echarts 实例统一保存，卸载/重绘前必须 dispose，避免 resize 监听与实例泄漏 */
let 趋势实例: echarts.ECharts | null = null
let 规格实例: echarts.ECharts | null = null
/** ResizeObserver：容器尺寸变化时重绘（窗口 resize 之外，侧栏折叠也要响应） */
let 观察器: ResizeObserver | null = null

const 截止日 = computed(() => String(数据.value?.截止 ?? '').slice(0, 10))
const 截止月 = computed(() => (截止日.value ? 截止日.value.slice(0, 7) : ''))

const 今日 = computed(() => 数据.value?.今日 ?? { 单数: 0, 金额: 0, 销量: 0 })
const 本月 = computed(() => 数据.value?.本月 ?? { 单数: 0, 金额: 0, 销量: 0 })
const 进气 = computed(() => 数据.value?.进气 ?? { 单数: 0, 金额: 0 })
const 库存 = computed(() => 数据.value?.库存 ?? { 实瓶: 0, 空瓶: 0, 在借: 0, 库存金额: 0 })
const 客户 = computed(() => 数据.value?.客户 ?? { 总数: 0, 活跃数: 0 })
const 应收 = computed(() => 数据.value?.应收 ?? { 总额: 0, 逾期90天: 0 })

/** 逾期占比：用于卡片上给"风险"一个可解释的比例 */
const 逾期占比 = computed(() => {
  const t = Number(应收.value.总额 || 0)
  if (t <= 0) return 0
  return Math.round((Number(应收.value.逾期90天 || 0) / t) * 100)
})

/** 客户活跃率 */
const 活跃率 = computed(() => {
  const t = Number(客户.value.总数 || 0)
  if (t <= 0) return 0
  return Math.round((Number(客户.value.活跃数 || 0) / t) * 100)
})

const 有趋势 = computed(() => (数据.value?.趋势 ?? []).length > 0)
const 有规格 = computed(() => (数据.value?.规格占比 ?? []).length > 0)

/** 读取主题令牌里的实际颜色值：图表颜色必须跟着主题走，不能写死 */
function 取色(变量名: string, 兜底: string): string {
  const v = getComputedStyle(document.documentElement).getPropertyValue(变量名).trim()
  return v || 兜底
}

function 画图() {
  const 文本色 = 取色('--c-ink-400', '#8b96a3')
  const 网格色 = 取色('--c-ink-100', '#e8ecf1')
  const 品牌色 = 取色('--c-brand-500', '#2b7de9')
  const 强调色 = 取色('--c-accent-500', '#f5a623')
  const 表面色 = 取色('--bg-surface', '#fff')
  const 边框色 = 取色('--c-border', '#d5dbe3')
  const 字族 = 取色('--font-sans', 'system-ui')

  const t = 数据.value?.趋势 ?? []
  if (趋势容器.value && t.length) {
    if (!趋势实例) 趋势实例 = echarts.init(趋势容器.value, undefined, { renderer: 'canvas' })
    趋势实例.setOption({
      // 网格留白：左给 Y 轴标签，右给第二个 Y 轴，底部给 X 轴
      grid: { left: 64, right: 56, top: 40, bottom: 34, containLabel: false },
      textStyle: { fontFamily: 字族, color: 文本色 },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'cross', crossStyle: { color: 文本色 }, label: { backgroundColor: 取值深色() } },
        backgroundColor: 表面色,
        borderColor: 边框色,
        borderWidth: 1,
        padding: [8, 12],
        textStyle: { color: 取色('--c-ink-800', '#24313f'), fontSize: 12 },
        valueFormatter: (v: unknown) => Number(v as number).toLocaleString('zh-CN')
      },
      legend: {
        data: ['金额', '单数'],
        top: 6,
        right: 8,
        icon: 'roundRect',
        itemWidth: 10,
        itemHeight: 10,
        textStyle: { color: 文本色, fontSize: 11 }
      },
      xAxis: {
        type: 'category',
        boundaryGap: true,
        data: t.map((x) => String(x.日期 ?? '').slice(5)),
        axisLine: { lineStyle: { color: 网格色 } },
        axisTick: { show: false },
        axisLabel: { fontSize: 11, color: 文本色, margin: 10 }
      },
      yAxis: [
        {
          type: 'value',
          name: '金额',
          nameTextStyle: { fontSize: 11, color: 文本色, padding: [0, 0, 6, 0] },
          axisLine: { show: false },
          axisTick: { show: false },
          axisLabel: {
            fontSize: 11,
            color: 文本色,
            formatter: (v: number) => (v >= 10000 ? `${v / 10000}万` : String(v))
          },
          splitLine: { lineStyle: { color: 网格色, type: 'dashed' } }
        },
        {
          type: 'value',
          name: '单数',
          nameTextStyle: { fontSize: 11, color: 文本色, padding: [0, 0, 6, 0] },
          axisLine: { show: false },
          axisTick: { show: false },
          axisLabel: { fontSize: 11, color: 文本色 },
          splitLine: { show: false }
        }
      ],
      series: [
        {
          name: '金额',
          type: 'line',
          smooth: 0.35,
          showSymbol: false,
          symbolSize: 6,
          // 渐变面积：上浓下淡，让"趋势"更有体积感
          areaStyle: {
            color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
              { offset: 0, color: 品牌色 + '55' },
              { offset: 1, color: 品牌色 + '05' }
            ])
          },
          lineStyle: { width: 2.5, color: 品牌色 },
          itemStyle: { color: 品牌色 },
          emphasis: { focus: 'series' },
          data: t.map((x) => Number(x.金额 ?? 0))
        },
        {
          name: '单数',
          type: 'bar',
          yAxisIndex: 1,
          barWidth: 10,
          itemStyle: { color: 强调色 + '99', borderRadius: [3, 3, 0, 0] },
          emphasis: { itemStyle: { color: 强调色 } },
          data: t.map((x) => Number(x.单数 ?? 0))
        }
      ]
    }, true)
  }

  const s = 数据.value?.规格占比 ?? []
  if (规格容器.value && s.length) {
    if (!规格实例) 规格实例 = echarts.init(规格容器.value, undefined, { renderer: 'canvas' })
    规格实例.setOption({
      textStyle: { fontFamily: 字族, color: 文本色 },
      tooltip: {
        trigger: 'item',
        backgroundColor: 表面色,
        borderColor: 边框色,
        borderWidth: 1,
        padding: [8, 12],
        textStyle: { color: 取色('--c-ink-800', '#24313f'), fontSize: 12 },
        formatter: '{b}<br/>{c} 瓶 · {d}%'
      },
      legend: {
        bottom: 0,
        type: 'scroll',
        icon: 'circle',
        itemWidth: 8,
        itemHeight: 8,
        textStyle: { fontSize: 11, color: 文本色 }
      },
      // 用品牌→强调的色带，保持与趋势图同一套语义色
      color: [品牌色, 强调色, '#5eb0a0', '#8e7cc3', '#e0709a', '#7f8fa6', '#c98b4b', '#4aa8c9'],
      series: [
        {
          type: 'pie',
          radius: ['46%', '68%'],
          center: ['50%', '43%'],
          avoidLabelOverlap: true,
          padAngle: 2,
          itemStyle: { borderRadius: 4, borderColor: 表面色, borderWidth: 2 },
          label: {
            formatter: '{b}\n{d}%',
            fontSize: 11,
            color: 文本色,
            lineHeight: 14
          },
          labelLine: { length: 8, length2: 8, lineStyle: { color: 网格色 } },
          emphasis: {
            scaleSize: 6,
            label: { fontSize: 12, fontWeight: 'bold', color: 取色('--c-ink-900', '#17212f') }
          },
          data: s.map((x) => ({ name: String(x.规格 ?? ''), value: Number(x.数量 ?? 0) }))
        }
      ]
    }, true)
  }
}

/** tooltip 轴指示器的深色标签底：跟随主题 */
function 取值深色(): string {
  // ⚠ 批次 13 修：原来写成 getComputedStyle(document.documentElement).classList.contains('dark')，
  // 而 getComputedStyle 返回的是 CSSStyleDeclaration，**根本没有 classList** →
  // 每次都抛 "Cannot read properties of undefined (reading 'contains')"，
  // 被 加载() 的 catch 吞成"看板数据加载失败"，于是首页永远只有错误面板（与接口 500 无关的独立 bug）。
  return document.documentElement.classList.contains('dark') ? '#38424f' : '#4a5866'
}

function 尺寸变化() {
  趋势实例?.resize()
  规格实例?.resize()
}

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    数据.value = await api.dashboard()
    刷新时间.value = new Date().toLocaleTimeString('zh-CN', { hour12: false })
    await nextTick()
    画图()
  } catch (e) {
    // 不要静默吞：批次 13 就是靠这行才定位到"接口成功、画图抛错"的
    console.error('[看板] 加载失败', e)
    错误.value = '看板数据加载失败，请检查后端服务与网络后重试'
  } finally {
    加载中.value = false
  }
}

/** 主题切换 / 侧栏折叠后需要按新配色重绘（echarts 的 canvas 不会自动跟随 CSS 变量） */
function 重绘图表() {
  if (!数据.value) return
  销毁图表()
  nextTick(() => 画图())
}

onMounted(async () => {
  window.addEventListener('resize', 尺寸变化)
  // 观察容器尺寸：侧栏折叠、窗口变化都会触发，比单纯 resize 更可靠
  if (typeof ResizeObserver !== 'undefined') {
    观察器 = new ResizeObserver(() => 尺寸变化())
    if (趋势容器.value) 观察器.observe(趋势容器.value)
    if (规格容器.value) 观察器.observe(规格容器.value)
  }
  // 主题切换时重绘（令牌变了，图表要跟着换色）
  const 主题观察 = new MutationObserver(() => 重绘图表())
  主题观察.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
  ;(window as any).__dashboard主题观察 = 主题观察

  await 加载()
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', 尺寸变化)
  观察器?.disconnect()
  ;(window as any).__dashboard主题观察?.disconnect()
  销毁图表()
})

function 销毁图表() {
  趋势实例?.dispose()
  规格实例?.dispose()
  趋势实例 = null
  规格实例 = null
}
</script>

<template>
  <div>
    <PageHead
      title="经营看板"
      icon="DataLine"
      :sub="截止日 ? `数据截止 ${截止日} · 口径：仅统计已过账单据` : '今日经营概况与近 14 天走势'"
    >
      <template #actions>
        <span v-if="刷新时间" class="hint">更新于 {{ 刷新时间 }}</span>
        <el-button :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
        <el-button type="primary" @click="router.push('/orders/delivery')">
          <el-icon><Van /></el-icon> 送气单
        </el-button>
      </template>
    </PageHead>

    <DataState
      :loading="加载中 && !数据"
      :error="错误"
      skeleton-type="stat"
      @retry="加载"
    >
      <template v-if="数据">
        <!-- ======================== 第一排：今日 / 本月 ======================== -->
        <div class="stat-grid">
          <StatCard
            label="今日送气"
            icon="Van"
            :value="Number(今日.单数).toLocaleString('zh-CN')"
            unit="单"
            :sub="`金额 <b>¥${Number(今日.金额).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</b> · 销量 <b>${Number(今日.销量).toLocaleString('zh-CN')}</b> kg`"
          />
          <StatCard
            label="本月销售额"
            icon="TrendCharts"
            tone="success"
            :value="`¥${Number(本月.金额).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`"
            :sub="`<b>${Number(本月.单数).toLocaleString('zh-CN')}</b> 单 · <b>${Number(本月.销量).toLocaleString('zh-CN')}</b> kg`"
          />
          <StatCard
            label="本月进气"
            icon="ShoppingCart"
            tone="info"
            :value="`¥${Number(进气.金额).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`"
            :sub="`<b>${Number(进气.单数).toLocaleString('zh-CN')}</b> 张进气单`"
          />
          <StatCard
            label="在外钢瓶"
            icon="Coin"
            tone="warning"
            :value="Number(库存.在借).toLocaleString('zh-CN')"
            unit="只"
            :sub="`实瓶 <b>${Number(库存.实瓶).toLocaleString('zh-CN')}</b> · 空瓶 <b>${Number(库存.空瓶).toLocaleString('zh-CN')}</b> · 库存额 <b>¥${Number(库存.库存金额).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</b>`"
          />
        </div>

        <!-- ======================== 第二排：客户 / 应收 / 结构 ======================== -->
        <div class="stat-grid">
          <StatCard
            label="客户总数"
            icon="User"
            :value="Number(客户.总数).toLocaleString('zh-CN')"
            unit="家"
            :sub="`活跃 <b>${Number(客户.活跃数).toLocaleString('zh-CN')}</b> 家（近 90 天有送气 · ${活跃率}%）`"
          />
          <StatCard
            label="应收总额"
            icon="Wallet"
            tone="danger"
            :value="`¥${Number(应收.总额).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`"
            :sub="`逾期 90 天以上 <b>¥${Number(应收.逾期90天).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</b>（占 ${逾期占比}%）`"
          />
          <StatCard
            label="库存结构"
            icon="Box"
            tone="info"
            :value="Number(库存.实瓶).toLocaleString('zh-CN')"
            unit="实瓶"
            :sub="`空瓶 <b>${Number(库存.空瓶).toLocaleString('zh-CN')}</b> · 在借 <b>${Number(库存.在借).toLocaleString('zh-CN')}</b>`"
          />
        </div>

        <!-- ======================== 图表区 ======================== -->
        <el-row :gutter="16" class="mt-2">
          <el-col :xs="24" :lg="16">
            <el-card shadow="never">
              <template #header>
                <div class="row">
                  <span>近 14 天送气金额与单数</span>
                  <span class="spacer" />
                  <span class="hint">柱=单数（右轴） · 线=金额（左轴）</span>
                </div>
              </template>
              <div v-if="有趋势" ref="趋势容器" class="chart-box chart-box--lg" />
              <div v-else class="chart-box chart-box--lg">
                <div class="chart-empty">
                  <el-icon style="font-size: 32px"><DataLine /></el-icon>
                  <div>近 14 天没有送气记录</div>
                  <div class="text-xs">过账一张送气单后这里就会出现走势</div>
                </div>
              </div>
            </el-card>
          </el-col>

          <el-col :xs="24" :lg="8">
            <el-card shadow="never" class="mt-mobile">
              <template #header>
                <div class="row">
                  <span>规格销量占比</span>
                  <span class="spacer" />
                  <span v-if="截止月" class="hint">{{ 截止月 }}</span>
                </div>
              </template>
              <div v-if="有规格" ref="规格容器" class="chart-box chart-box--lg" />
              <div v-else class="chart-box chart-box--lg">
                <div class="chart-empty">
                  <el-icon style="font-size: 32px"><PieChart /></el-icon>
                  <div>本月还没有销量</div>
                </div>
              </div>
            </el-card>
          </el-col>
        </el-row>

        <!-- ======================== 口径说明 ======================== -->
        <div class="caliber-note">
          <b>口径</b>：全部指标只统计<b>已过账</b>单据（草稿/已审核未过账不计）。
          「在外钢瓶」= 客户在借瓶数；「应收总额」以<b>应收单</b>为准（阶段 5 起不再用挂账口径）。
          数据截止 <b>{{ 截止日 }}</b>。
        </div>
      </template>
    </DataState>
  </div>
</template>

<style scoped>
/* 小屏时第二列卡片需要上边距（el-col 换行后要紧贴上一张） */
@media (max-width: 1199px) {
  .mt-mobile {
    margin-top: var(--sp-4);
  }
}
</style>

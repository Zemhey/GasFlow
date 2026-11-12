<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api, { type 对账明细行 } from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

const 明细 = ref<对账明细行[]>([])
const 合计 = ref<Record<string, any>>({})
const 加载中 = ref(false)
const 错误 = ref('')
const 日期 = ref(今天())
/** 合计口径来源：'后端合计' 时说明接口带回了合计对象，'前端累加' 时说明是按明细求和 */
const 合计来源 = ref<'后端合计' | '前端累加'>('后端合计')
/** 移动端（≤767px）筛选条收起 */
const 筛选展开 = ref(false)

function 今天() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** 有差异的行才高亮（差异为 0 表示账实相符） */
const 有差异行 = computed(() => 明细.value.filter((r) => Number(r.差异 ?? 0) !== 0))
const 差异合计 = computed(() => 明细.value.reduce((s, r) => s + Number(r.差异 ?? 0), 0))

/** 期末验算不符的行：期初 + 入库 − 出库 ≠ 期末，说明台账内部不自洽 */
const 验算不符行 = computed(() => 明细.value.filter((r) => 验算不符(r)))

/** 后端若带回合计就用后端的（与明细口径一致），否则按当前明细前端累加 */
const 合计对象 = computed(() => {
  const 后端 = 合计.value
  const 有后端 = 后端 && Object.keys(后端).length > 0
  const 求 = (k: string) => 明细.value.reduce((s, r) => s + Number((r as any)[k] ?? 0), 0)
  if (!有后端) {
    return {
      期初: 求('期初'),
      入库: 求('入库'),
      出库: 求('出库'),
      借出: 求('借出'),
      归还: 求('归还'),
      期末: 求('期末'),
      账面: 求('账面'),
      盘点实盘: 求('盘点实盘'),
      差异: 求('差异')
    }
  }
  return {
    期初: 后端?.期初 ?? 求('期初'),
    入库: 后端?.入库 ?? 求('入库'),
    出库: 后端?.出库 ?? 求('出库'),
    借出: 后端?.借出 ?? 求('借出'),
    归还: 后端?.归还 ?? 求('归还'),
    期末: 后端?.期末 ?? 求('期末'),
    账面: 后端?.账面 ?? 求('账面'),
    盘点实盘: 后端?.盘点实盘 ?? 求('盘点实盘'),
    差异: 后端?.差异 ?? 差异合计.value
  }
})

/** 合计行（列顺序：规格/期初/入库/出库/借出/归还/期末/账面/盘点实盘/差异） */
const 合计行 = computed(() => [
  '合计',
  数量(合计对象.value.期初),
  带符号(合计对象.value.入库),
  带符号(-Number(合计对象.value.出库 ?? 0)),
  数量(合计对象.value.借出),
  数量(合计对象.value.归还),
  数量(合计对象.value.期末),
  数量(合计对象.value.账面),
  数量(合计对象.value.盘点实盘),
  带符号(合计对象.value.差异)
])

/** 期初 + 入库 - 出库 = 期末（借出/归还只在借瓶侧，验算时只核对"期末 = 账面"） */
function 期末验算(row: 对账明细行) {
  return Number(row.期初 ?? 0) + Number(row.入库 ?? 0) - Number(row.出库 ?? 0)
}

function 验算不符(row: 对账明细行) {
  return 期末验算(row) !== Number(row.期末 ?? 0)
}

function 差异类(row: 对账明细行): 'ok' | 'gain' | 'loss' {
  const n = Number(row.差异 ?? 0)
  if (n === 0) return 'ok'
  return n > 0 ? 'gain' : 'loss'
}

/**
 * 数量格式化：后端个别列为 NULL 时原写法会渲染成空白（看着像缺数据），
 * 这里统一按 0 处理并本地格式化，负数按业务语义显示为 -N（不做 + 前缀，避免与"差异"混淆）。
 */
function 数量(v: any): string {
  const n = Number(v ?? 0)
  if (!Number.isFinite(n)) return '0'
  return n.toLocaleString('zh-CN')
}

/** 变动量：带正负号 + 千分位 */
function 带符号(v: any): string {
  const n = Number(v ?? 0)
  if (!Number.isFinite(n) || n === 0) return '0'
  const t = Math.abs(n).toLocaleString('zh-CN')
  return n > 0 ? `+${t}` : `-${t}`
}

async function 加载() {
  if (!日期.value) return
  加载中.value = true
  错误.value = ''
  try {
    const r = await api.inventory.对账(日期.value)
    明细.value = r?.明细 ?? []
    合计.value = r?.合计 ?? {}
    合计来源.value = r?.合计 && Object.keys(r.合计).length > 0 ? '后端合计' : '前端累加'
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? `${日期.value} 的对账数据加载失败，请稍后重试`
    明细.value = []
    合计.value = {}
  } finally {
    加载中.value = false
  }
}

/** 回到今天重新对账（空态与错误态的出口） */
function 今天对账() {
  日期.value = 今天()
  加载()
}

/** 导出当前对账结果为 CSV（纯前端，不动接口） */
function 导出() {
  if (!明细.value.length) {
    ElMessage.warning('当前没有可导出的对账数据')
    return
  }
  const 表头 = [
    '规格名称',
    '期初',
    '入库',
    '出库',
    '借出',
    '归还',
    '期末',
    '账面',
    '盘点实盘',
    '差异'
  ]
  const 体 = 明细.value.map((r) =>
    [
      r.规格名称,
      Number(r.期初 ?? 0),
      Number(r.入库 ?? 0),
      Number(r.出库 ?? 0),
      Number(r.借出 ?? 0),
      Number(r.归还 ?? 0),
      Number(r.期末 ?? 0),
      Number(r.账面 ?? 0),
      Number(r.盘点实盘 ?? 0),
      Number(r.差异 ?? 0)
    ]
      .map((v) => `"${String(v ?? '').replace(/"/g, '""')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `库存对账_${日期.value}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${明细.value.length} 行`)
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="库存对账"
      icon="Odometer"
      :sub="`按日期核对「台账推算期末」与「库存结存账面」· 当前对账日 ${日期} · 账面取库存结存，盘点实盘取当日已过账的每日盘点`"
    >
      <template #actions>
        <el-button :disabled="!明细.length" @click="导出">
          <el-icon><Download /></el-icon> 导出
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 重新对账
        </el-button>
      </template>
    </PageHead>

    <!-- KPI：差异与期末是本页最该先看的两类数，先给结论再看明细 -->
    <div class="stat-grid">
      <StatCard
        label="差异规格"
        icon="Warning"
        :tone="有差异行.length ? 'danger' : 'success'"
        :value="有差异行.length.toLocaleString('zh-CN')"
        unit="项"
        :sub="
          有差异行.length
            ? `共 ${明细.length} 个规格有账 · 差异绝对值合计 <b>${数量(Math.abs(差异合计))}</b> 只`
            : `全部 ${明细.length} 个规格账实相符`
        "
      />
      <StatCard
        label="差异合计"
        icon="Switch"
        :tone="差异合计 > 0 ? 'warning' : 差异合计 < 0 ? 'danger' : 'success'"
        :value="带符号(差异合计)"
        unit="只"
        :sub="`按当前 ${明细.length} 行明细求和（${合计来源 === '后端合计' ? '与后端合计一致' : '后端未返回合计，前端累加'}）`"
      />
      <StatCard
        label="期末（台账推算）"
        icon="Finished"
        tone="info"
        :value="数量(合计对象.期末)"
        unit="只"
        :sub="`期初 <b>${数量(合计对象.期初)}</b> + 入库 <b>${数量(合计对象.入库)}</b> − 出库 <b>${数量(合计对象.出库)}</b>`"
      />
      <StatCard
        label="账面（库存结存）"
        icon="Box"
        tone="info"
        :value="数量(合计对象.账面)"
        unit="只"
        sub="来自五维结存汇总；与期末不符说明台账与结存不自洽，需排查过账/反过账"
      />
      <StatCard
        label="期末验算不符"
        icon="CircleClose"
        :tone="验算不符行.length ? 'warning' : 'success'"
        :value="验算不符行.length.toLocaleString('zh-CN')"
        unit="项"
        sub="逐行验算 <b>期初 + 入库 − 出库 = 期末</b>，不符说明该行台账内部对不上"
      />
    </div>

    <el-card shadow="never">
      <button
        class="mobile-filter-toggle el-button el-button--default"
        @click="筛选展开 = !筛选展开"
      >
        <el-icon><Filter /></el-icon> 对账日期
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <span class="filter-bar__label">对账日期</span>
        <el-date-picker
          v-model="日期"
          type="date"
          value-format="YYYY-MM-DD"
          style="width: 150px"
          @change="加载"
        />
        <el-button type="primary" @click="加载">
          <el-icon><Refresh /></el-icon> 重新对账
        </el-button>

        <span class="spacer" />
        <el-tag v-if="有差异行.length" type="danger" size="small" effect="light">
          <el-icon><Warning /></el-icon> 差异规格 {{ 有差异行.length }} 项 · 合计 {{ 带符号(差异合计) }} 只
        </el-tag>
        <el-tag v-else-if="明细.length" type="success" size="small" effect="light">
          <el-icon><CircleCheck /></el-icon> 账实相符
        </el-tag>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!明细.length"
        empty-title="该日没有可对账的规格"
        empty-desc="对账明细来自「每日盘点」已过账的规格行：当日没有盘点、盘点单尚未过账，或规格还没有库存结存/台账流水时，这里都会是空的。可换一个已做过盘点的日期，或先去「每日盘点」录入并过账一张盘点单。"
        empty-icon="Odometer"
        :skeleton-rows="10"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button @click="今天对账">
            <el-icon><Calendar /></el-icon> 回到今天重新对账
          </el-button>
        </template>

        <!-- 颜色图例：差异行高亮 + 差异标签色的含义 -->
        <div class="row-wrap mb-2">
          <div class="legend">
            <span class="legend__item">
              <i class="legend__swatch sw-gain" />盘盈：差异 &gt; 0（实盘多于账面）
            </span>
            <span class="legend__item">
              <i class="legend__swatch sw-loss" />盘亏：差异 &lt; 0（实盘少于账面）
            </span>
            <span class="legend__item">
              <i class="legend__swatch sw-ok" />相符：差异 = 0
            </span>
            <span class="legend__item">
              <i class="legend__swatch sw-row" />淡蓝底 = 该行有差异
            </span>
          </div>
        </div>

        <div class="table-wrap table-scroll-hint">
          <el-table
            :data="明细"
            size="small"
            stripe
            show-summary
            :row-class-name="({ row }) => (差异类(row) === 'ok' ? '' : 'row-diff')"
            :summary-method="() => 合计行"
            max-height="calc(100vh - 320px)"
          >
            <el-table-column prop="规格名称" label="规格名称" min-width="150" show-overflow-tooltip fixed />
            <el-table-column label="期初" width="90" align="right">
              <template #default="{ row }"><span class="num">{{ 数量(row.期初) }}</span></template>
            </el-table-column>
            <el-table-column label="入库" width="90" align="right">
              <template #default="{ row }">
                <span class="num up">{{ 带符号(row.入库) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="出库" width="90" align="right">
              <template #default="{ row }">
                <span class="num down">{{ 带符号(-Number(row.出库 ?? 0)) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="借出" width="90" align="right">
              <template #default="{ row }"><span class="num">{{ 数量(row.借出) }}</span></template>
            </el-table-column>
            <el-table-column label="归还" width="90" align="right">
              <template #default="{ row }"><span class="num">{{ 数量(row.归还) }}</span></template>
            </el-table-column>
            <el-table-column label="期末" width="110" align="right">
              <template #default="{ row }">
                <el-tooltip
                  :disabled="!验算不符(row)"
                  :content="`期初 + 入库 − 出库 = ${数量(期末验算(row))}，与期末 ${数量(row.期末)} 不一致`"
                  placement="top"
                >
                  <span class="num text-strong" :class="{ 'text-warning': 验算不符(row) }">
                    {{ 数量(row.期末) }}{{ 验算不符(row) ? ' ⚠' : '' }}
                  </span>
                </el-tooltip>
              </template>
            </el-table-column>
            <el-table-column label="账面" width="100" align="right">
              <template #default="{ row }"><span class="num">{{ 数量(row.账面) }}</span></template>
            </el-table-column>
            <el-table-column label="盘点实盘" width="110" align="right">
              <template #default="{ row }"><span class="num">{{ 数量(row.盘点实盘) }}</span></template>
            </el-table-column>
            <el-table-column label="差异" width="110" align="right">
              <template #default="{ row }">
                <el-tag
                  :type="
                    差异类(row) === 'ok'
                      ? 'success'
                      : 差异类(row) === 'gain'
                        ? 'warning'
                        : 'danger'
                  "
                  size="small"
                  effect="light"
                >
                  {{ 带符号(row.差异) }}
                </el-tag>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>合计（{{ 明细.length }} 个规格 · {{ 合计来源 }}）</span>
          <span>期初 <b>{{ 数量(合计对象.期初) }}</b></span>
          <span>入库 <b>{{ 数量(合计对象.入库) }}</b></span>
          <span>出库 <b>{{ 数量(合计对象.出库) }}</b></span>
          <span>借出 <b>{{ 数量(合计对象.借出) }}</b></span>
          <span>归还 <b>{{ 数量(合计对象.归还) }}</b></span>
          <span>期末 <b>{{ 数量(合计对象.期末) }}</b></span>
          <span>账面 <b>{{ 数量(合计对象.账面) }}</b></span>
          <span>盘点实盘 <b>{{ 数量(合计对象.盘点实盘) }}</b></span>
          <span :class="差异合计 === 0 ? '' : 'sum-bar__danger'">
            差异 <b>{{ 带符号(合计对象.差异) }}</b>
          </span>
        </div>
      </DataState>

      <div class="caliber-note">
        <b>口径</b>：查询参数只有<b>对账日期</b>（GET /inventory/reconcile?日期=YYYY-MM-DD），页面不做任何前端过滤，
        明细整表来自后端。<b>期初 / 入库 / 出库 / 借出 / 归还 / 期末</b> 由 <b>钢瓶库存流水</b>（台账，只增不改）
        按规格汇总推算，验算关系为 <b>期初 + 入库 − 出库 = 期末</b>（借出/归还属借瓶侧，不参与该式）；
        <b>账面</b> 取 <b>库存结存</b>（五维结存按规格汇总），<b>盘点实盘</b> 取当日已过账「每日盘点」的实盘数量，
        <b>差异 = 盘点实盘 − 账面</b>（正数盘盈、负数盘亏）。<b>合计行与顶部指标</b>优先使用接口返回的
        <b>合计</b> 对象（当前来源：<b>{{ 合计来源 }}</b>），接口未返回时才按上表明细前端累加，
        两者口径一致；<b>差异合计</b> 恒按明细逐行求和，便于与"差异规格项数"对照。
        导出按钮导出的是<b>当前对账日的全部明细行（含表头）</b>，不含合计行。
      </div>
    </el-card>
  </div>
</template>

<style scoped>
/* 有差异行整行淡蓝底：先扫出"哪几个规格对不上"，再逐行看数字 */
:deep(.row-diff) {
  background: var(--c-brand-50) !important;
}

:deep(.row-diff:hover > td.el-table__cell) {
  background: var(--c-brand-100) !important;
}

/* 图例色块：只能靠 scoped 类名着色，色值仍来自令牌 */
.sw-gain { background: var(--c-warning-500); }
.sw-loss { background: var(--c-danger-500); }
.sw-ok { background: var(--c-success-500); }
.sw-row { background: var(--c-brand-100); }
</style>

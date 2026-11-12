<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api, { type 盘点差异行 } from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'
import MoneyText from '@/components/MoneyText.vue'

const 行 = ref<盘点差异行[]>([])
const 加载中 = ref(false)
const 错误 = ref('')
/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)
/** 只看有差异的行：盘点复核最常用的视图 */
const 只看差异 = ref(false)

function 本月() {
  const d = new Date()
  const f = new Date(d.getFullYear(), d.getMonth(), 1)
  const fmt = (x: Date) =>
    `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`
  return [fmt(f), fmt(d)]
}

const 查询 = reactive({ 日期: 本月() })

/** 差异行：差异数量不为 0（盘盈或盘亏） */
const 有差异 = computed(() => 行.value.filter((r) => 数值(r.差异) !== 0))
/** 盘盈 / 盘亏分开统计：一个数看不出方向，分开才能判断是收货漏记还是发货漏记 */
const 盘盈行 = computed(() => 行.value.filter((r) => 数值(r.差异) > 0))
const 盘亏行 = computed(() => 行.value.filter((r) => 数值(r.差异) < 0))

const 过滤后 = computed(() => (只看差异.value ? 有差异.value : 行.value))

const 差异数量合计 = computed(() => 行.value.reduce((s, r) => s + 数值(r.差异), 0))
const 差异金额合计 = computed(() => 行.value.reduce((s, r) => s + 数值(r.差异金额), 0))
/** 账面 / 实盘合计：把"账实差多少"用绝对值摆出来 */
const 账面合计 = computed(() => 行.value.reduce((s, r) => s + 数值(r.账面), 0))
const 实盘合计 = computed(() => 行.value.reduce((s, r) => s + 数值(r.实盘), 0))
/** 差异率 = |差异数量合计| ÷ 账面合计（账面为 0 时给 0，避免除零） */
const 差异率 = computed(() => {
  const 账 = 账面合计.value
  if (!账) return 0
  return Number(((Math.abs(差异数量合计.value) / 账) * 100).toFixed(2))
})

/** 取数值并兜底非数字：接口给的数字理论上都是 number，但空值/字符串也不能把求和污染成 NaN */
function 数值(v: any): number {
  const n = Number(v ?? 0)
  return Number.isFinite(n) ? n : 0
}

function 金额(v: any): string {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function 带符号(v: any): string {
  const n = 数值(v)
  if (n === 0) return '0'
  return n > 0 ? `+${n}` : String(n)
}

/** 差异标签色：盘盈 warning（多出来的也要查）/ 盘亏 danger（少了的风险最高）/ 相符 success */
function 差异标签(row: 盘点差异行): 'success' | 'warning' | 'danger' {
  const n = 数值(row.差异)
  if (n === 0) return 'success'
  return n > 0 ? 'warning' : 'danger'
}

function 日期(v: any): string {
  const s = String(v ?? '')
  return s ? s.slice(0, 10) : '—'
}

/** 当前查询区间，用于空态与口径文案 */
const 区间 = computed(() => {
  const f = String(查询.日期?.[0] ?? '')
  const t = String(查询.日期?.[1] ?? '')
  return f && t ? `${f} ~ ${t}` : '全部'
})

const 空标题 = computed(() =>
  只看差异.value ? '所选区间内账实相符，没有差异行' : '所选区间内没有盘点记录'
)
const 空说明 = computed(() =>
  只看差异.value
    ? '差异 = 实盘 − 账面；全部为 0 说明这期间的盘点与结存一致。取消「只看差异」可查看全部盘点行。'
    : '盘点差异来自盘点单过账时写入的差异行；这期间没有盘点单过账，或日期范围不对。请确认盘点单已过账，或放宽日期范围。'
)

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const params: any = {}
    if (查询.日期?.length === 2) {
      params.from = 查询.日期[0]
      params.to = 查询.日期[1]
    }
    const r = await api.reports.盘点差异(params)
    行.value = r?.rows ?? []
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '盘点差异加载失败，请稍后重试'
    行.value = []
  } finally {
    加载中.value = false
  }
}

function 重置() {
  查询.日期 = 本月()
  只看差异.value = false
  加载()
}

/** 打印：必须走 script 里的函数，模板里写 window.print() 会被编译成 _ctx.window（undefined） */
function 打印() {
  window.print()
}

/** 导出当前视图为 CSV（纯前端，不改后端契约） */
function 导出() {
  if (!过滤后.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = ['盘点日期', '盘点区域', '规格', '账面', '实盘', '差异', '差异金额', '差异原因']
  const 体 = 过滤后.value.map((r) =>
    [
      日期(r.盘点日期),
      r.盘点区域,
      r.规格,
      数值(r.账面),
      数值(r.实盘),
      数值(r.差异),
      Number(r.差异金额 ?? 0).toFixed(2),
      r.差异原因
    ]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `盘点差异_${区间.value.replace(/[ ~]/g, '') || '全部'}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${过滤后.value.length} 行`)
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="盘点差异"
      icon="Odometer"
      sub="差异 = 实盘 − 账面 · 明细取已过账盘点单写入的差异行，差异必须走调整单处理"
    >
      <template #actions>
        <el-button :disabled="!过滤后.length" @click="导出">
          <el-icon><Download /></el-icon> 导出
        </el-button>
        <el-button :disabled="!行.length" @click="打印">
          <el-icon><Printer /></el-icon> 打印
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 查询
        </el-button>
      </template>
    </PageHead>

    <!-- KPI：差异金额合计非 0 时整卡转 danger，让盘亏风险一眼可见 -->
    <div class="stat-grid">
      <StatCard
        label="差异数量合计"
        icon="Odometer"
        :tone="差异数量合计 !== 0 ? 'danger' : 'success'"
        :value="带符号(差异数量合计)"
        unit="只"
        :sub="`盘盈 <b>${盘盈行.length}</b> 行 · 盘亏 <b>${盘亏行.length}</b> 行`"
      />
      <StatCard
        label="差异金额合计"
        icon="Money"
        :tone="差异金额合计 !== 0 ? 'danger' : 'success'"
        :value="`¥${金额(差异金额合计)}`"
        sub="差异金额 = 差异数量 × 成本单价（正数 = 盘盈金额，负数 = 盘亏金额）"
      />
      <StatCard
        label="账实相符情况"
        icon="Files"
        :tone="有差异.length ? 'warning' : 'success'"
        :value="`${行.length - 有差异.length} / ${行.length}`"
        :sub="`有差异 <b>${有差异.length}</b> 行 · 差异率 <b>${差异率}%</b>`"
      />
      <StatCard
        label="账面 / 实盘"
        icon="Box"
        tone="info"
        :value="账面合计.toLocaleString('zh-CN')"
        unit="只"
        :sub="`实盘合计 <b>${实盘合计.toLocaleString('zh-CN')}</b> 只 · 期间 ${区间}`"
      />
    </div>

    <el-card shadow="never">
      <button
        class="mobile-filter-toggle el-button el-button--default no-print"
        @click="筛选展开 = !筛选展开"
      >
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="只看差异" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible no-print" :class="{ 'is-open': 筛选展开 }">
        <el-date-picker
          v-model="查询.日期"
          type="daterange"
          value-format="YYYY-MM-DD"
          start-placeholder="开始日期"
          end-placeholder="结束日期"
          style="width: 250px"
        />
        <el-checkbox v-model="只看差异" border>
          只看差异{{ 有差异.length ? `（${有差异.length}）` : '' }}
        </el-checkbox>
        <el-button type="primary" @click="加载">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <el-tag v-if="有差异.length" type="danger" effect="light" size="small">
          <el-icon><Warning /></el-icon> 有差异 {{ 有差异.length }} 行
        </el-tag>
        <el-tag v-else type="success" effect="light" size="small">
          <el-icon><CircleCheck /></el-icon> 账实相符
        </el-tag>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!过滤后.length"
        :empty-title="空标题"
        :empty-desc="空说明"
        empty-icon="Odometer"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="只看差异" @click="只看差异 = false">
            <el-icon><RefreshLeft /></el-icon> 显示全部盘点行
          </el-button>
          <el-button v-else @click="重置">
            <el-icon><Refresh /></el-icon> 重置为本月
          </el-button>
        </template>

        <div class="legend mb-2">
          <span class="legend__item"><i class="legend__swatch swatch-gain" />差异 &gt; 0 盘盈（实盘多于账面）</span>
          <span class="legend__item"><i class="legend__swatch swatch-loss" />差异 &lt; 0 盘亏（实盘少于账面）</span>
          <span class="legend__item"><i class="legend__swatch swatch-ok" />差异 = 0 账实相符</span>
        </div>

        <!-- 打印区：盘点差异表交给仓管与财务复核签字 -->
        <div class="print-area">
          <div class="doc-head">
            <div class="doc-title">盘点差异表 · {{ 区间 }}</div>
          </div>

          <div class="table-wrap table-scroll-hint print-table">
            <el-table
              :data="过滤后"
              size="small"
              stripe
              :row-class-name="({ row }) => (数值(row.差异) === 0 ? '' : 'row-diff')"
              max-height="calc(100vh - 320px)"
            >
              <el-table-column label="盘点日期" width="120">
                <template #default="{ row }">
                  <span class="num">{{ 日期(row.盘点日期) }}</span>
                </template>
              </el-table-column>
              <el-table-column prop="盘点区域" label="盘点区域" width="140" show-overflow-tooltip>
                <template #default="{ row }">{{ row.盘点区域 || '—' }}</template>
              </el-table-column>
              <el-table-column prop="规格" label="规格" min-width="160" show-overflow-tooltip>
                <template #default="{ row }">{{ row.规格 || '—' }}</template>
              </el-table-column>
              <el-table-column label="账面" width="110" align="right">
                <template #default="{ row }"><MoneyText :value="数值(row.账面)" :digits="0" /></template>
              </el-table-column>
              <el-table-column label="实盘" width="110" align="right">
                <template #default="{ row }"><MoneyText :value="数值(row.实盘)" :digits="0" /></template>
              </el-table-column>
              <el-table-column label="差异" width="120" align="right">
                <template #default="{ row }">
                  <el-tag :type="差异标签(row)" size="small" effect="light">
                    {{ 带符号(row.差异) }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="差异金额" width="150" align="right">
                <template #default="{ row }">
                  <span :class="数值(row.差异金额) !== 0 ? 'down' : 'flat'">
                    <MoneyText :value="row.差异金额" prefix />
                  </span>
                </template>
              </el-table-column>
              <el-table-column prop="差异原因" label="差异原因" min-width="220" show-overflow-tooltip>
                <template #default="{ row }">{{ row.差异原因 || '—' }}</template>
              </el-table-column>
            </el-table>
          </div>

          <div class="sum-bar">
            <span>合计（{{ 过滤后.length }} 行 / 共 {{ 行.length }} 行）</span>
            <span>账面 <b>{{ 账面合计.toLocaleString('zh-CN') }}</b> 只</span>
            <span>实盘 <b>{{ 实盘合计.toLocaleString('zh-CN') }}</b> 只</span>
            <span class="sum-bar__danger">差异 <b>{{ 带符号(差异数量合计) }}</b> 只</span>
            <span class="sum-bar__danger">差异金额 <b>¥{{ 金额(差异金额合计) }}</b></span>
          </div>

          <p class="sign">盘点人：__________　　复核人：__________　　仓管：__________</p>
        </div>

        <div class="caliber-note">
          <b>口径</b>：行取盘点差异接口返回的明细（盘点单过账时写入的差异行），日期区间按<b>盘点日期</b>过滤。
          <b>差异 = 实盘 − 账面</b>，正数为盘盈、负数为盘亏；
          <b>差异金额 = 差异 × 成本单价</b>（由数据库持久化列计算，前端不重算）。
          顶部指标卡与合计条按<b>全部已加载行</b>汇总（不受「只看差异」影响，勾选它只影响中间的表格）；
          「差异率」= |差异数量合计| ÷ 账面合计。差异必须走<b>调整单过账</b>修正结存，不允许直接改库存。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 差异行淡琥珀底：扫一眼就能找到要复核的那几行 */
:deep(.row-diff) {
  background: var(--c-warning-50) !important;
}

/* 单据抬头：屏幕上左对齐，打印时居中加粗 */
.doc-head {
  padding: var(--sp-2) 0 var(--sp-3);
}

.doc-title {
  font-size: var(--fs-lg);
  font-weight: var(--fw-semibold);
  color: var(--c-ink-900);
}

.sign {
  margin: var(--sp-6) 0 0;
  font-size: var(--fs-sm);
  color: var(--c-ink-500);
}

/* 图例色块：盘盈（琥珀）/ 盘亏（红）/ 相符（绿） */
.legend__swatch.swatch-gain {
  background: var(--c-warning-500);
}

.legend__swatch.swatch-loss {
  background: var(--c-danger-500);
}

.legend__swatch.swatch-ok {
  background: var(--c-success-500);
}

@media print {
  .doc-head {
    text-align: center;
  }

  .doc-title {
    font-size: var(--fs-xl);
    font-weight: var(--fw-bold);
  }

  .print-table {
    border: none;
    overflow: visible;
  }

  .sign {
    margin-top: var(--sp-8);
  }

  /* el-table 的 max-height 会把内容裁在滚动容器里，打印前必须放开，
     否则纸面上只有屏幕上可见的那几行 */
  .print-table :deep(.el-scrollbar),
  .print-table :deep(.el-scrollbar__wrap),
  .print-table :deep(.el-table__body-wrapper) {
    max-height: none !important;
    overflow: visible !important;
  }

  .print-table :deep(.el-table) {
    height: auto !important;
  }
}
</style>

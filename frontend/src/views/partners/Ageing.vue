<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import api, { type 账龄行 } from '@/api'
import { useLookupStore } from '@/stores/lookup'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'
import MoneyText from '@/components/MoneyText.vue'

const route = useRoute()
const lookup = useLookupStore()

const 客户编号 = ref<number | null>(null)
const 明细 = ref<账龄行[]>([])
const 合计 = ref<Record<string, any>>({})
const 加载中 = ref(false)
const 错误 = ref('')
/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)

/**
 * 账龄分档：与后端返回的「账龄天数」字段对齐（前端不重算天数，只做分桶展示）。
 * 0-30 档下界取 -9999，是为了把"送气日期晚于今天"等异常值也兜进第一档，不丢行。
 */
const 分档 = [
  { label: '0-30 天', min: -9999, max: 30, tone: 'success' as const, icon: 'CircleCheck' },
  { label: '31-60 天', min: 31, max: 60, tone: 'warning' as const, icon: 'Timer' },
  { label: '61-90 天', min: 61, max: 90, tone: 'warning' as const, icon: 'Timer' },
  { label: '90 天以上', min: 91, max: 99999, tone: 'danger' as const, icon: 'Warning' }
]

function 在档(row: 账龄行, min: number, max: number) {
  const n = Number(row.账龄天数 ?? 0)
  return n >= min && n <= max
}

/** 分档汇总：只对"已取回的明细"分桶求和，接口的合计对象不参与分桶 */
const 汇总 = computed(() =>
  分档.map((d) => {
    const 行 = 明细.value.filter((r) => 在档(r, d.min, d.max))
    return {
      ...d,
      笔数: 行.length,
      未收: 行.reduce((s, r) => s + Number(r.未收金额 ?? 0), 0)
    }
  })
)

/** 合计优先取接口返回的合计对象，接口缺失时才回退为对明细求和 */
const 未收合计 = computed(() =>
  合计.value?.未收金额 ?? 明细.value.reduce((s, r) => s + Number(r.未收金额 ?? 0), 0)
)
const 金额合计 = computed(() =>
  合计.value?.金额合计 ?? 明细.value.reduce((s, r) => s + Number(r.金额合计 ?? 0), 0)
)
const 已收合计 = computed(() =>
  合计.value?.已收金额 ?? 明细.value.reduce((s, r) => s + Number(r.已收金额 ?? 0), 0)
)
const 逾期90天 = computed(() =>
  明细.value.filter((r) => Number(r.账龄天数 ?? 0) > 90).reduce((s, r) => s + Number(r.未收金额 ?? 0), 0)
)

function 金额(v: any): string {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/** 档位占未收总额的比例（分母为 0 时给 0，避免 NaN） */
function 占未收比(值: number): number {
  const 总 = Number(未收合计.value ?? 0)
  if (!总) return 0
  return Math.round((值 / 总) * 100)
}

function 日期(v: any): string {
  const s = String(v ?? '')
  return s ? s.slice(0, 10) : '—'
}

function 账龄标签(row: 账龄行): 'primary' | 'success' | 'info' | 'warning' | 'danger' {
  const n = Number(row.账龄天数 ?? 0)
  if (n > 90) return 'danger'
  if (n > 60) return 'warning'
  if (n > 30) return 'primary'
  return 'success'
}

function 风险名(row: 账龄行): string {
  const n = Number(row.账龄天数 ?? 0)
  if (n > 90) return '高风险'
  if (n > 30) return '关注'
  return '正常'
}

function 风险类(row: 账龄行): string {
  const n = Number(row.账龄天数 ?? 0)
  if (n > 90) return 'down'
  if (n > 30) return 'text-warning'
  return 'flat'
}

/** 未收金额的色调跟着档位走：>90 红、>30 琥珀、其余走默认 */
function 未收类(row: 账龄行): string {
  const n = Number(row.账龄天数 ?? 0)
  if (n > 90) return 'down'
  if (n > 30) return 'text-warning'
  return 'num'
}

const 空标题 = computed(() => (客户编号.value ? '该客户没有未收单据' : '尚未选择客户'))
const 空说明 = computed(() =>
  客户编号.value
    ? '账龄明细只列出未收金额大于 0 的单据；该客户已结清、或从未送过气时，这里就是空的。可换一个客户查看。'
    : '账龄按单个客户计算：请先在上方「客户」下拉里选一个客户，再点「查询」。'
)

async function 加载() {
  if (!客户编号.value) return
  加载中.value = true
  错误.value = ''
  try {
    const r = await api.partners.账龄(客户编号.value)
    明细.value = r?.明细 ?? []
    合计.value = r?.合计 ?? {}
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '账龄数据加载失败，请稍后重试'
    明细.value = []
    合计.value = {}
  } finally {
    加载中.value = false
  }
}

/** 导出当前客户的账龄明细（纯前端 CSV，不改后端契约） */
function 导出() {
  if (!明细.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = ['单据号码', '送气日期', '金额合计', '已收金额', '未收金额', '账龄天数']
  const 体 = 明细.value.map((r) =>
    [
      r.单据号码,
      日期(r.送气日期),
      Number(r.金额合计 ?? 0).toFixed(2),
      Number(r.已收金额 ?? 0).toFixed(2),
      Number(r.未收金额 ?? 0).toFixed(2),
      Number(r.账龄天数 ?? 0)
    ]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `账龄分析_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${明细.value.length} 行`)
}

onMounted(async () => {
  await lookup.加载()
  const q = route.query.客户编号
  if (q) {
    客户编号.value = Number(q)
    await 加载()
  } else {
    const first = lookup.列表('客户')[0]
    if (first) {
      客户编号.value = first['客户编号']
      await 加载()
    }
  }
})
</script>

<template>
  <div>
    <PageHead
      title="账龄分析"
      icon="Clock"
      sub="按未收余额分档：0-30 / 31-60 / 61-90 / 90 天以上 · 数据来自往来账龄接口"
    >
      <template #actions>
        <el-button :disabled="!明细.length" @click="导出">
          <el-icon><Download /></el-icon> 导出
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <!-- 分档 KPI：一眼看清欠款集中在哪个账龄段 -->
    <div class="stat-grid">
      <StatCard
        v-for="d in 汇总"
        :key="d.label"
        :label="d.label"
        :icon="d.icon"
        :tone="d.tone"
        :value="`¥${金额(d.未收)}`"
        :sub="`<b>${d.笔数}</b> 笔 · 占未收 <b>${占未收比(d.未收)}%</b>`"
      />
    </div>

    <el-card shadow="never">
      <button
        class="mobile-filter-toggle el-button el-button--default"
        @click="筛选展开 = !筛选展开"
      >
        <el-icon><Filter /></el-icon> 筛选
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <span class="filter-bar__label">客户</span>
        <el-select
          v-model="客户编号"
          filterable
          clearable
          placeholder="选择客户"
          style="width: 240px"
          @change="加载"
        >
          <el-option v-for="o in lookup.选项('客户')" :key="o.value" :label="o.label" :value="o.value" />
        </el-select>
        <el-button type="primary" @click="加载">
          <el-icon><Search /></el-icon> 查询
        </el-button>

        <span class="spacer" />
        <el-tag v-if="逾期90天 > 0" type="danger" effect="light" size="small">
          <el-icon><Warning /></el-icon> 90 天以上未收 ¥{{ 金额(逾期90天) }}
        </el-tag>
        <el-tag v-else type="success" effect="light" size="small">
          <el-icon><CircleCheck /></el-icon> 无 90 天以上欠款
        </el-tag>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!明细.length"
        :empty-title="空标题"
        :empty-desc="空说明"
        empty-icon="Clock"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="客户编号" @click="加载">
            <el-icon><Refresh /></el-icon> 刷新
          </el-button>
        </template>

        <div class="legend mb-2">
          <span class="legend__item"><i class="legend__swatch swatch-ok" />≤ 30 天 正常</span>
          <span class="legend__item"><i class="legend__swatch swatch-warn" />31-90 天 关注</span>
          <span class="legend__item"><i class="legend__swatch swatch-bad" />> 90 天 高风险</span>
        </div>

        <div class="table-wrap table-scroll-hint">
          <el-table :data="明细" size="small" stripe max-height="calc(100vh - 320px)">
            <el-table-column prop="单据号码" label="单据号码" min-width="170" show-overflow-tooltip />
            <el-table-column label="送气日期" width="120">
              <template #default="{ row }">
                <span class="num">{{ 日期(row.送气日期) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="金额合计" width="140" align="right">
              <template #default="{ row }"><MoneyText :value="row.金额合计" prefix /></template>
            </el-table-column>
            <el-table-column label="已收金额" width="140" align="right">
              <template #default="{ row }"><MoneyText :value="row.已收金额" prefix /></template>
            </el-table-column>
            <el-table-column label="未收金额" width="140" align="right">
              <template #default="{ row }">
                <span :class="未收类(row)"><MoneyText :value="row.未收金额" prefix /></span>
              </template>
            </el-table-column>
            <el-table-column label="账龄天数" width="120" align="center">
              <template #default="{ row }">
                <el-tag :type="账龄标签(row)" size="small" effect="light">
                  {{ row.账龄天数 ?? 0 }} 天
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="风险" width="110" align="center">
              <template #default="{ row }">
                <span :class="风险类(row)">{{ 风险名(row) }}</span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>合计（{{ 明细.length }} 笔）</span>
          <span>应收 <b>¥{{ 金额(金额合计) }}</b></span>
          <span>已收 <b>¥{{ 金额(已收合计) }}</b></span>
          <span class="sum-bar__danger">未收 <b>¥{{ 金额(未收合计) }}</b></span>
          <span>90 天以上 <b>¥{{ 金额(逾期90天) }}</b></span>
        </div>

        <div class="caliber-note">
          <b>口径</b>：明细取往来账龄接口返回的<b>未结清单据</b>（未收金额大于 0 才列出）；
          账龄天数直接取接口返回值，前端<b>不重算</b>。四档 0-30 / 31-60 / 61-90 / 90 天以上由前端按
          账龄天数对<b>已取回的明细</b>分桶，各档金额 = Σ该档未收金额；顶部「合计」栏与合计条优先取接口返回的
          合计对象，接口未返回时才回退为对明细求和。<b>未收金额 = 该单金额合计 − 已收金额</b>。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 图例色块：与分档语义色一致（绿=正常 / 琥珀=关注 / 红=高风险） */
.legend__swatch.swatch-ok {
  background: var(--c-success-500);
}

.legend__swatch.swatch-warn {
  background: var(--c-warning-500);
}

.legend__swatch.swatch-bad {
  background: var(--c-danger-500);
}
</style>

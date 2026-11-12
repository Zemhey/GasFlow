<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api from '@/api'
import { useLookupStore } from '@/stores/lookup'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'
import MoneyText from '@/components/MoneyText.vue'

const lookup = useLookupStore()
const 行 = ref<any[]>([])
const 日期回显 = ref('')
const 加载中 = ref(false)
const 错误 = ref('')
/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)

function 今天() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const 查询 = reactive<any>({ 日期: 今天(), 员工编号: null })

/** 打印/导出的标题日期：优先用接口回显的日期，其次用查询日期 */
const 单据日期 = computed(() => 日期回显.value || String(查询.日期 ?? '').slice(0, 10))

/** 送瓶员选项的名称：用于标题与空态文案，让"给谁看的清单"一目了然 */
const 送瓶员名 = computed(() =>
  查询.员工编号 ? lookup.名称('员工', 查询.员工编号) : ''
)

const 合计 = computed(() => ({
  行数: 行.value.length,
  送气量: 行.value.reduce((s, r) => s + Number(r.送气量 ?? 0), 0),
  收瓶量: 行.value.reduce((s, r) => s + Number(r.收瓶量 ?? 0), 0),
  金额: 行.value.reduce((s, r) => s + Number(r.金额 ?? 0), 0)
}))

/** 涉及客户数与单据数：司机出发前要跑几趟、几张单，这里给个总数 */
const 客户数 = computed(() => new Set(行.value.map((r) => String(r.客户 ?? '')).filter(Boolean)).size)
const 单据数 = computed(() => new Set(行.value.map((r) => String(r.单据号码 ?? '')).filter(Boolean)).size)

function 金额(v: any): string {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

const 空标题 = computed(() =>
  送瓶员名.value ? `${送瓶员名.value} 当日没有配送任务` : '该日期没有配送任务'
)
const 空说明 = computed(() =>
  '配送清单只列出所选日期内已过账送气单的明细。换个日期、或把送瓶员改为「全部送瓶员」再查；确认当天确实有送气单过账。'
)

async function 查询数据() {
  加载中.value = true
  错误.value = ''
  try {
    const r = await api.reports.配送清单({ 日期: 查询.日期, 员工编号: 查询.员工编号 ?? undefined })
    行.value = r?.rows ?? []
    日期回显.value = String(r?.日期 ?? 查询.日期 ?? '').slice(0, 10)
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '配送清单加载失败，请稍后重试'
    行.value = []
  } finally {
    加载中.value = false
  }
}

function 重置() {
  查询.日期 = 今天()
  查询.员工编号 = null
  查询数据()
}

/** 打印：必须走 script 里的函数，模板里写 window.print() 会被编译成 _ctx.window（undefined） */
function 打印() {
  window.print()
}

/** 导出当前清单为 CSV（纯前端，不影响后端契约） */
function 导出() {
  if (!行.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = ['送瓶员', '单据号码', '客户', '地址', '电话', '规格', '送气量', '收空瓶', '金额']
  const 体 = 行.value.map((r) =>
    [
      r.送瓶员,
      r.单据号码,
      r.客户,
      r.地址,
      r.电话,
      r.规格,
      Number(r.送气量 ?? 0),
      Number(r.收瓶量 ?? 0),
      Number(r.金额 ?? 0).toFixed(2)
    ]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `配送清单_${单据日期.value || 今天()}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${行.value.length} 行`)
}

onMounted(async () => {
  await lookup.加载()
  await 查询数据()
})
</script>

<template>
  <div>
    <PageHead
      title="配送清单"
      icon="Van"
      sub="按日期 / 送瓶员列出当日要送的货 · 明细来自已过账送气单，可直接打印给司机"
    >
      <template #actions>
        <el-button :disabled="!行.length" @click="导出">
          <el-icon><Download /></el-icon> 导出 CSV
        </el-button>
        <el-button :disabled="!行.length" @click="打印">
          <el-icon><Printer /></el-icon> 打印配送单
        </el-button>
        <el-button type="primary" :loading="加载中" @click="查询数据">
          <el-icon><Refresh /></el-icon> 查询
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="配送行数"
        icon="List"
        :value="合计.行数.toLocaleString('zh-CN')"
        unit="行"
        :sub="`<b>${单据数}</b> 张送气单 · <b>${客户数}</b> 个客户`"
      />
      <StatCard
        label="送气量合计"
        icon="Box"
        :value="合计.送气量.toLocaleString('zh-CN')"
        unit="瓶"
        sub="当日送出的实瓶数量"
      />
      <StatCard
        label="收空瓶合计"
        icon="Coin"
        tone="info"
        :value="合计.收瓶量.toLocaleString('zh-CN')"
        unit="只"
        sub="当日应回收的空瓶数量"
      />
      <StatCard
        label="随车金额"
        icon="Money"
        tone="warning"
        :value="`¥${金额(合计.金额)}`"
        :sub="`清单日期 <b>${单据日期 || '—'}</b>${送瓶员名 ? ` · ${送瓶员名}` : ''}`"
      />
    </div>

    <el-card shadow="never">
      <button
        class="mobile-filter-toggle el-button el-button--default no-print"
        @click="筛选展开 = !筛选展开"
      >
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="查询.员工编号" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible no-print" :class="{ 'is-open': 筛选展开 }">
        <el-date-picker
          v-model="查询.日期"
          type="date"
          value-format="YYYY-MM-DD"
          placeholder="配送日期"
          style="width: 160px"
        />
        <el-select
          v-model="查询.员工编号"
          clearable
          filterable
          placeholder="全部送瓶员"
          style="width: 180px"
        >
          <el-option v-for="o in lookup.选项('员工')" :key="o.value" :label="o.label" :value="o.value" />
        </el-select>
        <el-button type="primary" @click="查询数据">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <span class="hint">按「送瓶员 → 单据号码」排序，方便分单装车</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        :empty-title="空标题"
        :empty-desc="空说明"
        empty-icon="Van"
        @retry="查询数据"
      >
        <template #emptyAction>
          <el-button @click="重置">
            <el-icon><Refresh /></el-icon> 改看今天
          </el-button>
        </template>

        <!-- 打印区：驾驶室/客户签收用的正式清单，表头每页重复 -->
        <div class="print-area">
          <div class="doc-head">
            <div class="doc-title">配送清单{{ 单据日期 ? ` · ${单据日期}` : '' }}</div>
            <div class="doc-meta">
              <span>送瓶员：{{ 送瓶员名 || '全部' }}</span>
              <span>共 {{ 合计.行数 }} 行 / {{ 单据数 }} 单</span>
              <span>送气量 {{ 合计.送气量 }} 瓶</span>
              <span>收空瓶 {{ 合计.收瓶量 }} 只</span>
              <span>金额 ¥{{ 金额(合计.金额) }}</span>
            </div>
          </div>

          <div class="table-wrap table-scroll-hint print-table">
            <el-table :data="行" size="small" stripe max-height="calc(100vh - 320px)">
              <el-table-column prop="送瓶员" label="送瓶员" width="100" show-overflow-tooltip>
                <template #default="{ row }">{{ row.送瓶员 || '—' }}</template>
              </el-table-column>
              <el-table-column prop="单据号码" label="单据号码" width="170" show-overflow-tooltip />
              <el-table-column prop="客户" label="客户" min-width="150" show-overflow-tooltip>
                <template #default="{ row }">{{ row.客户 || '—' }}</template>
              </el-table-column>
              <el-table-column prop="地址" label="地址" min-width="220" show-overflow-tooltip>
                <template #default="{ row }">{{ row.地址 || '—' }}</template>
              </el-table-column>
              <el-table-column label="电话" width="140">
                <template #default="{ row }">
                  <span class="num">{{ row.电话 || '—' }}</span>
                </template>
              </el-table-column>
              <el-table-column prop="规格" label="规格" width="140" show-overflow-tooltip>
                <template #default="{ row }">{{ row.规格 || '—' }}</template>
              </el-table-column>
              <el-table-column label="送气量" width="100" align="right">
                <template #default="{ row }">
                  <MoneyText :value="row.送气量" :digits="0" />
                </template>
              </el-table-column>
              <el-table-column label="收空瓶" width="100" align="right">
                <template #default="{ row }">
                  <MoneyText :value="row.收瓶量" :digits="0" />
                </template>
              </el-table-column>
              <el-table-column label="金额" width="130" align="right">
                <template #default="{ row }"><MoneyText :value="row.金额" prefix /></template>
              </el-table-column>
            </el-table>
          </div>

          <div class="sum-bar">
            <span>合计（{{ 合计.行数 }} 行 / {{ 单据数 }} 单 / {{ 客户数 }} 个客户）</span>
            <span>送气量 <b>{{ 合计.送气量.toLocaleString('zh-CN') }}</b> 瓶</span>
            <span>收空瓶 <b>{{ 合计.收瓶量.toLocaleString('zh-CN') }}</b> 只</span>
            <span class="sum-bar__danger">金额 <b>¥{{ 金额(合计.金额) }}</b></span>
          </div>

          <p class="sign">送瓶员签字：______________　　发货人：______________　　日期：______________</p>
        </div>

        <div class="caliber-note">
          <b>口径</b>：行 = 所选日期内<b>已过账送气单</b>的明细（送气日期 ≥ 当日 00:00 且 &lt; 次日 00:00），
          按「送瓶员 → 单据号码」排序；指定送瓶员时按该员工的送气单过滤。
          <b>送气量</b>取明细实瓶数量、<b>收空瓶</b>取明细空瓶数量、<b>金额</b>取明细金额；
          一行 = 一张送气单的一个规格明细，所以行数通常大于单据数。
          顶部指标卡与合计条按<b>当前清单</b>汇总；打印时筛选条与操作按钮带 <b>no-print</b>，只输出标题、明细表与签字栏。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 单据抬头：屏幕上是卡片式标题，打印时转成居中黑字 */
.doc-head {
  display: flex;
  align-items: baseline;
  gap: var(--sp-4);
  flex-wrap: wrap;
  padding: var(--sp-2) 0 var(--sp-3);
}

.doc-title {
  font-size: var(--fs-lg);
  font-weight: var(--fw-semibold);
  color: var(--c-ink-900);
}

.doc-meta {
  display: flex;
  align-items: center;
  gap: var(--sp-4);
  flex-wrap: wrap;
  font-size: var(--fs-xs);
  color: var(--c-ink-400);
}

/* 签字栏：屏幕弱化，打印时保留 */
.sign {
  margin: var(--sp-6) 0 0;
  font-size: var(--fs-sm);
  color: var(--c-ink-500);
}

@media print {
  .doc-head {
    display: block;
    text-align: center;
    padding: 0 0 var(--sp-2);
  }

  .doc-title {
    font-size: var(--fs-xl);
    font-weight: var(--fw-bold);
  }

  .doc-meta {
    justify-content: center;
    color: var(--c-ink-900);
  }

  .sign {
    margin-top: var(--sp-8);
  }

  /* el-table 的 max-height 会把内容裁在滚动容器里，打印前必须放开，
     否则纸面上只有屏幕上可见的那几行 */
  .print-table {
    border: none;
    overflow: visible;
  }

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

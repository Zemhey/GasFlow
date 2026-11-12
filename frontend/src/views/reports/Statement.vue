<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api from '@/api'
import { useLookupStore } from '@/stores/lookup'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

const lookup = useLookupStore()
const 结果 = ref<any>(null)
const 加载中 = ref(false)
const 错误 = ref('')
/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)
const 查询 = reactive<any>({ 客户编号: null, 日期: [] as string[] })

function 本月() {
  const d = new Date()
  const fmt = (x: Date) =>
    `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`
  查询.日期 = [fmt(new Date(d.getFullYear(), d.getMonth(), 1)), fmt(d)]
}

/** 明细行（接口字段固定，这里集中取一次避免模板里到处判空） */
const 明细 = computed<any[]>(() => 结果.value?.明细 ?? [])

/** 合计优先取接口的合计对象；接口缺失时回退为对明细求和（口径一致，不改契约） */
const 合计 = computed(() => {
  const t = 结果.value?.合计 ?? {}
  const 销量 = 明细.value.reduce((s, r) => s + Number(r.数量 ?? 0), 0)
  const 金额 = 明细.value.reduce((s, r) => s + Number(r.金额 ?? 0), 0)
  const 空瓶 = 明细.value.reduce((s, r) => s + Number(r.空瓶数量 ?? 0), 0)
  return {
    单数: t.单数 ?? new Set(明细.value.map((r) => String(r.单据号码 ?? ''))).size,
    销量: t.销量 ?? 销量,
    金额: t.金额 ?? 金额,
    空瓶
  }
})

/** 规格数：对账单一页纸能列出几种规格，方便核对 */
const 规格数 = computed(() => new Set(明细.value.map((r) => String(r.规格 ?? '')).filter(Boolean)).size)

function 金额(v: any): string {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function 日期(v: any): string {
  const s = String(v ?? '')
  return s ? s.slice(0, 10) : '—'
}

function 客户名称() {
  const c = 结果.value?.客户
  if (!c) return ''
  if (typeof c === 'string') return c
  return c.客户全称 || c.客户简称 || c.客户
}

const 期间 = computed(() => {
  const 起 = 日期(结果.value?.起)
  const 止 = 日期(结果.value?.止)
  return `${起} ~ ${止}`
})

const 已选客户名 = computed(() =>
  查询.客户编号 ? lookup.名称('客户', 查询.客户编号) : ''
)

const 空标题 = '尚未生成对账单'
const 空说明 =
  '对账单用于与客户核对应收余额。请先在筛选条里选择客户与期间，再点「生成对账单」；生成后可直接打印或导出 PDF 交客户签字确认。'

async function 查询数据() {
  if (!查询.客户编号) return
  加载中.value = true
  错误.value = ''
  try {
    结果.value = await api.reports.客户对账单({
      客户编号: 查询.客户编号,
      from: 查询.日期?.[0],
      to: 查询.日期?.[1]
    })
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '对账单生成失败，请稍后重试'
    结果.value = null
  } finally {
    加载中.value = false
  }
}

function 打印() {
  window.print()
}

/** 导出对账单明细为 CSV（纯前端；正式签收仍走打印/PDF） */
function 导出() {
  if (!明细.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = ['日期', '单据号码', '规格', '数量', '单位', '单价', '金额', '空瓶数量']
  const 体 = 明细.value.map((r) =>
    [
      日期(r.日期),
      r.单据号码,
      r.规格,
      Number(r.数量 ?? 0),
      r.单位,
      Number(r.单价 ?? 0).toFixed(2),
      Number(r.金额 ?? 0).toFixed(2),
      Number(r.空瓶数量 ?? 0)
    ]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const 尾 = [
    `"合计（${合计.value.单数} 单 / 销量 ${合计.value.销量}）"`,
    '',
    '',
    '',
    '',
    '',
    `"${合计.value.金额.toFixed(2)}"`,
    ''
  ].join(',')
  const csv = '\uFEFF' + [表头.join(','), ...体, 尾].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `客户对账单_${客户名称() || 查询.客户编号}_${期间.value.replace(/[ ~]/g, '')}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${明细.value.length} 行`)
}

onMounted(async () => {
  await lookup.加载()
  本月()
})
</script>

<template>
  <div>
    <PageHead
      title="客户对账单"
      icon="Document"
      sub="按客户 + 期间生成应收对账单 · 明细取已过账送气单，可打印或导出 PDF 交客户确认"
    >
      <template #actions>
        <el-button :disabled="!结果 || !明细.length" @click="导出">
          <el-icon><Download /></el-icon> 导出 CSV
        </el-button>
        <el-button :disabled="!结果" @click="打印">
          <el-icon><Printer /></el-icon> 打印 / 导出 PDF
        </el-button>
        <el-button type="primary" :loading="加载中" @click="查询数据">
          <el-icon><Refresh /></el-icon> 生成对账单
        </el-button>
      </template>
    </PageHead>

    <!-- 已生成时用指标卡把"期初 / 本期 / 期末"三个数提到最上面 -->
    <div v-if="结果" class="stat-grid no-print">
      <StatCard
        label="期初应收"
        icon="Wallet"
        tone="info"
        :value="`¥${金额(结果.期初应收)}`"
        sub="期间开始前该客户的应收余额"
      />
      <StatCard
        label="本期送气金额"
        icon="Money"
        tone="warning"
        :value="`¥${金额(合计.金额)}`"
        :sub="`<b>${合计.单数}</b> 单 · 销量 <b>${合计.销量}</b>`"
      />
      <StatCard
        label="期末应收"
        icon="TrendCharts"
        :tone="Number(结果.期末应收 ?? 0) > 0 ? 'danger' : 'success'"
        :value="`¥${金额(结果.期末应收)}`"
        :sub="`规格 <b>${规格数}</b> 种 · 回收空瓶 <b>${合计.空瓶}</b> 只`"
      />
      <StatCard
        label="对账期间"
        icon="Calendar"
        tone="info"
        :value="期间"
        :sub="客户名称() ? `<b>${客户名称()}</b>` : '—'"
      />
    </div>

    <el-card shadow="never">
      <button
        class="mobile-filter-toggle el-button el-button--default no-print"
        @click="筛选展开 = !筛选展开"
      >
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="查询.客户编号" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible no-print" :class="{ 'is-open': 筛选展开 }">
        <el-select
          v-model="查询.客户编号"
          filterable
          clearable
          placeholder="选择客户"
          style="width: 240px"
        >
          <el-option v-for="o in lookup.选项('客户')" :key="o.value" :label="o.label" :value="o.value" />
        </el-select>
        <el-date-picker
          v-model="查询.日期"
          type="daterange"
          value-format="YYYY-MM-DD"
          start-placeholder="开始"
          end-placeholder="结束"
          style="width: 260px"
        />
        <el-button type="primary" @click="查询数据">
          <el-icon><Search /></el-icon> 生成对账单
        </el-button>
        <el-button @click="本月">
          <el-icon><RefreshLeft /></el-icon> 重置为本月
        </el-button>

        <span class="spacer" />
        <span class="hint">明细按送气日期排序，每页重复表头</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!结果"
        :empty-title="空标题"
        :empty-desc="空说明"
        empty-icon="Document"
        @retry="查询数据"
      >
        <template #emptyAction>
          <el-button v-if="!查询.客户编号" type="primary" @click="筛选展开 = true">
            <el-icon><Filter /></el-icon> 打开筛选，选择客户
          </el-button>
          <el-button v-else type="primary" @click="查询数据">
            <el-icon><Refresh /></el-icon> 重新生成对账单
          </el-button>
        </template>

        <!-- 打印区：正式对账单只输出这一块（含抬头、明细表、合计与签字栏） -->
        <div class="print-area sheet">
          <h2 class="doc-title">客户对账单</h2>
          <div class="meta">
            <span>客户：{{ 客户名称() || 已选客户名 || '—' }}</span>
            <span>期间：{{ 期间 }}</span>
            <span>期初应收：¥{{ 金额(结果?.期初应收) }}</span>
          </div>

          <div class="table-wrap table-scroll-hint grid-wrap">
            <table class="grid">
              <thead>
                <tr>
                  <th>日期</th>
                  <th>单据号码</th>
                  <th>规格</th>
                  <th class="num">数量</th>
                  <th>单位</th>
                  <th class="num">单价</th>
                  <th class="num">金额</th>
                  <th class="num">空瓶</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(r, i) in 明细" :key="i">
                  <td class="num">{{ 日期(r.日期) }}</td>
                  <td>{{ r.单据号码 || '—' }}</td>
                  <td>{{ r.规格 || '—' }}</td>
                  <td class="num">{{ r.数量 ?? 0 }}</td>
                  <td>{{ r.单位 || '—' }}</td>
                  <td class="num">{{ 金额(r.单价) }}</td>
                  <td class="num">{{ 金额(r.金额) }}</td>
                  <td class="num">{{ r.空瓶数量 ?? 0 }}</td>
                </tr>
                <!-- 期间内没有送气单：仍然出一张"零明细"的正式对账单，而不是留白 -->
                <tr v-if="!明细.length">
                  <td colspan="8" class="row-empty">
                    本期该客户没有已过账的送气单据，应收无增减
                  </td>
                </tr>
              </tbody>
              <tfoot>
                <tr>
                  <td colspan="3">合计（{{ 合计.单数 }} 单 / 销量 {{ 合计.销量 }} / 空瓶 {{ 合计.空瓶 }}）</td>
                  <td colspan="3" />
                  <td class="num">{{ 金额(合计.金额) }}</td>
                  <td />
                </tr>
                <tr>
                  <td colspan="6">期末应收</td>
                  <td class="num strong">{{ 金额(结果?.期末应收) }}</td>
                  <td />
                </tr>
              </tfoot>
            </table>
          </div>

          <div class="sum-bar no-print">
            <span>共 {{ 明细.length }} 行明细</span>
            <span>送气金额 <b>¥{{ 金额(合计.金额) }}</b></span>
            <span class="sum-bar__danger">期末应收 <b>¥{{ 金额(结果?.期末应收) }}</b></span>
          </div>

          <p class="sign">制单：__________　　复核：__________　　客户确认：__________</p>
        </div>

        <div class="caliber-note">
          <b>口径</b>：期间起止取所选日期的 00:00（含）至止日 +1 天 00:00（不含），
          明细 = 该客户在此期间内<b>已过账送气单</b>的单据明细（规格取钢瓶规格表），按送气日期 → 送气单编号 → 明细编号排序；
          <b>合计</b>按送气单表去重统计单数、销量合计与金额合计。
          <b>期初应收</b>取接口返回值（当前后端实现为客户对账单的期初，即期初应收余额），
          <b>期末应收 = 期初应收 + 本期送气金额 − 本期已收/退货冲减</b>，由接口计算并返回，前端不做加减。
          本页展示的是「送气挂账」口径的明细；正式余额以<b>应收单</b>为准（阶段 5 口径）。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 对账单纸张：屏幕上是一张"纸"，打印时去掉内边距与边框 */
.sheet {
  padding: var(--sp-4) var(--sp-5);
  background: var(--bg-surface);
  border: 1px solid var(--c-border-light);
  border-radius: var(--r-md);
}

.doc-title {
  margin: 0 0 var(--sp-3);
  text-align: center;
  font-size: var(--fs-2xl);
}

.meta {
  display: flex;
  gap: var(--sp-6);
  flex-wrap: wrap;
  margin-bottom: var(--sp-3);
  font-size: var(--fs-sm);
  color: var(--c-ink-500);
}

/* 原生表格：对账单必须用 table 才能在打印时分页并重复表头。
   窄屏下 8 列会挤成一团，给一个最小宽度让 .table-scroll-hint 的横滚真正可用。 */
.grid {
  width: 100%;
  min-width: 40rem;
  border-collapse: collapse;
  font-size: var(--fs-sm);
}

.grid th,
.grid td {
  padding: var(--sp-2) var(--sp-3);
  border: 1px solid var(--c-border-light);
  text-align: left;
}

.grid th {
  background: var(--bg-surface-alt);
  font-weight: var(--fw-semibold);
  color: var(--c-ink-700);
}

.grid .num {
  text-align: right;
}

.grid .strong {
  font-weight: var(--fw-bold);
}

.grid tfoot td {
  background: var(--bg-surface-alt);
  font-weight: var(--fw-semibold);
}

.row-empty {
  text-align: center;
  color: var(--c-ink-400);
}

.sign {
  margin: var(--sp-6) 0 0;
  font-size: var(--fs-sm);
  color: var(--c-ink-500);
}

/* 打印：只留对账单本体，去掉屏幕上的纸张边框与阴影 */
@media print {
  .sheet {
    padding: 0;
    border: none;
    border-radius: 0;
  }

  .grid-wrap {
    border: none;
    overflow: visible;
  }

  .doc-title {
    font-size: var(--fs-xl);
  }

  /* 打印必须回到纸面宽度，min-width 会把表格顶出版心 */
  .grid {
    min-width: 0;
  }

  .sign {
    margin-top: var(--sp-8);
  }
}
</style>

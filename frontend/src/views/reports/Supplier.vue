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
const 查询 = reactive<any>({ 供气商编号: null, 日期: [] as string[] })

function 本月() {
  const d = new Date()
  const fmt = (x: Date) =>
    `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`
  查询.日期 = [fmt(new Date(d.getFullYear(), d.getMonth(), 1)), fmt(d)]
}

const 明细 = computed<any[]>(() => 结果.value?.明细 ?? [])

/** 合计优先取接口的合计对象，缺失时回退为对明细求和（口径一致，不改契约） */
const 合计 = computed(() => {
  const t = 结果.value?.合计 ?? {}
  const 实瓶 = 明细.value.reduce((s, r) => s + Number(r.实瓶数量 ?? 0), 0)
  const 气重 = 明细.value.reduce((s, r) => s + Number(r.单瓶气重公斤 ?? 0), 0)
  const 金额 = 明细.value.reduce((s, r) => s + Number(r.金额 ?? 0), 0)
  return {
    单数: t.单数 ?? new Set(明细.value.map((r) => String(r.单据号码 ?? ''))).size,
    金额: t.金额 ?? 金额,
    实瓶,
    气重,
    均单价: 实瓶 ? 金额 / 实瓶 : 0
  }
})

/** 车牌数：一天几台车来拉货，调度要看 */
const 车牌数 = computed(() => new Set(明细.value.map((r) => String(r.车牌 ?? '')).filter(Boolean)).size)

function 金额(v: any): string {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function 日期(v: any): string {
  const s = String(v ?? '')
  return s ? s.slice(0, 10) : '—'
}

function 供气商名() {
  const g = 结果.value?.供气商
  if (!g) return ''
  if (typeof g === 'string') return g
  return g.简称 || g.全称 || g.供气商
}

const 期间 = computed(() => `${日期(结果.value?.起)} ~ ${日期(结果.value?.止)}`)

const 已选供气商名 = computed(() =>
  查询.供气商编号 ? lookup.名称('供气商', 查询.供气商编号) : ''
)

const 空标题 = '尚未生成对账单'
const 空说明 =
  '对账单用于与供气商核对进气量与应付金额。请先在筛选条里选择供气商与期间，再点「生成对账单」；生成后可直接打印交供气商确认。'

async function 查询数据() {
  if (!查询.供气商编号) return
  加载中.value = true
  错误.value = ''
  try {
    结果.value = await api.reports.供气商对账单({
      供气商编号: 查询.供气商编号,
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

/** 打印：必须走 script 里的函数，模板里写 window.print() 会被编译成 _ctx.window（undefined） */
function 打印() {
  window.print()
}

/** 导出对账单明细为 CSV（纯前端；正式签收仍走打印） */
function 导出() {
  if (!明细.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = ['日期', '单据号码', '车牌', '规格', '实瓶数', '单瓶气重', '单瓶合算价', '金额']
  const 体 = 明细.value.map((r) =>
    [
      日期(r.日期),
      r.单据号码,
      r.车牌,
      r.规格,
      Number(r.实瓶数量 ?? 0),
      Number(r.单瓶气重公斤 ?? 0),
      Number(r.单瓶合算价格 ?? 0).toFixed(2),
      Number(r.金额 ?? 0).toFixed(2)
    ]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const 尾 = [`"合计（${合计.value.单数} 单）"`, '', '', '', '', '', '', `"${合计.value.金额.toFixed(2)}"`].join(',')
  const csv = '\uFEFF' + [表头.join(','), ...体, 尾].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `供气商对账单_${供气商名() || 查询.供气商编号}_${期间.value.replace(/[ ~]/g, '')}.csv`
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
      title="供气商对账"
      icon="OfficeBuilding"
      sub="按供气商 + 期间生成进气对账单 · 明细取已过账进气单，可打印交供气商确认"
    >
      <template #actions>
        <el-button :disabled="!结果 || !明细.length" @click="导出">
          <el-icon><Download /></el-icon> 导出 CSV
        </el-button>
        <el-button :disabled="!结果" @click="打印">
          <el-icon><Printer /></el-icon> 打印
        </el-button>
        <el-button type="primary" :loading="加载中" @click="查询数据">
          <el-icon><Refresh /></el-icon> 生成对账单
        </el-button>
      </template>
    </PageHead>

    <div v-if="结果" class="stat-grid no-print">
      <StatCard
        label="进气金额合计"
        icon="Money"
        tone="warning"
        :value="`¥${金额(合计.金额)}`"
        :sub="`<b>${合计.单数}</b> 单 · 期间 <b>${期间}</b>`"
      />
      <StatCard
        label="实瓶数合计"
        icon="Box"
        :value="合计.实瓶.toLocaleString('zh-CN')"
        unit="瓶"
        :sub="`车牌 <b>${车牌数}</b> 台 · 单瓶均合算价 <b>¥${金额(合计.均单价)}</b>`"
      />
      <StatCard
        label="单瓶气重合计"
        icon="Odometer"
        tone="info"
        :value="合计.气重.toLocaleString('zh-CN')"
        unit="公斤"
        sub="明细单瓶气重公斤之和（按行累加）"
      />
      <StatCard
        label="供气商"
        icon="OfficeBuilding"
        tone="info"
        :value="供气商名() || '—'"
        :sub="`明细 <b>${明细.length}</b> 行 · 规格按钢瓶规格归类`"
      />
    </div>

    <el-card shadow="never">
      <button
        class="mobile-filter-toggle el-button el-button--default no-print"
        @click="筛选展开 = !筛选展开"
      >
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="查询.供气商编号" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible no-print" :class="{ 'is-open': 筛选展开 }">
        <el-select
          v-model="查询.供气商编号"
          filterable
          clearable
          placeholder="选择供气商"
          style="width: 220px"
        >
          <el-option v-for="o in lookup.选项('供气商')" :key="o.value" :label="o.label" :value="o.value" />
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
        <span class="hint">明细按进气日期 → 进气单编号排序，每页重复表头</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!结果"
        :empty-title="空标题"
        :empty-desc="空说明"
        empty-icon="OfficeBuilding"
        @retry="查询数据"
      >
        <template #emptyAction>
          <el-button v-if="!查询.供气商编号" type="primary" @click="筛选展开 = true">
            <el-icon><Filter /></el-icon> 打开筛选，选择供气商
          </el-button>
          <el-button v-else type="primary" @click="查询数据">
            <el-icon><Refresh /></el-icon> 重新生成对账单
          </el-button>
        </template>

        <!-- 打印区：正式供气商对账单只输出这一块 -->
        <div class="print-area sheet">
          <h2 class="doc-title">供气商对账单</h2>
          <p class="meta">
            <span>供气商：{{ 供气商名() || 已选供气商名 || '—' }}</span>
            <span>期间：{{ 期间 }}</span>
            <span>车牌：{{ 车牌数 }} 台</span>
          </p>

          <div class="table-wrap table-scroll-hint grid-wrap">
            <table class="grid">
              <thead>
                <tr>
                  <th>日期</th>
                  <th>单据号码</th>
                  <th>车牌</th>
                  <th>规格</th>
                  <th class="num">实瓶数</th>
                  <th class="num">单瓶气重</th>
                  <th class="num">单瓶合算价</th>
                  <th class="num">金额</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(r, i) in 明细" :key="i">
                  <td class="num">{{ 日期(r.日期) }}</td>
                  <td>{{ r.单据号码 || '—' }}</td>
                  <td>{{ r.车牌 || '—' }}</td>
                  <td>{{ r.规格 || '—' }}</td>
                  <td class="num">{{ r.实瓶数量 ?? 0 }}</td>
                  <td class="num">{{ r.单瓶气重公斤 ?? 0 }}</td>
                  <td class="num">{{ 金额(r.单瓶合算价格) }}</td>
                  <td class="num">{{ 金额(r.金额) }}</td>
                </tr>
                <!-- 期间内没有进气单：仍然出一张"零明细"的正式对账单，而不是留白 -->
                <tr v-if="!明细.length">
                  <td colspan="8" class="row-empty">
                    本期该供气商没有已过账的进气单据，应付无增减
                  </td>
                </tr>
              </tbody>
              <tfoot>
                <tr>
                  <td colspan="4">合计（{{ 合计.单数 }} 单 / 实瓶 {{ 合计.实瓶 }}）</td>
                  <td colspan="3" />
                  <td class="num">{{ 金额(合计.金额) }}</td>
                </tr>
              </tfoot>
            </table>
          </div>

          <div class="sum-bar no-print">
            <span>共 {{ 明细.length }} 行明细</span>
            <span>实瓶 <b>{{ 合计.实瓶.toLocaleString('zh-CN') }}</b> 瓶</span>
            <span class="sum-bar__danger">进气金额 <b>¥{{ 金额(合计.金额) }}</b></span>
          </div>

          <p class="sign">制单：__________　　复核：__________　　供气商确认：__________</p>
        </div>

        <div class="caliber-note">
          <b>口径</b>：期间起止取所选日期的 00:00（含）至止日 +1 天 00:00（不含），
          明细 = 该供气商在此期间内<b>已过账进气单</b>的明细（规格取钢瓶规格表、车牌取打气车表），
          按进气日期 → 进气单编号排序；
          <b>单瓶合算价格</b>与<b>金额</b>均由明细行给出，其中 <b>金额 = 实瓶数量 × 单瓶合算价格</b>（数据库计算，前端不重算）；
          <b>合计</b>按进气单主干表去重统计单数与金额合计。
          顶部的「进气金额合计」优先取接口返回的合计对象，接口缺失时才回退为对明细求和；
          「单瓶气重合计」是把每一行的单瓶气重公斤数<b>累加</b>（不是加权平均）；「单瓶均合算价」= 金额合计 ÷ 实瓶数合计。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 对账单纸张：屏幕上是"一张纸"，打印时去掉边框与内边距 */
.sheet {
  padding: var(--sp-4) var(--sp-5);
  background: var(--bg-surface);
  border: 1px solid var(--c-border-light);
  border-radius: var(--r-md);
}

.doc-title {
  margin: 0 0 var(--sp-2);
  text-align: center;
  font-size: var(--fs-2xl);
}

.meta {
  display: flex;
  justify-content: center;
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

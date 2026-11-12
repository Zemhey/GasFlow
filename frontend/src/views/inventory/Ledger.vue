<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api, { type 流水行 } from '@/api'
import { useLookupStore } from '@/stores/lookup'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

const lookup = useLookupStore()
const 行 = ref<流水行[]>([])
const 总数 = ref(0)
const 加载中 = ref(false)
const 错误 = ref('')
/** 移动端（≤767px）筛选条默认收起，由「筛选」开关展开 */
const 筛选展开 = ref(false)

function 本月(): string[] {
  const d = new Date()
  const f = new Date(d.getFullYear(), d.getMonth(), 1)
  const fmt = (x: Date) =>
    `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`
  return [fmt(f), fmt(d)]
}

const 查询 = reactive({
  page: 1,
  size: 50,
  规格编号: null as number | null,
  日期: 本月() as string[]
})

/* 口径说明与 KPI 都只基于「当前页」的流水行，避免把分页数据说成全期间合计。 */

/** 当前页 KPI：由已取回的这一页流水直接算出，不额外请求 */
const 本页 = computed(() => {
  const t = 行.value
  let 实瓶 = 0
  let 空瓶 = 0
  let 入 = 0
  let 出 = 0
  const 单据 = new Set<string>()
  t.forEach((r) => {
    const s = Number(r.实瓶变动 ?? 0)
    const k = Number(r.空瓶变动 ?? 0)
    实瓶 += s
    空瓶 += k
    if (s > 0) 入 += 1
    else if (s < 0) 出 += 1
    // 期初结转等流水可能没有单据号码，那种行不计入"涉及单据"
    const 号 = String(r.单据号码 ?? '')
    if (号) 单据.add(`${String(r.单据类型 ?? '')}#${号}`)
  })
  return { 实瓶, 空瓶, 入, 出, 单据: 单据.size }
})

/** 是否用了非默认条件（默认是「本月 + 全部规格」），空态文案与出口按钮都靠它 */
const 有筛选 = computed(() => {
  const 默认 = 本月()
  const d = 查询.日期 ?? []
  return (
    !!查询.规格编号 || d.length !== 2 || d[0] !== 默认[0] || d[1] !== 默认[1]
  )
})

const 日期文本 = computed(() => {
  const d = 查询.日期
  return d?.length === 2 ? `${d[0]} ~ ${d[1]}` : '全部日期'
})

/** 变动数：正数绿、负数红、0 灰（颜色语义来自全局 .up/.down/.flat） */
function 变动色(v: any): 'up' | 'down' | 'flat' {
  const n = Number(v ?? 0)
  if (n > 0) return 'up'
  if (n < 0) return 'down'
  return 'flat'
}

/** 带符号 + 千分位：变动列与 KPI 共用，正数带 + */
function 带符号(v: any): string {
  const n = Number(v ?? 0)
  if (!Number.isFinite(n) || n === 0) return '0'
  const t = Math.abs(n).toLocaleString('zh-CN')
  return n > 0 ? `+${t}` : `-${t}`
}

/** 业务日期只用到日（后端返回的是 date，个别驱动会带时间部分） */
function 日期(v: any): string {
  const s = String(v ?? '').slice(0, 10)
  return s || '—'
}

function 单据类型名(t: any): string {
  const m: Record<string, string> = {
    delivery: '送气单',
    intake: '进气单',
    borrow: '借瓶单',
    return: '还瓶单',
    warehouse: '入仓单',
    stocktake: '每日盘点',
    deposit: '押金收据',
    receipt: '收款单',
    payment: '付款单',
    commission: '提成结算'
  }
  return m[String(t ?? '')] ?? String(t ?? '')
}

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const params: any = { page: 查询.page, size: 查询.size }
    if (查询.规格编号) params.规格编号 = 查询.规格编号
    if (查询.日期?.length === 2) {
      params.from = 查询.日期[0]
      params.to = 查询.日期[1]
    }
    const r = await api.inventory.流水(params)
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '库存流水加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

/** 条件一变就回到第 1 页再查 */
function 查询首页() {
  查询.page = 1
  加载()
}

function 重置() {
  查询.规格编号 = null
  查询.日期 = 本月()
  查询.page = 1
  加载()
}

/** 空态出口：本月没有流水时，把区间放宽到本年初再查一次 */
function 放宽到今年() {
  const d = new Date()
  const fmt = (x: Date) =>
    `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`
  查询.规格编号 = null
  查询.日期 = [`${d.getFullYear()}-01-01`, fmt(d)]
  查询.page = 1
  加载()
}

/** 导出当前页为 CSV（纯前端，不动任何接口） */
function 导出() {
  if (!行.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = [
    '业务日期',
    '规格名称',
    '业务类型',
    '实瓶变动',
    '空瓶变动',
    '单据类型',
    '单据号码',
    '客户',
    '供气商',
    '备注'
  ]
  const 体 = 行.value.map((r) =>
    [
      日期(r.业务日期),
      r.规格名称,
      r.业务类型名称,
      r.实瓶变动,
      r.空瓶变动,
      单据类型名(r.单据类型),
      r.单据号码,
      r.客户,
      r.供气商,
      r.备注
    ]
      .map((v) => `"${String(v ?? '').replace(/"/g, '""')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `出入库流水_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${行.value.length} 行`)
}

onMounted(async () => {
  await lookup.加载()
  await 加载()
})
</script>

<template>
  <div>
    <PageHead
      title="出入库流水"
      icon="List"
      sub="钢瓶库存流水台账（只增不改）· 每一行是一次过账留下的数量变动，反过账/冲销会撤掉对应流水"
    >
      <template #actions>
        <el-button :disabled="!行.length" @click="导出">
          <el-icon><Download /></el-icon> 导出本页
        </el-button>
        <el-button type="primary" :loading="加载中" @click="查询首页">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <!-- KPI：只统计「当前这一页」，口径写在卡片副标题里，避免被误读成全期间合计 -->
    <div class="stat-grid">
      <StatCard
        label="本页流水"
        icon="List"
        :value="行.length.toLocaleString('zh-CN')"
        unit="条"
        :sub="`符合条件共 <b>${总数.toLocaleString('zh-CN')}</b> 条 · 每页 ${查询.size} 条`"
      />
      <StatCard
        label="本页实瓶净变动"
        icon="Box"
        :value="带符号(本页.实瓶)"
        unit="瓶"
        :tone="本页.实瓶 > 0 ? 'success' : 本页.实瓶 < 0 ? 'danger' : 'info'"
        :sub="`其中入 <b>${本页.入}</b> 条 · 出 <b>${本页.出}</b> 条`"
      />
      <StatCard
        label="本页空瓶净变动"
        icon="Coin"
        :value="带符号(本页.空瓶)"
        unit="瓶"
        :tone="本页.空瓶 > 0 ? 'success' : 本页.空瓶 < 0 ? 'danger' : 'info'"
        sub="空瓶变动由借瓶/还瓶与入仓出仓产生"
      />
      <StatCard
        label="本页涉及单据"
        icon="Tickets"
        tone="info"
        :value="本页.单据.toLocaleString('zh-CN')"
        unit="张"
        sub="按 <b>单据类型 + 单据号码</b> 去重"
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
        <el-select
          v-model="查询.规格编号"
          clearable
          filterable
          placeholder="全部规格"
          style="width: 170px"
          @change="查询首页"
        >
          <el-option
            v-for="o in lookup.选项('商品')"
            :key="o.value"
            :label="o.label"
            :value="o.value"
          />
        </el-select>

        <el-date-picker
          v-model="查询.日期"
          type="daterange"
          value-format="YYYY-MM-DD"
          start-placeholder="开始日期"
          end-placeholder="结束日期"
          style="width: 250px"
          @change="查询首页"
        />

        <el-button type="primary" @click="查询首页">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <span class="hint">本页 {{ 行.length }} 条 / 共 {{ 总数.toLocaleString('zh-CN') }} 条</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        :empty-title="有筛选 ? '当前条件下没有出入库流水' : '本月没有出入库流水'"
        :empty-desc="
          有筛选
            ? '放宽日期区间或清空规格再看；流水按业务日期归档，跨月数据需要把开始日期往前移。'
            : '台账只由过账产生：先在单据上完成「提交 → 审核 → 过账」，这里才会出现对应流水（当前默认查本月）。'
        "
        empty-icon="List"
        :skeleton-rows="10"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置">
            <el-icon><RefreshLeft /></el-icon> 恢复默认（本月）
          </el-button>
          <el-button v-else @click="放宽到今年">
            <el-icon><Calendar /></el-icon> 放宽到今年以来
          </el-button>
        </template>

        <!-- 颜色图例：变动列的正负含义（放在表格上方，窄屏自动换行） -->
        <div class="row-wrap mb-2">
          <div class="legend">
            <span class="legend__item">
              <i class="legend__swatch sw-up" />正数 = 结存增加（入仓 / 归还等）
            </span>
            <span class="legend__item">
              <i class="legend__swatch sw-down" />负数 = 结存减少（送气 / 借出等）
            </span>
            <span class="legend__item">
              <i class="legend__swatch sw-flat" />0 = 该维度本期无变动
            </span>
          </div>
        </div>

        <div class="table-wrap table-scroll-hint">
          <el-table :data="行" size="small" stripe max-height="calc(100vh - 320px)">
            <el-table-column label="业务日期" width="110">
              <template #default="{ row }">
                <span class="num">{{ 日期(row.业务日期) }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="规格名称" label="规格名称" min-width="140" show-overflow-tooltip />
            <el-table-column prop="业务类型名称" label="业务类型" width="130" show-overflow-tooltip>
              <template #default="{ row }">
                {{ row.业务类型名称 || '—' }}
              </template>
            </el-table-column>
            <el-table-column label="实瓶变动" width="100" align="right">
              <template #default="{ row }">
                <span class="num" :class="变动色(row.实瓶变动)">{{ 带符号(row.实瓶变动) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="空瓶变动" width="100" align="right">
              <template #default="{ row }">
                <span class="num" :class="变动色(row.空瓶变动)">{{ 带符号(row.空瓶变动) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="单据类型" width="110">
              <template #default="{ row }">{{ 单据类型名(row.单据类型) }}</template>
            </el-table-column>
            <el-table-column prop="单据号码" label="单据号码" width="160" show-overflow-tooltip>
              <template #default="{ row }">
                <span class="num">{{ row.单据号码 || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="客户" label="客户" min-width="140" show-overflow-tooltip />
            <el-table-column prop="供气商" label="供气商" min-width="120" show-overflow-tooltip />
            <el-table-column prop="备注" label="备注" min-width="160" show-overflow-tooltip />
          </el-table>
        </div>

        <el-pagination
          class="mt-3"
          style="justify-content: flex-end"
          background
          layout="total, sizes, prev, pager, next"
          :total="总数"
          v-model:current-page="查询.page"
          v-model:page-size="查询.size"
          :page-sizes="[50, 100, 200]"
          @current-change="加载"
          @size-change="查询首页"
        />

        <div class="sum-bar">
          <span>本页合计（{{ 行.length }} 条）</span>
          <span>实瓶净变动 <b>{{ 带符号(本页.实瓶) }}</b></span>
          <span>空瓶净变动 <b>{{ 带符号(本页.空瓶) }}</b></span>
          <span>入 <b>{{ 本页.入 }}</b> 条</span>
          <span class="sum-bar__danger">出 <b>{{ 本页.出 }}</b> 条</span>
          <span>涉及单据 <b>{{ 本页.单据 }}</b> 张</span>
        </div>
      </DataState>

      <div class="caliber-note">
        <b>口径</b>：每一行取自 <b>钢瓶库存流水</b>（台账，只增不改）：业务日期 + 规格 +
        业务类型 + 单据类型/号码 + 实瓶变动 / 空瓶变动，数量为该次过账对结存的增量（正数入、负数出），
        由过账过程写入；<b>反过账 / 冲销会撤掉对应单据的流水</b>，因此"已过账单据数"与"流水条数"不应被手工核对成恒等式。
        规格、业务类型、单据类型、客户的名称由后端关联明细给出（非本页拼装）。
        顶部指标与合计条<b>只统计当前这一页（{{ 查询.size }} 条/页，区间 {{ 日期文本 }}）</b>，
        不是全期间合计；要看某规格的实时结存请用「库存查询」，要看某日的账实差异请用「库存对账」。
        导出按钮导出的是<b>当前页</b>行，不包含未翻到的分页数据。
      </div>
    </el-card>
  </div>
</template>

<style scoped>
/* 图例色块：只能靠 scoped 类名着色，色值仍来自令牌 */
.sw-up { background: var(--c-up); }
.sw-down { background: var(--c-down); }
.sw-flat { background: var(--c-flat); }
</style>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import api, { type 客户欠款行 } from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'
import MoneyText from '@/components/MoneyText.vue'

const router = useRouter()
const 行 = ref<客户欠款行[]>([])
const 总数 = ref(0)
const 加载中 = ref(false)
const 错误 = ref('')
/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)
const 查询 = reactive({ page: 1, size: 50, keyword: '' })

/* ------------------------------------------------------------------ 颜色 */

/**
 * 从 CSS 令牌取色，供 el-progress 的 :color 使用（Element Plus 只接受具体色值，
 * 写死十六进制会在暗色模式下失效）。取值很贵且表格里会逐行调用，所以按
 * 「令牌名 + 当前是否暗色」缓存；主题切换时缓存键变化，自动重新取值，
 * 不需要额外的 MutationObserver。
 */
const 色缓存 = new Map<string, string>()

function 取色(变量名: string, 兜底: string) {
  const 暗色 = document.documentElement.classList.contains('dark')
  const 键 = `${变量名}|${暗色 ? 'dark' : 'light'}`
  const 命中 = 色缓存.get(键)
  if (命中) return 命中
  const 值 = getComputedStyle(document.documentElement).getPropertyValue(变量名).trim() || 兜底
  色缓存.set(键, 值)
  return 值
}

/* ------------------------------------------------------- 信用占用与风险 */

/** 信用额度占用率：应收 / 信用额度（额度为 0 表示不限额，不参与超额判断） */
function 占用率(row: 客户欠款行) {
  const 额度 = Number(row.信用额度 ?? 0)
  if (额度 <= 0) return 0
  return Number(row.应收金额 ?? 0) / 额度
}

function 超额(row: 客户欠款行) {
  const 额度 = Number(row.信用额度 ?? 0)
  return 额度 > 0 && Number(row.应收金额 ?? 0) > 额度
}

/** 占用 80% 以上（含超额）算风险：给一个"快满了"的黄色预警 */
function 风险(row: 客户欠款行) {
  if (超额(row)) return 'danger' as const
  if (占用率(row) >= 0.8) return 'warning' as const
  return 'safe' as const
}

const 超额家数 = computed(() => 行.value.filter(超额).length)
const 关注家数 = computed(() => 行.value.filter((r) => 风险(r) === 'warning').length)
const 应收合计 = computed(() => 行.value.reduce((s, r) => s + Number(r.应收金额 ?? 0), 0))
const 押金合计 = computed(() => 行.value.reduce((s, r) => s + Number(r.押金金额 ?? 0), 0))
const 在借合计 = computed(() => 行.value.reduce((s, r) => s + Number(r.在借瓶数 ?? 0), 0))
/** 有信用额度客户的应收小计：占用率分母，用来算"整体额度使用率" */
const 有额度应收 = computed(() =>
  行.value.filter((r) => Number(r.信用额度 ?? 0) > 0).reduce((s, r) => s + Number(r.应收金额 ?? 0), 0)
)
const 额度合计 = computed(() =>
  行.value.filter((r) => Number(r.信用额度 ?? 0) > 0).reduce((s, r) => s + Number(r.信用额度 ?? 0), 0)
)
/** 整体额度占用率：只对"设了额度"的客户加权，未设额度(0=不限额)不参与 */
const 整体占用率 = computed(() => {
  const 额 = 额度合计.value
  if (!额) return 0
  return Math.round((有额度应收.value / 额) * 100)
})

function 金额(v: any): string {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function 百分比(row: 客户欠款行) {
  const r = 占用率(row)
  if (!Number.isFinite(r) || r === 0) return '—'
  return (r * 100).toFixed(0) + '%'
}

function 进度色(row: 客户欠款行) {
  const t = 风险(row)
  if (t === 'danger') return 取色('--c-danger-500', '#d9483b')
  if (t === 'warning') return 取色('--c-warning-500', '#f5a623')
  return 取色('--c-success-500', '#1e9e5a')
}

/** 最后交易日期：只取日期部分，空值统一显示 — ，避免渲染 undefined */
function 日期(v: any): string {
  const s = String(v ?? '')
  return s ? s.slice(0, 10) : '—'
}

const 有筛选 = computed(() => !!查询.keyword.trim())

const 空标题 = computed(() => (有筛选.value ? '没有符合条件的客户' : '还没有客户欠款数据'))
const 空说明 = computed(() =>
  有筛选.value
    ? '换个客户编码 / 简称 / 电话再试，或点「重置」看全部客户。'
    : '客户欠款以已过账的应收单为准：先录入并过账一张送气单（或退货单），该客户才会出现在这里。'
)

/* -------------------------------------------------------------- 数据加载 */

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r = await api.partners.客户({
      page: 查询.page,
      size: 查询.size,
      keyword: 查询.keyword || undefined
    })
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '客户欠款加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

function 重置() {
  查询.keyword = ''
  查询.page = 1
  加载()
}

/** 跳到账龄分析（带上客户编号） */
function 看账龄(row: 客户欠款行) {
  router.push({ path: '/partners/ageing', query: { 客户编号: String(row.客户编号) } })
}

/** 导出当前页为 CSV（纯前端，不改后端契约） */
function 导出() {
  if (!行.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = [
    '客户编码',
    '客户简称',
    '联系电话',
    '业务员',
    '区域',
    '应收金额',
    '押金金额',
    '在借瓶数',
    '信用额度',
    '额度占用率',
    '最后交易日期'
  ]
  const 体 = 行.value.map((r) =>
    [
      r.客户编码,
      r.客户简称,
      r.联系电话,
      r.业务员,
      r.区域,
      Number(r.应收金额 ?? 0).toFixed(2),
      Number(r.押金金额 ?? 0).toFixed(2),
      Number(r.在借瓶数 ?? 0),
      Number(r.信用额度 ?? 0) > 0 ? Number(r.信用额度).toFixed(2) : '不限额',
      Number(r.信用额度 ?? 0) > 0 ? 百分比(r) : '—',
      日期(r.最后交易日期) === '—' ? '' : 日期(r.最后交易日期)
    ]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `客户欠款_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${行.value.length} 行`)
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="客户欠款"
      icon="Wallet"
      sub="按客户汇总应收 / 押金 / 在借瓶数 · 应收以已过账应收单为准（阶段 5 口径）"
    >
      <template #actions>
        <el-button :disabled="!行.length" @click="导出">
          <el-icon><Download /></el-icon> 导出本页
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <!-- KPI：本页口径（分页后只统计当前页，口径说明里会写清） -->
    <div class="stat-grid">
      <StatCard
        label="应收合计"
        icon="Money"
        tone="warning"
        :value="`¥${金额(应收合计)}`"
        :sub="`本页 <b>${行.length}</b> 家客户`"
      />
      <StatCard
        label="押金合计"
        icon="Coin"
        tone="info"
        :value="`¥${金额(押金合计)}`"
        sub="客户押金账户余额（可退）"
      />
      <StatCard
        label="在借瓶数"
        icon="Box"
        :value="在借合计.toLocaleString('zh-CN')"
        unit="只"
        sub="在外钢瓶：客户尚未归还的瓶数"
      />
      <StatCard
        label="超信用额度"
        icon="Warning"
        :tone="超额家数 ? 'danger' : 'success'"
        :value="超额家数.toLocaleString('zh-CN')"
        unit="家"
        :sub="`接近额度（≥80%）另 <b>${关注家数}</b> 家 · 本页额度占用 <b>${整体占用率}%</b>`"
      />
    </div>

    <el-card shadow="never">
      <button
        class="mobile-filter-toggle el-button el-button--default"
        @click="筛选展开 = !筛选展开"
      >
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="有筛选" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input
          v-model="查询.keyword"
          placeholder="客户编码 / 简称 / 电话"
          clearable
          style="width: 220px"
          @keyup.enter="查询.page = 1; 加载()"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <el-button type="primary" @click="查询.page = 1; 加载()">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <el-tag v-if="超额家数" type="danger" effect="light" size="small">
          <el-icon><Warning /></el-icon> 超信用额度 {{ 超额家数 }} 家
        </el-tag>
        <el-tag v-else type="success" effect="light" size="small">
          <el-icon><CircleCheck /></el-icon> 本页无超额客户
        </el-tag>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        :empty-title="空标题"
        :empty-desc="空说明"
        empty-icon="Wallet"
        :skeleton-rows="查询.size > 50 ? 12 : 8"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置">
            <el-icon><RefreshLeft /></el-icon> 清除筛选
          </el-button>
          <el-button v-else @click="加载">
            <el-icon><Refresh /></el-icon> 刷新
          </el-button>
        </template>

        <div class="legend mb-2">
          <span class="legend__item"><i class="legend__swatch swatch-safe" />占用 &lt; 80% 正常</span>
          <span class="legend__item"><i class="legend__swatch swatch-warn" />占用 ≥ 80% 接近额度</span>
          <span class="legend__item"><i class="legend__swatch swatch-over" />应收 &gt; 额度 超额</span>
          <span class="legend__item"><i class="legend__swatch swatch-none" />未设额度（不限额）</span>
        </div>

        <div class="table-wrap table-scroll-hint">
          <el-table
            :data="行"
            size="small"
            stripe
            :row-class-name="({ row }) => (超额(row) ? 'row-over' : '')"
            max-height="calc(100vh - 320px)"
          >
            <el-table-column prop="客户编码" label="客户编码" width="120" show-overflow-tooltip />
            <el-table-column prop="客户简称" label="客户简称" min-width="150" show-overflow-tooltip />
            <el-table-column prop="联系电话" label="联系电话" width="140">
              <template #default="{ row }">
                <span class="num">{{ row.联系电话 || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="业务员" label="业务员" width="100">
              <template #default="{ row }">{{ row.业务员 || '—' }}</template>
            </el-table-column>
            <el-table-column prop="区域" label="区域" width="110">
              <template #default="{ row }">{{ row.区域 || '—' }}</template>
            </el-table-column>
            <el-table-column label="应收金额" width="140" align="right">
              <template #default="{ row }">
                <span :class="超额(row) ? 'down' : ''">
                  <MoneyText :value="row.应收金额" prefix />
                </span>
              </template>
            </el-table-column>
            <el-table-column label="押金金额" width="130" align="right">
              <template #default="{ row }"><MoneyText :value="row.押金金额" prefix /></template>
            </el-table-column>
            <el-table-column label="在借瓶数" width="110" align="right">
              <template #default="{ row }">
                <MoneyText :value="row.在借瓶数" :digits="0" :prefix="false" />
              </template>
            </el-table-column>
            <el-table-column label="信用额度" width="140" align="right">
              <template #default="{ row }">
                <MoneyText
                  v-if="Number(row.信用额度 ?? 0) > 0"
                  :value="row.信用额度"
                  prefix
                />
                <span v-else class="flat">不限额</span>
              </template>
            </el-table-column>
            <el-table-column label="额度占用" min-width="170">
              <template #default="{ row }">
                <div v-if="Number(row.信用额度 ?? 0) > 0" class="usage">
                  <el-progress
                    :percentage="Math.min(100, Math.round(占用率(row) * 100))"
                    :stroke-width="8"
                    :show-text="false"
                    :color="进度色(row)"
                  />
                  <span class="pct num" :class="超额(row) ? 'down' : ''">{{ 百分比(row) }}</span>
                </div>
                <span v-else class="flat">—</span>
              </template>
            </el-table-column>
            <el-table-column label="最后交易" width="120">
              <template #default="{ row }">
                <span class="num">{{ 日期(row.最后交易日期) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="140" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" @click="看账龄(row)">
                  <el-icon><Clock /></el-icon> 账龄
                </el-button>
                <el-tag v-if="超额(row)" type="danger" size="small" effect="light" class="over-tag">
                  超额
                </el-tag>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>本页合计（{{ 行.length }} 家 / 共 {{ 总数.toLocaleString('zh-CN') }} 家）</span>
          <span>应收 <b>¥{{ 金额(应收合计) }}</b></span>
          <span>押金 <b>¥{{ 金额(押金合计) }}</b></span>
          <span>在借 <b>{{ 在借合计.toLocaleString('zh-CN') }}</b> 只</span>
          <span class="sum-bar__danger">超额度 <b>{{ 超额家数 }}</b> 家</span>
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
          @size-change="查询.page = 1; 加载()"
        />

        <div class="caliber-note">
          <b>口径</b>：应收金额取<b>已过账应收单</b>的未收余额（阶段 5 起以应收单为唯一口径，不再用送气单挂账口径）；
          押金金额取客户押金账户余额；在借瓶数取押金账户借出瓶数（在外钢瓶）。
          <b>额度占用率 = 应收金额 ÷ 信用额度</b>；<b>信用额度为 0 表示不限额</b>，不参与占用率与超额判断。
          顶部指标卡与合计条统计的是<b>本页已加载的 {{ 行.length }} 家客户</b>（非全部 {{ 总数 }} 家），
          「整体额度占用」只对有额度的客户加权。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 额度占用进度条 + 百分数：进度条自适应，百分数定宽右对齐，避免数字跳动 */
.usage {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
}

.usage .el-progress {
  flex: 1 1 auto;
  min-width: 0;
}

.pct {
  flex: 0 0 var(--sp-10);
  text-align: right;
  font-size: var(--fs-xs);
  color: var(--c-ink-500);
}

.over-tag {
  margin-left: var(--sp-1);
}

/* 图例色块：与进度条三档语义色一致 */
.legend__swatch.swatch-safe {
  background: var(--c-success-500);
}

.legend__swatch.swatch-warn {
  background: var(--c-warning-500);
}

.legend__swatch.swatch-over {
  background: var(--c-danger-500);
}

.legend__swatch.swatch-none {
  background: var(--c-ink-200);
}

/* 超额行整体淡红底：一眼扫过去就知道哪几家要催款 */
:deep(.row-over) {
  background: var(--c-danger-50) !important;
}
</style>

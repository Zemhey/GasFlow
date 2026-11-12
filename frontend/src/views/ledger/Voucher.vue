<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import api, { type 凭证行, type 凭证分录行 } from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 凭证台账（批次 16 重写）。
 *
 * 为什么不再用通用视图台账：通用页是 `vw_凭证分录` 一行一条**分录**，
 * 而"红冲/过账"是按**凭证**整张做的动作 —— 在分录行上放按钮，一张 25 行分录的凭证
 * 会变成 25 个红冲入口。所以这里换成"凭证主表一行 + 展开看分录"，动作天然落在正确粒度上。
 *
 * 按钮纪律（批次 15 的教训：死按钮 = 点一次必 409）：
 *   · 只渲染后端算出的 可过账 / 可红冲 为真的按钮；
 *   · 不能红冲的**已记账**行不放假按钮，改在"操作"列写一行灰色原因
 *     （如"所属期间已结账"），让人知道为什么没有按钮，而不是以为页面坏了。
 */

const router = useRouter()

const 行 = ref<凭证行[]>([])
const 总数 = ref(0)
const 加载中 = ref(false)
const 错误 = ref('')
const 筛选展开 = ref(false)
const 查询 = reactive({
  page: 1,
  size: 50,
  keyword: '',
  from: '',
  to: '',
  状态: '' as '' | number
})

/** 展开行 → 分录（懒加载：一页 50 张凭证全预取等于白拉 500 行） */
const 分录缓存 = reactive<Record<number, 凭证分录行[]>>({})
const 分录加载中 = reactive<Record<number, boolean>>({})

const 业务类型名: Record<string, string> = {
  MANUAL: '手工凭证',
  SALE_DELIVER: '销售出库',
  SALE_RET: '销售退货',
  RECEIPT_CASH: '现金收款',
  RECEIPT_BANK: '银行收款',
  PAYMENT_BANK: '银行付款',
  PURCHASE_RECEIPT: '采购到货',
  EXPENSE_BANK: '费用报销',
  COMMISSION: '提成计提',
  PAYROLL_BANK: '工资发放',
  CLOSE_PL: '结转损益',
  COST_CLOSE: '成本结转',
  OPENING: '期初结转'
}

function 类型名(v: string) {
  return 业务类型名[v] || v || '—'
}

function 文本(v: any): string {
  if (v === null || v === undefined || v === '') return '—'
  const s = String(v)
  if (/^\d{4}-\d{2}-\d{2}T/.test(s)) return s.replace('T', ' ').slice(0, 19)
  return s
}

function 金额(v: any): string {
  const n = Number(v)
  if (!Number.isFinite(n)) return '—'
  return n.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/** 单据状态：0 草稿 / 2 已记账 / 9 已作废（凭证没有 1/3，别套单据那套色卡） */
function 状态标签(v: number): { 文本: string; 类型: 'info' | 'success' | 'danger' } {
  if (v === 0) return { 文本: '草稿', 类型: 'info' }
  if (v === 2) return { 文本: '已记账', 类型: 'success' }
  if (v === 9) return { 文本: '已作废', 类型: 'danger' }
  return { 文本: String(v), 类型: 'info' }
}

const 本页借 = computed(() => 行.value.reduce((a, r) => a + (Number(r.借方合计) || 0), 0))
const 本页贷 = computed(() => 行.value.reduce((a, r) => a + (Number(r.贷方合计) || 0), 0))
const 本页可红冲 = computed(() => 行.value.filter((r) => r.可红冲 === 1).length)
const 本页草稿 = computed(() => 行.value.filter((r) => r.单据状态 === 0).length)

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r: any = await api.finance.凭证列表({
      page: 查询.page,
      size: 查询.size,
      keyword: 查询.keyword || undefined,
      from: 查询.from || undefined,
      to: 查询.to || undefined,
      状态: 查询.状态 === '' ? undefined : 查询.状态
    })
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
    Object.keys(分录缓存).forEach((k) => delete 分录缓存[Number(k)])
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '凭证台账加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

async function 展开(row: 凭证行) {
  if (分录缓存[row.凭证编号] || 分录加载中[row.凭证编号]) return
  分录加载中[row.凭证编号] = true
  try {
    const r = await api.finance.凭证详情(row.凭证编号)
    分录缓存[row.凭证编号] = r?.分录 ?? []
  } catch {
    分录缓存[row.凭证编号] = []
  } finally {
    分录加载中[row.凭证编号] = false
  }
}

async function 过账(row: 凭证行) {
  await ElMessageBox.confirm(
    `确认过账凭证 ${row.凭证字号}？过账后金额与分录即锁定，要改只能红冲。`,
    '过账确认',
    { type: 'warning', confirmButtonText: '过账', cancelButtonText: '取消' }
  )
  await api.finance.凭证过账(row.凭证编号)
  ElMessage.success(`凭证 ${row.凭证字号} 已过账`)
  await 加载()
}

async function 红冲(row: 凭证行) {
  const { value } = await ElMessageBox.prompt(
    `凭证 ${row.凭证字号}（借 ${金额(row.借方合计)} / 贷 ${金额(row.贷方合计)}）\n` +
      '红冲会生成一张借贷对调的红字凭证，并把原凭证置为已作废 —— 原凭证不会被删除，也不改金额。',
    '红冲确认',
    {
      type: 'warning',
      confirmButtonText: '确认红冲',
      cancelButtonText: '取消',
      inputPlaceholder: '冲销原因（会写进红字凭证摘要）',
      inputValue: '账务更正',
      inputValidator: (v) => (v && v.trim().length > 0 ? true : '请填写冲销原因')
    }
  )
  const r: any = await api.finance.凭证红冲(row.凭证编号, value.trim())
  ElMessage.success(`已生成红字凭证 ${r?.新凭证字号 ?? ''}`)
  await 加载()
}

function 换页(p: number) {
  查询.page = p
  加载()
}
function 换页大小(s: number) {
  查询.size = s
  查询.page = 1
  加载()
}
function 查询现在() {
  查询.page = 1
  加载()
}
function 重置() {
  查询.keyword = ''
  查询.from = ''
  查询.to = ''
  查询.状态 = ''
  查询.page = 1
  加载()
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="凭证台账"
      icon="Tickets"
      sub="一张凭证一行 · 展开看分录 · 草稿可过账、已记账可红冲（红冲=另开红字凭证，原凭证留痕作废）"
    >
      <template #actions>
        <el-button @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
        <el-button type="primary" @click="router.push('/ledger/voucher/new')">
          <el-icon><EditPen /></el-icon> 录入凭证
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="凭证张数"
        icon="Tickets"
        tone="brand"
        :value="总数.toLocaleString('zh-CN')"
        unit="张"
        :sub="查询.keyword || 查询.from ? '已按筛选条件统计' : '全部记账凭证'"
      />
      <StatCard
        label="本页借方"
        icon="Money"
        tone="info"
        :value="金额(本页借)"
        sub="当前页已记账凭证借方合计"
      />
      <StatCard
        label="本页贷方"
        icon="Money"
        tone="info"
        :value="金额(本页贷)"
        sub="借贷相等是过账的前提"
      />
      <StatCard
        label="本页可红冲"
        icon="RefreshLeft"
        :tone="本页可红冲 ? 'warning' : 'success'"
        :value="本页可红冲"
        unit="张"
        :sub="`另有 ${本页草稿} 张草稿待过账`"
      />
    </div>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input v-model="查询.keyword" placeholder="凭证字号 / 摘要" clearable style="width: 220px" @keyup.enter="查询现在">
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <el-date-picker v-model="查询.from" type="date" value-format="YYYY-MM-DD" placeholder="起始日期" style="width: 140px" />
        <el-date-picker v-model="查询.to" type="date" value-format="YYYY-MM-DD" placeholder="结束日期" style="width: 140px" />
        <el-select v-model="查询.状态" placeholder="全部状态" clearable style="width: 130px">
          <el-option label="草稿" :value="0" />
          <el-option label="已记账" :value="2" />
          <el-option label="已作废" :value="9" />
        </el-select>
        <el-button type="primary" @click="查询现在">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>
        <span class="spacer" />
        <span class="hint">共 {{ 总数.toLocaleString('zh-CN') }} 张</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        empty-title="没有符合条件的凭证"
        empty-desc="演示库已结账 12 个期间；本期还没有凭证时，点右上角「录入凭证」建一张。"
        empty-icon="Tickets"
        :skeleton-rows="10"
        @retry="加载"
      >
        <div class="table-wrap table-scroll-hint">
          <el-table
            :data="行"
            size="small"
            stripe
            border
            row-key="凭证编号"
            max-height="calc(100vh - 420px)"
            @expand-change="(row: 凭证行) => 展开(row)"
          >
            <el-table-column type="expand">
              <template #default="{ row }">
                <div v-loading="分录加载中[row.凭证编号]" class="expand-box">
                  <table v-if="分录缓存[row.凭证编号]?.length" class="lines">
                    <thead>
                      <tr>
                        <th style="width: 52px">行</th>
                        <th>摘要</th>
                        <th style="width: 110px">科目编码</th>
                        <th style="width: 180px">科目名称</th>
                        <th style="width: 130px; text-align: right">借方金额</th>
                        <th style="width: 130px; text-align: right">贷方金额</th>
                        <th style="width: 150px">辅助核算</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="l in 分录缓存[row.凭证编号]" :key="l.行号">
                        <td class="center">{{ l.行号 }}</td>
                        <td>{{ l.摘要 || '—' }}</td>
                        <td class="num">{{ l.科目编码 }}</td>
                        <td>{{ l.科目名称 }}</td>
                        <td class="num right">{{ Number(l.借方金额) ? 金额(l.借方金额) : '' }}</td>
                        <td class="num right">{{ Number(l.贷方金额) ? 金额(l.贷方金额) : '' }}</td>
                        <td>
                          <span v-if="l.客户">客户：{{ l.客户 }}</span>
                          <span v-else-if="l.供应商">供应商：{{ l.供应商 }}</span>
                          <span v-else-if="l.商品">{{ l.商品 }}</span>
                          <span v-else class="flat">—</span>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                  <div v-else-if="!分录加载中[row.凭证编号]" class="flat">这张凭证没有分录</div>
                </div>
              </template>
            </el-table-column>

            <el-table-column prop="凭证字号" label="凭证字号" width="160" fixed>
              <template #default="{ row }">
                <span class="num">{{ row.凭证字号 }}</span>
                <el-tag v-if="row.是否冲销" size="small" type="warning" effect="plain" style="margin-left: 4px">红字</el-tag>
              </template>
            </el-table-column>

            <el-table-column prop="凭证日期" label="凭证日期" width="110">
              <template #default="{ row }">{{ String(row.凭证日期).slice(0, 10) }}</template>
            </el-table-column>

            <el-table-column prop="期间名称" label="所属期间" width="120">
              <template #default="{ row }">
                {{ row.期间名称 || '—' }}
                <el-tag v-if="row.期间状态 === 1" size="small" type="info" effect="plain" style="margin-left: 4px">已结账</el-tag>
              </template>
            </el-table-column>

            <el-table-column prop="摘要" label="摘要" min-width="240" show-overflow-tooltip />

            <el-table-column prop="借方合计" label="借方合计" width="130" align="right">
              <template #default="{ row }"><span class="num">{{ 金额(row.借方合计) }}</span></template>
            </el-table-column>

            <el-table-column prop="贷方合计" label="贷方合计" width="130" align="right">
              <template #default="{ row }"><span class="num">{{ 金额(row.贷方合计) }}</span></template>
            </el-table-column>

            <el-table-column prop="分录行数" label="分录" width="70" align="center" />

            <el-table-column prop="业务类型代码" label="来源" width="110">
              <template #default="{ row }">{{ 类型名(row.业务类型代码) }}</template>
            </el-table-column>

            <el-table-column prop="单据状态" label="状态" width="90" align="center">
              <template #default="{ row }">
                <el-tag :type="状态标签(row.单据状态).类型" size="small" effect="light">
                  {{ 状态标签(row.单据状态).文本 }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column prop="制单人" label="制单" width="90">
              <template #default="{ row }">{{ 文本(row.制单人) }}</template>
            </el-table-column>

            <el-table-column label="操作" width="150" fixed="right" align="center">
              <template #default="{ row }">
                <el-button v-if="row.可过账 === 1" link type="primary" @click="过账(row)">过账</el-button>
                <el-button v-else-if="row.可红冲 === 1" link type="danger" @click="红冲(row)">红冲</el-button>
                <el-tooltip v-else :content="row.红冲受阻原因 || '当前状态没有可执行的动作'" placement="left">
                  <span class="flat">{{ row.红冲受阻原因 ? '不可操作' : '—' }}</span>
                </el-tooltip>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <el-pagination
          class="mt-3"
          style="justify-content: flex-end"
          background
          layout="total, sizes, prev, pager, next"
          :total="总数"
          :current-page="查询.page"
          :page-size="查询.size"
          :page-sizes="[50, 100, 200]"
          @current-change="换页"
          @size-change="换页大小"
        />

        <div class="caliber-note">
          <b>口径</b>：本页只按<b>凭证</b>记账，借贷平衡由 <code>usp_财务_凭证过账</code> 在过账时复核；
          「红冲」调用 <code>usp_财务_红冲</code>，生成借贷对调的<b>红字凭证</b>并把原凭证置为<b>已作废</b>，
          原凭证金额与分录<b>永不改动</b>。所属期间<b>已结账</b>的凭证不可红冲 —— 那是库里的期间锁定
          （<code>trg_凭证_期间锁定</code>），要冲历史凭证请先在「系统 · 期间管理」<b>反月结</b>。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
.expand-box {
  padding: 8px 6px 4px 40px;
}
.lines {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--fs-12, 12px);
}
.lines th,
.lines td {
  border-bottom: 1px solid var(--c-border, #e5e7eb);
  padding: 5px 8px;
  text-align: left;
}
.lines th {
  color: var(--c-text-3, #6b7280);
  font-weight: 500;
  background: var(--c-fill-2, #fafafa);
}
.lines .right {
  text-align: right;
}
.lines .center {
  text-align: center;
}
@media (min-width: 768px) {
  .filter-collapsible {
    display: flex;
  }
}
</style>

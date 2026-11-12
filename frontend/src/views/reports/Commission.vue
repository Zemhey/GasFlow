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
const 汇总 = ref<any[]>([])
const 明细 = ref<any[]>([])
const 加载中 = ref(false)
const 错误 = ref('')
/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)
const 查询 = reactive<any>({ 日期: [] as string[], 员工编号: null })

function 本月() {
  const d = new Date()
  const fmt = (x: Date) =>
    `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`
  查询.日期 = [fmt(new Date(d.getFullYear(), d.getMonth(), 1)), fmt(d)]
}

/** 本期的起止（用于 KPI 副标题与导出文件名），与接口参数同一来源 */
const 起始 = computed(() => String(查询.日期?.[0] ?? ''))
const 截止 = computed(() => String(查询.日期?.[1] ?? ''))

const 合计 = computed(() => ({
  单数: 汇总.value.reduce((s, r) => s + Number(r.单数 ?? 0), 0),
  数量: 汇总.value.reduce((s, r) => s + Number(r.数量 ?? 0), 0),
  提成: 汇总.value.reduce((s, r) => s + Number(r.提成 ?? 0), 0)
}))

/** 明细合计：与汇总的「提成」互相对照（两者不一致说明有明细没归到送瓶员） */
const 明细合计 = computed(() => ({
  行数: 明细.value.length,
  数量: 明细.value.reduce((s, r) => s + Number(r.数量 ?? 0), 0),
  提成: 明细.value.reduce((s, r) => s + Number(r.提成 ?? 0), 0),
  无方案行数: 明细.value.filter((r) => Number(r.单瓶提成 ?? 0) === 0 && Number(r.数量 ?? 0) > 0).length
}))

/** 人均提成（按有提成的送瓶员算）：给管理者一个"人效"参考 */
const 人均提成 = computed(() => {
  const 人数 = 汇总.value.filter((r) => Number(r.提成 ?? 0) > 0).length
  if (!人数) return 0
  return 合计.value.提成 / 人数
})

/** 单瓶平均提成 = 总提成 ÷ 总数量（数量为 0 时给 0） */
const 单瓶均提成 = computed(() => {
  const 量 = 合计.value.数量
  if (!量) return 0
  return 合计.value.提成 / 量
})

async function 查询数据() {
  加载中.value = true
  错误.value = ''
  try {
    const r = await api.reports.计提({
      from: 查询.日期?.[0],
      to: 查询.日期?.[1],
      员工编号: 查询.员工编号 ?? undefined
    })
    汇总.value = r?.汇总 ?? []
    明细.value = r?.明细 ?? []
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '计提数据加载失败，请稍后重试'
    汇总.value = []
    明细.value = []
  } finally {
    加载中.value = false
  }
}

function 金额(v: any): string {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function 日期(v: any): string {
  const s = String(v ?? '')
  return s ? s.slice(0, 10) : '—'
}

function 重置() {
  查询.员工编号 = null
  本月()
  查询数据()
}

const 有筛选 = computed(() => !!查询.员工编号)

const 空标题 = computed(() =>
  有筛选.value ? '该送瓶员本期没有送气记录' : '本期没有送气记录，计提为 0'
)
const 空说明 = computed(() =>
  有筛选.value
    ? '这位送瓶员在所选期间内没有已过账的送气单，或送气单没带计提方案。换个人、或放宽日期再试。'
    : '提成 = Σ(送气单明细实瓶数量 × 该客户计提方案里该规格的提成额)；没有已过账送气单时自然为 0。请先确认本期有送气单过账。'
)

/** 导出当前视图（汇总 + 明细两段，纯前端 CSV） */
function 导出() {
  if (!汇总.value.length && !明细.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 段: string[] = []
  段.push('计提汇总')
  段.push(['送瓶员', '单数', '配送数量', '应计提成'].join(','))
  for (const r of 汇总.value) {
    段.push(
      [r.送瓶员, Number(r.单数 ?? 0), Number(r.数量 ?? 0), Number(r.提成 ?? 0).toFixed(2)]
        .map((v) => `"${String(v ?? '')}"`)
        .join(',')
    )
  }
  段.push(
    ['合计', 合计.value.单数, 合计.value.数量, 合计.value.提成.toFixed(2)]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  段.push('')
  段.push('计提明细')
  段.push(['日期', '单据号码', '客户', '送瓶员', '规格', '数量', '单瓶提成', '提成'].join(','))
  for (const r of 明细.value) {
    段.push(
      [
        日期(r.日期),
        r.单据号码,
        r.客户,
        r.送瓶员,
        r.规格,
        Number(r.数量 ?? 0),
        Number(r.单瓶提成 ?? 0).toFixed(2),
        Number(r.提成 ?? 0).toFixed(2)
      ]
        .map((v) => `"${String(v ?? '')}"`)
        .join(',')
    )
  }
  段.push(
    [
      '合计',
      '',
      '',
      '',
      '',
      明细合计.value.数量,
      '',
      明细合计.value.提成.toFixed(2)
    ]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + 段.join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `计提汇总_${起始.value || '全部'}_${截止.value || '全部'}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success('已导出汇总与明细')
}

onMounted(async () => {
  await lookup.加载()
  本月()
  await 查询数据()
})
</script>

<template>
  <div>
    <PageHead
      title="计提汇总"
      icon="PriceTag"
      sub="送瓶员提成 = Σ(实瓶数量 × 计提方案中该规格的提成额) · 只统计已过账送气单"
    >
      <template #actions>
        <el-button :disabled="!汇总.length && !明细.length" @click="导出">
          <el-icon><Download /></el-icon> 导出
        </el-button>
        <el-button type="primary" :loading="加载中" @click="查询数据">
          <el-icon><Refresh /></el-icon> 查询
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="应计提成"
        icon="Money"
        tone="warning"
        :value="`¥${金额(合计.提成)}`"
        :sub="`期间 <b>${起始 || '—'}</b> ~ <b>${截止 || '—'}</b>`"
      />
      <StatCard
        label="配送数量"
        icon="Box"
        :value="合计.数量.toLocaleString('zh-CN')"
        unit="瓶"
        :sub="`<b>${合计.单数}</b> 张送气单 · 单瓶均提成 <b>¥${金额(单瓶均提成)}</b>`"
      />
      <StatCard
        label="参与送瓶员"
        icon="User"
        :value="汇总.length.toLocaleString('zh-CN')"
        unit="人"
        :sub="`人均提成 <b>¥${金额(人均提成)}</b>（按有提成者计）`"
      />
      <StatCard
        label="明细行数"
        icon="Files"
        tone="info"
        :value="明细合计.行数.toLocaleString('zh-CN')"
        unit="行"
        :sub="`明细提成合计 <b>¥${金额(明细合计.提成)}</b>（应与上表一致）`"
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
        <el-date-picker
          v-model="查询.日期"
          type="daterange"
          value-format="YYYY-MM-DD"
          start-placeholder="开始"
          end-placeholder="结束"
          style="width: 260px"
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
        <span class="hint">提成为 0 的明细表示该单未挂计提方案或该规格无提成额</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!汇总.length && !明细.length"
        :empty-title="空标题"
        :empty-desc="空说明"
        empty-icon="PriceTag"
        @retry="查询数据"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置">
            <el-icon><RefreshLeft /></el-icon> 清除筛选
          </el-button>
          <el-button v-else @click="查询数据">
            <el-icon><Refresh /></el-icon> 刷新
          </el-button>
        </template>

        <div class="section-title mb-2">送瓶员汇总</div>
        <div class="table-wrap table-scroll-hint">
          <el-table :data="汇总" size="small" stripe max-height="420">
            <el-table-column prop="送瓶员" label="送瓶员" min-width="140" show-overflow-tooltip>
              <template #default="{ row }">{{ row.送瓶员 || '(未指定)' }}</template>
            </el-table-column>
            <el-table-column label="单数" width="110" align="right">
              <template #default="{ row }">
                <MoneyText :value="row.单数" :digits="0" />
              </template>
            </el-table-column>
            <el-table-column label="配送数量" width="130" align="right">
              <template #default="{ row }">
                <MoneyText :value="row.数量" :digits="0" />
              </template>
            </el-table-column>
            <el-table-column label="应计提成" width="160" align="right">
              <template #default="{ row }"><MoneyText :value="row.提成" prefix /></template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>汇总合计（{{ 汇总.length }} 人）</span>
          <span>单数 <b>{{ 合计.单数 }}</b></span>
          <span>配送数量 <b>{{ 合计.数量.toLocaleString('zh-CN') }}</b></span>
          <span class="sum-bar__danger">应计提成 <b>¥{{ 金额(合计.提成) }}</b></span>
        </div>

        <div class="section-title mt-4 mb-2">计提明细</div>
        <div class="table-wrap table-scroll-hint">
          <el-table :data="明细" size="small" stripe max-height="420">
            <el-table-column label="日期" width="120">
              <template #default="{ row }">
                <span class="num">{{ 日期(row.日期) }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="单据号码" label="单据号码" width="170" show-overflow-tooltip />
            <el-table-column prop="客户" label="客户" min-width="150" show-overflow-tooltip>
              <template #default="{ row }">{{ row.客户 || '—' }}</template>
            </el-table-column>
            <el-table-column prop="送瓶员" label="送瓶员" width="110">
              <template #default="{ row }">{{ row.送瓶员 || '—' }}</template>
            </el-table-column>
            <el-table-column prop="规格" label="规格" width="140" show-overflow-tooltip>
              <template #default="{ row }">{{ row.规格 || '—' }}</template>
            </el-table-column>
            <el-table-column label="数量" width="100" align="right">
              <template #default="{ row }">
                <MoneyText :value="row.数量" :digits="0" />
              </template>
            </el-table-column>
            <el-table-column label="单瓶提成" width="130" align="right">
              <template #default="{ row }">
                <span :class="Number(row.单瓶提成 ?? 0) ? 'num' : 'num flat'">
                  <MoneyText :value="row.单瓶提成" prefix />
                </span>
              </template>
            </el-table-column>
            <el-table-column label="提成" width="130" align="right">
              <template #default="{ row }">
                <span :class="Number(row.提成 ?? 0) ? '' : 'flat'">
                  <MoneyText :value="row.提成" prefix />
                </span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>明细合计（{{ 明细合计.行数 }} 行）</span>
          <span>配送数量 <b>{{ 明细合计.数量.toLocaleString('zh-CN') }}</b></span>
          <span class="sum-bar__danger">提成 <b>¥{{ 金额(明细合计.提成) }}</b></span>
          <span :class="明细合计.无方案行数 ? 'text-warning' : ''">
            未匹配到计件提成（单瓶提成 = 0 且有数量）<b>{{ 明细合计.无方案行数 }}</b> 行
          </span>
        </div>

        <div class="caliber-note">
          <b>口径</b>：汇总与明细都只取所选期间内<b>已过账的送气单</b>。
          <b>提成 = Σ(送气单明细.实瓶数量 × 计提方案明细.提成金额)</b>，计提方案按送气单上的「计提方案编号 + 钢瓶规格编号」关联；
          送气单未挂计提方案、或该方案里没有该规格时，单瓶提成为 0，明细仍会列出但提成为 0（上面的「未匹配」行数即此）。
          <b>单数 = 去重后的送气单张数</b>（COUNT DISTINCT 送气单编号），所以同一送瓶员多行的明细相加会大于汇总单数。
          顶部「参与送瓶员」按汇总行数计（含未指定送瓶员的行）；「人均提成」只按提成大于 0 的送瓶员平均。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

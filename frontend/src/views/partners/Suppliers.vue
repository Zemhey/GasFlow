<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'
import MoneyText from '@/components/MoneyText.vue'

/**
 * 供应商应付：契约只给了 GET /api/partners/suppliers（无分页参数），
 * 返回结构与客户欠款同构，字段不确定时按"含关键词的字段"兜底取值。
 */
const 行 = ref<Record<string, any>[]>([])
const 加载中 = ref(false)
const 错误 = ref('')
const 关键词 = ref('')

/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)

function 取(row: Record<string, any>, ...keys: string[]) {
  for (const k of keys) {
    if (row[k] !== undefined && row[k] !== null && row[k] !== '') return row[k]
  }
  return ''
}

const 名称键 = ['供气商简称', '简称', '供气商', '客户简称', '名称']
const 应付键 = ['应付金额', '应付款', '未付金额', '金额']
const 预付键 = ['预付金额', '已付金额', '预付']
const 日期键 = ['最后交易日期', '最后交易', '最后结账日期']
const 编码键 = ['供气商编码', '客户编码', '编码']
const 电话键 = ['联系电话', '电话']
const 额度键 = ['信用额度', '账期天数']

/** 取值并转成数值，空值/非数字一律按 0（求和与阈值判断都用它） */
function 数值(row: Record<string, any>, keys: string[]): number {
  const n = Number(取(row, ...keys) || 0)
  return Number.isFinite(n) ? n : 0
}

const 过滤后 = computed(() => {
  const k = 关键词.value.trim()
  if (!k) return 行.value
  return 行.value.filter((r) =>
    [名称键, 编码键, 电话键].some((ks) => String(取(r, ...ks)).includes(k))
  )
})

const 应付合计 = computed(() => 过滤后.value.reduce((s, r) => s + 数值(r, 应付键), 0))
const 预付合计 = computed(() => 过滤后.value.reduce((s, r) => s + 数值(r, 预付键), 0))
/** 有应付余额的家数（应付 > 0 表示这家还没付清） */
const 有应付家数 = computed(() => 过滤后.value.filter((r) => 数值(r, 应付键) > 0).length)
/** 最大单家应付：催付优先级排序用 */
const 最大应付 = computed(() =>
  过滤后.value.reduce((m, r) => Math.max(m, 数值(r, 应付键)), 0)
)
/** 最大应付对应的供气商名（用于指标卡副标题给出"谁是最大的"） */
const 最大应付名 = computed(() => {
  let 名 = ''
  let 值 = -1
  for (const r of 过滤后.value) {
    const v = 数值(r, 应付键)
    if (v > 值) {
      值 = v
      名 = String(取(r, ...名称键) || '')
    }
  }
  return 值 > 0 ? 名 : ''
})

function 金额(v: any): string {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/** 日期只取前 10 位；空值显示 —，避免渲染出 undefined */
function 日期(v: any): string {
  const s = String(v ?? '')
  return s ? s.slice(0, 10) : '—'
}

/** 预付/已付 > 应付 → 这家是"预付余额"（我方欠对方为负），用绿色标记 */
function 有预付余额(row: Record<string, any>) {
  return 数值(row, 预付键) > 0 && 数值(row, 应付键) === 0
}

const 有筛选 = computed(() => !!关键词.value.trim())

const 空标题 = computed(() => (有筛选.value ? '没有符合条件的供气商' : '还没有供气商应付数据'))
const 空说明 = computed(() =>
  有筛选.value
    ? '换个供气商名称 / 编码 / 电话再试，或清空关键词看全部。'
    : '应付以已过账应付单为准：先录入并过账一张进气单（或付款单），供气商才会出现在这里。'
)

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r = await api.partners.供应商()
    行.value = Array.isArray(r) ? r : []
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '供气商应付加载失败，请稍后重试'
    行.value = []
  } finally {
    加载中.value = false
  }
}

function 重置() {
  关键词.value = ''
}

/** 导出当前筛选结果为 CSV（纯前端，不改后端契约） */
function 导出() {
  if (!过滤后.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = ['供气商编码', '供气商', '联系电话', '应付金额', '预付/已付', '账期/额度', '最后交易']
  const 体 = 过滤后.value.map((r) =>
    [
      String(取(r, ...编码键)),
      String(取(r, ...名称键)),
      String(取(r, ...电话键)),
      数值(r, 应付键).toFixed(2),
      数值(r, 预付键).toFixed(2),
      String(取(r, ...额度键)),
      日期(取(r, ...日期键)) === '—' ? '' : 日期(取(r, ...日期键))
    ]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `供气商应付_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${过滤后.value.length} 行`)
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="供应商应付"
      icon="OfficeBuilding"
      sub="按供气商汇总应付余额与预付 / 已付 · 应付以已过账应付单为准"
    >
      <template #actions>
        <el-button :disabled="!过滤后.length" @click="导出">
          <el-icon><Download /></el-icon> 导出
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <!-- KPI：当前筛选结果的口径（无筛选时即全部） -->
    <div class="stat-grid">
      <StatCard
        label="应付合计"
        icon="Money"
        tone="warning"
        :value="`¥${金额(应付合计)}`"
        :sub="`共 <b>${过滤后.length}</b> 家供气商`"
      />
      <StatCard
        label="未付清家数"
        icon="Timer"
        :tone="有应付家数 ? 'danger' : 'success'"
        :value="有应付家数.toLocaleString('zh-CN')"
        unit="家"
        sub="应付余额大于 0，仍需付款"
      />
      <StatCard
        label="最大单家应付"
        icon="Warning"
        tone="info"
        :value="`¥${金额(最大应付)}`"
        :sub="最大应付名 ? `金额最大：<b>${最大应付名}</b>` : '当前没有未付余额'"
      />
      <StatCard
        label="预付 / 已付合计"
        icon="Coin"
        tone="success"
        :value="`¥${金额(预付合计)}`"
        sub="已付金额与预付余额之和"
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
          v-model="关键词"
          placeholder="供气商 / 编码 / 电话"
          clearable
          style="width: 220px"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 清空
        </el-button>

        <span class="spacer" />
        <el-tag type="warning" effect="light" size="small">
          应付合计 ¥{{ 金额(应付合计) }}
        </el-tag>
        <el-tag v-if="预付合计" type="success" effect="light" size="small">
          预付合计 ¥{{ 金额(预付合计) }}
        </el-tag>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!过滤后.length"
        :empty-title="空标题"
        :empty-desc="空说明"
        empty-icon="OfficeBuilding"
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
          <span class="legend__item"><i class="legend__swatch swatch-due" />应付 &gt; 0 尚未付清</span>
          <span class="legend__item"><i class="legend__swatch swatch-paid" />应付 = 0 已结清</span>
          <span class="legend__item"><i class="legend__swatch swatch-none" />无账期 / 额度数据</span>
        </div>

        <div class="table-wrap table-scroll-hint">
          <el-table :data="过滤后" size="small" stripe max-height="calc(100vh - 320px)">
            <el-table-column label="供气商编码" width="130" show-overflow-tooltip>
              <template #default="{ row }">{{ 取(row, ...编码键) || '—' }}</template>
            </el-table-column>
            <el-table-column label="供气商" min-width="180" show-overflow-tooltip>
              <template #default="{ row }">{{ 取(row, ...名称键) || '—' }}</template>
            </el-table-column>
            <el-table-column label="联系电话" width="140">
              <template #default="{ row }">
                <span class="num">{{ 取(row, ...电话键) || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="应付金额" width="150" align="right">
              <template #default="{ row }">
                <span :class="数值(row, 应付键) > 0 ? 'down' : 'flat'">
                  <MoneyText :value="数值(row, 应付键)" prefix />
                </span>
              </template>
            </el-table-column>
            <el-table-column label="预付 / 已付" width="150" align="right">
              <template #default="{ row }">
                <span :class="有预付余额(row) ? 'up' : ''">
                  <MoneyText :value="数值(row, 预付键)" prefix />
                </span>
              </template>
            </el-table-column>
            <el-table-column label="账期 / 额度" width="130" align="right">
              <template #default="{ row }">
                <span class="num">{{ 取(row, ...额度键) || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="最后交易" width="130">
              <template #default="{ row }">
                <span class="num">{{ 日期(取(row, ...日期键)) }}</span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>共 {{ 过滤后.length }} 家供气商</span>
          <span class="sum-bar__danger">应付合计 <b>¥{{ 金额(应付合计) }}</b></span>
          <span>预付 / 已付 <b>¥{{ 金额(预付合计) }}</b></span>
          <span>未付清 <b>{{ 有应付家数 }}</b> 家</span>
        </div>

        <div class="caliber-note">
          <b>口径</b>：应付金额取<b>已过账应付单</b>的未付余额（应付单 方向 +1 计应付、方向 −1 计冲减，再减已付金额）；
          「预付 / 已付」取已付金额与预付余额。金额字段按
          <b>应付金额 / 应付款 / 未付金额 / 金额</b> 顺序兜底取值（接口无分页参数，一次返回全部供气商）。
          顶部指标卡与合计条统计的是<b>当前筛选后的结果</b>；「最大单家应付」为该结果里应付金额最大的那一家。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 图例色块：应付未清（红）/ 已结清（绿）/ 无数据（灰） */
.legend__swatch.swatch-due {
  background: var(--c-danger-500);
}

.legend__swatch.swatch-paid {
  background: var(--c-success-500);
}

.legend__swatch.swatch-none {
  background: var(--c-ink-200);
}
</style>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import api from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 视图台账（通用表格页）—— 五个业务域共用同一个组件，差别只在配置。
 *
 * props.view  主视图（一张 `vw_*`）
 * props.列    需要强调显示/合计的数值列名（只影响卡片与合计，不影响表格渲染）
 *
 * 为什么不给每个域写一页：v3 的域差异全在**视图**里，页面只要换个 view 名就是另一个台账。
 * 每域手写一页 = 五份几乎相同的模板，改一处要改五处；而且一旦视图加列，五页都要跟着改。
 */

const props = withDefaults(
  defineProps<{
    view: string
    title: string
    副标题?: string
    /** 卡片里要重点展示的数值列（视图里必须真实存在，否则那一张卡显示 —） */
    重点列?: string[]
    /** 状态列名（用于按状态分级着色） */
    状态列?: string
    /** 任务列名（如 任务编号）：有值时行上加「从任务看明细」跳转，不重写报表 */
    任务列?: string
  }>(),
  { 重点列: () => [], 副标题: '', 状态列: '', 任务列: '' }
)

const router = useRouter()

function 看任务(row: Record<string, any>) {
  const v = row[props.任务列]
  if (v) router.push(`/dispatch/tasks/${v}`)
}

interface 列元 {
  列: string
  型: string
}

const 行 = ref<Record<string, any>[]>([])
const 列 = ref<列元[]>([])
const 总数 = ref(0)
const 日期列 = ref('')
const 加载中 = ref(false)
const 错误 = ref('')
const 筛选展开 = ref(false)
const 查询 = reactive({ page: 1, size: 50, keyword: '', from: '', to: '', orderBy: '', desc: true })

function 是数值型(型: string) {
  return ['int', 'bigint', 'smallint', 'tinyint', 'decimal', 'numeric', 'money', 'smallmoney', 'float', 'real'].includes(型)
}

function 文本(v: any): string {
  if (v === null || v === undefined || v === '') return '—'
  const s = String(v)
  if (/^\d{4}-\d{2}-\d{2}T/.test(s)) return s.replace('T', ' ').slice(0, 19)
  return s
}
function 数值文本(v: any): string {
  const n = Number(v)
  if (!Number.isFinite(n)) return 文本(v)
  return n.toLocaleString('zh-CN', { maximumFractionDigits: 2 })
}

/** 本页合计：重点列 + 其余数值列（同一套逻辑，与通用报表页保持一致） */
const 合计 = computed(() => {
  const 结果: Record<string, number> = {}
  for (const c of 列.value) {
    if (!是数值型(c.型)) continue
    结果[c.列] = 行.value.reduce((a, r) => {
      const n = Number(r[c.列])
      return a + (Number.isFinite(n) ? n : 0)
    }, 0)
  }
  return 结果
})

/** 状态着色：库里单据状态 0 草稿 / 1 已提交 / 2 已审核 / 3 已过账 / 9 已冲销 */
function 状态标签(row: Record<string, any>): { 文本: string; 类型: '' | 'success' | 'warning' | 'danger' | 'info' | 'primary' } | null {
  if (!props.状态列) return null
  const v = row[props.状态列]
  if (v === null || v === undefined || v === '') return null
  const n = Number(v)
  if (!Number.isFinite(n)) return { 文本: String(v), 类型: 'info' }
  const 表: Record<number, { 文本: string; 类型: 'success' | 'warning' | 'danger' | 'info' | 'primary' }> = {
    0: { 文本: '草稿', 类型: 'info' },
    1: { 文本: '已提交', 类型: 'warning' },
    2: { 文本: '已审核', 类型: 'primary' },
    3: { 文本: '已过账', 类型: 'success' },
    9: { 文本: '已冲销', 类型: 'danger' }
  }
  return 表[n] ?? { 文本: String(n), 类型: 'info' }
}

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r: any = await api.views.数据(props.view, {
      page: 查询.page,
      size: 查询.size,
      keyword: 查询.keyword || undefined,
      from: 查询.from || undefined,
      to: 查询.to || undefined,
      orderBy: 查询.orderBy || undefined,
      desc: 查询.desc
    })
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
    列.value = r?.列 ?? []
    日期列.value = r?.日期列 ?? ''
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '台账加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

function 排序(c: 列元) {
  if (查询.orderBy === c.列) 查询.desc = !查询.desc
  else { 查询.orderBy = c.列; 查询.desc = true }
  查询.page = 1
  加载()
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
function 重置() {
  查询.keyword = ''
  查询.from = ''
  查询.to = ''
  查询.orderBy = ''
  查询.page = 1
  加载()
}

function 导出() {
  if (!行.value.length) {
    ElMessage.warning('当前页没有可导出的行')
    return
  }
  const 头 = 列.value.map((c) => `"${c.列}"`).join(',')
  const 体 = 行.value.map((r) => 列.value.map((c) => `"${String(r[c.列] ?? '').replace(/"/g, '""')}"`).join(','))
  const csv = '\uFEFF' + [头, ...体].join('\r\n')
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = `${props.title}_第${查询.page}页.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出当前页 ${行.value.length} 行`)
}

const 重点值 = computed(() => props.重点列.map((名) => ({ 名, 值: 合计.value[名] })))

onMounted(加载)
</script>

<template>
  <div>
    <PageHead :title="title" icon="Tickets" :sub="副标题 || `数据源 ${view} · 列与排序按视图的真实结构渲染 · 口径在库里`">
      <template #actions>
        <el-button :disabled="!行.length" @click="导出">
          <el-icon><Download /></el-icon> 导出本页
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard label="总行数" icon="Document" tone="brand" :value="总数.toLocaleString('zh-CN')" unit="行" :sub="查询.keyword || 查询.from ? '已按筛选条件统计' : '全部记录'" />
      <StatCard
        v-for="x in 重点值"
        :key="x.名"
        :label="x.名"
        icon="Money"
        tone="info"
        :value="数值文本(x.值)"
        :sub="`本页 ${x.名} 合计`"
      />
      <StatCard v-if="!重点值.length" label="本页行数" icon="Tickets" :value="行.length.toLocaleString('zh-CN')" unit="行" :sub="`每页 ${查询.size} 行`" />
    </div>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input v-model="查询.keyword" placeholder="全文本列模糊搜索" clearable style="width: 220px">
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <template v-if="日期列">
          <el-date-picker v-model="查询.from" type="date" value-format="YYYY-MM-DD" placeholder="起始" style="width: 140px" />
          <el-date-picker v-model="查询.to" type="date" value-format="YYYY-MM-DD" placeholder="结束" style="width: 140px" />
        </template>
        <el-button type="primary" @click="查询.page = 1; 加载()">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>
        <span class="spacer" />
        <span class="hint">{{ 日期列 ? `日期列：${日期列}` : '无日期列' }} · 共 {{ 总数.toLocaleString('zh-CN') }} 行</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        empty-title="没有符合条件的记录"
        empty-desc="该视图在当前条件下没有数据；可清空筛选、放宽日期区间。"
        empty-icon="Tickets"
        :skeleton-rows="10"
        @retry="加载"
      >
        <div class="table-wrap table-scroll-hint">
          <el-table :data="行" size="small" stripe border max-height="calc(100vh - 400px)">
            <el-table-column
              v-for="c in 列"
              :key="c.列"
              :label="c.列"
              :min-width="是数值型(c.型) ? 120 : 150"
              :align="是数值型(c.型) ? 'right' : 'left'"
              show-overflow-tooltip
              sortable
              @sort-change="() => 排序(c)"
            >
              <template #default="{ row }">
                <el-tag v-if="状态列 && c.列 === 状态列 && 状态标签(row)" :type="状态标签(row)!.类型" size="small" effect="light">
                  {{ 状态标签(row)!.文本 }}
                </el-tag>
                <span v-else :class="是数值型(c.型) ? 'num' : ''">
                  {{ 是数值型(c.型) ? 数值文本(row[c.列]) : 文本(row[c.列]) }}
                </span>
              </template>
            </el-table-column>
            <el-table-column v-if="任务列" label="关联" width="132" fixed="right">
              <template #default="{ row }">
                <el-button v-if="row[任务列]" link type="primary" @click="看任务(row)">
                  从任务看明细
                </el-button>
                <span v-else class="text-muted">—</span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div v-if="重点值.length" class="sum-bar">
          <span>本页合计</span>
          <span v-for="x in 重点值" :key="x.名">{{ x.名 }} <b>{{ 数值文本(x.值) }}</b></span>
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
          <b>口径</b>：本页直接读视图 <code>{{ view }}</code>，<b>不做二次聚合</b>；
          合计只统计<b>当前页</b>的数值列。排序在服务端完成，点表头即切换升降序。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
@media (min-width: 768px) {
  .filter-collapsible {
    display: flex;
  }
}
</style>

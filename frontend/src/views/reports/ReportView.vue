<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import api from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 通用报表页：GET /api/views/{view}?page&size&keyword&from&to&orderBy&desc
 *
 * 一张页面服务 120 张视图：**列不写死**，由后端返回的 `列[{列, 型}]` 现场渲染；
 * 排序点表头即可（服务端排序）；日期列与文本列也由后端告诉我们，所以筛选框对任何视图都成立。
 *
 * 为什么这样设计：v3 的口径在视图里，视图有 50 多列的（如 vw_凭证分录）也有十来列的。
 * 前端写死列 = 每加一张视图都要改代码；按元数据渲染 = 加视图零改动。
 */

interface 列元 {
  列: string
  型: string
}

const route = useRoute()
const router = useRouter()

const 视图 = computed(() => decodeURIComponent(String(route.params.view ?? '')))
const 行 = ref<Record<string, any>[]>([])
const 列 = ref<列元[]>([])
const 总数 = ref(0)
/** 后端告诉我们哪列是"日期列"：日期区间筛选就落在它上面 */
const 日期列 = ref('')
const 加载中 = ref(false)
const 错误 = ref('')
const 筛选展开 = ref(false)
const 查询 = reactive({ page: 1, size: 50, keyword: '', from: '', to: '', orderBy: '', desc: true })

/** 金额/数量列：右对齐 + 千分位；不靠列名猜，靠 SQL 类型判断，避免把编号当金额 */
function 是数值型(型: string) {
  return ['int', 'bigint', 'smallint', 'tinyint', 'decimal', 'numeric', 'money', 'smallmoney', 'float', 'real'].includes(型)
}
function 是日期型(型: string) {
  return ['date', 'datetime', 'datetime2', 'smalldatetime', 'datetimeoffset'].includes(型)
}

function 文本(v: any): string {
  if (v === null || v === undefined || v === '') return '—'
  const s = String(v)
  if (/^\d{4}-\d{2}-\d{2}T/.test(s)) return s.replace('T', ' ').slice(0, 19)
  if (/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}/.test(s)) return s.slice(0, 19)
  return s
}
function 数值文本(v: any): string {
  const n = Number(v)
  if (!Number.isFinite(n)) return 文本(v)
  return n.toLocaleString('zh-CN', { maximumFractionDigits: 4 })
}

/** 数字列合计：把"这一页的规模"摆出来（只对数值列求和，日期/文本不参与） */
const 合计 = computed(() => {
  const 结果: Record<string, number> = {}
  for (const c of 列.value) {
    if (!是数值型(c.型)) continue
    const s = 行.value.reduce((a, r) => {
      const n = Number(r[c.列])
      return a + (Number.isFinite(n) ? n : 0)
    }, 0)
    结果[c.列] = s
  }
  return 结果
})
const 数值列数 = computed(() => 列.value.filter((c) => 是数值型(c.型)).length)

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r: any = await api.views.数据(视图.value, {
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
    错误.value = e?.response?.data?.message ?? '报表加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

/** 点表头排序：同一列再点一次切升降序 */
function 排序(c: 列元) {
  if (查询.orderBy === c.列) 查询.desc = !查询.desc
  else {
    查询.orderBy = c.列
    查询.desc = true
  }
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
  查询.desc = true
  查询.page = 1
  加载()
}

function 导出() {
  if (!行.value.length) {
    ElMessage.warning('当前页没有可导出的行')
    return
  }
  const 头 = 列.value.map((c) => `"${c.列.replace(/"/g, '""')}"`).join(',')
  const 体 = 行.value.map((r) =>
    列.value.map((c) => `"${String(r[c.列] ?? '').replace(/"/g, '""')}"`).join(',')
  )
  const csv = '\uFEFF' + [头, ...体].join('\r\n')
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = `${视图.value}_第${查询.page}页.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出当前页 ${行.value.length} 行`)
}

/** 视图名 → 中文标题（去掉 vw_ 前缀即可，视图名本身就是中文） */
const 标题 = computed(() => 视图.value.replace(/^vw_/, '') || '报表')

onMounted(加载)
watch(视图, () => {
  查询.page = 1
  查询.keyword = ''
  查询.orderBy = ''
  加载()
})
</script>

<template>
  <div>
    <PageHead :title="标题" icon="Document" :sub="`数据源：数据库视图 ${视图} · 列与排序按视图的真实结构自动渲染 · 口径在库里，页面不做二次计算`">
      <template #actions>
        <el-button @click="router.push('/reports/center')">
          <el-icon><Back /></el-icon> 返回目录
        </el-button>
        <el-button :disabled="!行.length" @click="导出">
          <el-icon><Download /></el-icon> 导出本页
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard label="总行数" icon="Document" tone="brand" :value="总数.toLocaleString('zh-CN')" unit="行" :sub="查询.keyword || 查询.from ? '已按筛选条件统计' : '未筛选的全部行'" />
      <StatCard label="列数" icon="Grid" tone="info" :value="列.length.toLocaleString('zh-CN')" unit="列" :sub="`其中 <b>${数值列数}</b> 个数值列可参与合计`" />
      <StatCard label="本页行数" icon="Tickets" :value="行.length.toLocaleString('zh-CN')" unit="行" :sub="`每页 ${查询.size} 行`" />
      <StatCard label="日期筛选列" icon="Calendar" tone="info" :value="日期列 || '—'" :sub="日期列 ? '区间筛选作用在这一列' : '该视图没有日期列，不做区间筛选'" />
    </div>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input v-model="查询.keyword" placeholder="全文本列模糊搜索" clearable style="width: 220px">
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <el-date-picker
          v-if="日期列"
          v-model="查询.from"
          type="date"
          value-format="YYYY-MM-DD"
          placeholder="起始日期"
          style="width: 150px"
        />
        <el-date-picker
          v-if="日期列"
          v-model="查询.to"
          type="date"
          value-format="YYYY-MM-DD"
          placeholder="结束日期"
          style="width: 150px"
        />
        <el-button type="primary" @click="查询.page = 1; 加载()">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>
        <span class="spacer" />
        <span class="hint">{{ 日期列 ? `日期筛选列：${日期列}` : '无日期列' }} · 共 {{ 总数.toLocaleString('zh-CN') }} 行</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        empty-title="没有符合条件的行"
        empty-desc="该视图在当前条件下没有数据；可清空筛选、放宽日期区间或换一张报表。"
        empty-icon="Document"
        :skeleton-rows="10"
        @retry="加载"
      >
        <div class="table-wrap table-scroll-hint">
          <el-table :data="行" size="small" stripe max-height="calc(100vh - 400px)" border>
            <el-table-column
              v-for="c in 列"
              :key="c.列"
              :label="c.列"
              :min-width="是数值型(c.型) ? 120 : 是日期型(c.型) ? 160 : 150"
              :align="是数值型(c.型) ? 'right' : 'left'"
              show-overflow-tooltip
              sortable
              @sort-change="() => 排序(c)"
            >
              <template #default="{ row }">
                <span :class="是数值型(c.型) ? 'num' : ''">
                  {{ 是数值型(c.型) ? 数值文本(row[c.列]) : 文本(row[c.列]) }}
                </span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <!-- 本页合计：只对数值列求和，并在列名后面直接标出来 -->
        <div v-if="数值列数" class="sum-bar">
          <span>本页合计</span>
          <span v-for="c in 列.filter((x) => 是数值型(x.型))" :key="c.列">
            {{ c.列 }} <b>{{ 数值文本(合计[c.列]) }}</b>
          </span>
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
          <b>口径</b>：一行 = 视图里的一行，页面<b>不做任何二次聚合</b>；排序在服务端完成，
          合计只统计<b>当前页</b>的数值列（跨页合计请导出后算，或直接用视图自带的汇总接口）。
          列按 SQL 类型着色：数值右对齐、日期统一 <code>yyyy-MM-dd HH:mm:ss</code>。
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

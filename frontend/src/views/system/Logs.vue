<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 登录日志：GET /api/system/login-logs?page&size
 *
 * 口径：一页 = 一次登录尝试（成功或失败都会留痕），按登录时间倒序。
 * 后端只回当前页，所以 KPI 指的是"本页样本"，页面上必须写明，
 * 否则会把"本页 30 条里 4 条失败"误读成"系统一共 4 条失败"。
 */

const 行 = ref<Record<string, any>[]>([])
const 总数 = ref(0)
const 加载中 = ref(false)
const 错误 = ref('')
const 查询 = reactive({ page: 1, size: 30 })

/** 本页内的客户端筛选：后端接口只认 page/size，加筛选不扩接口 */
const 关键词 = ref('')
const 只看失败 = ref(false)
/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)

const 过滤后 = computed(() => {
  let 集 = 行.value
  if (只看失败.value) 集 = 集.filter((r) => !是成功(r))
  const k = 关键词.value.trim().toLowerCase()
  if (!k) return 集
  return 集.filter((r) =>
    [取值(r, '工号', '尝试工号'), 取值(r, '姓名'), 取值(r, '登录IP', 'IP'), 取值(r, '客户端')]
      .map((v) => String(v ?? '').toLowerCase())
      .some((v) => v.includes(k))
  )
})

const 有筛选 = computed(() => !!关键词.value.trim() || 只看失败.value)

/* ------------------------------------------------------------------ KPI */

const 本页失败数 = computed(() => 行.value.filter((r) => !是成功(r)).length)
const 本页成功数 = computed(() => 行.value.length - 本页失败数.value)
const 本页成功率 = computed(() =>
  行.value.length ? Math.round((本页成功数.value / 行.value.length) * 100) : 100
)
const 异常账号数 = computed(
  () => new Set(行.value.filter((r) => !是成功(r)).map((r) => 取值(r, '工号', '尝试工号')).filter(Boolean)).size
)
/** 本页时间跨度：用来确认"这一页落在哪个时间段"，比单看条数有用 */
const 本页区间 = computed(() => {
  const 时 = 行.value.map((r) => 取值(r, '登录时间')).filter(Boolean).map(String).sort()
  if (!时.length) return '—'
  const 起 = 时[0]
  const 止 = 时[时.length - 1]
  return 起 === 止 ? 起 : `${起} ~ ${止}`
})

/* ------------------------------------------------------------ 取值与判定 */

/** 登录结果：后端是 tinyint(0..3)，也可能直接给中文名，两种都兜住 */
function 结果名(v: any) {
  const s = String(v ?? '')
  if (!s) return '未知'
  const n = Number(s)
  if (Number.isNaN(n)) return s
  switch (n) {
    case 0:
      return '失败'
    case 1:
      return '成功'
    case 2:
      return '成功（已锁定）'
    case 3:
      return '拒绝'
    default:
      return s
  }
}

function 是成功(row: Record<string, any>) {
  return 结果名(取值(row, '登录结果', '结果')).startsWith('成功')
}

/** 结果标签语义：成功绿 / 拒绝橙 / 失败红 / 未知灰 */
function 结果类型(row: Record<string, any>): 'success' | 'warning' | 'danger' | 'info' {
  const s = 结果名(取值(row, '登录结果', '结果'))
  if (s.startsWith('成功')) return 'success'
  if (s === '拒绝') return 'warning'
  if (s === '失败') return 'danger'
  return 'info'
}

function 取值(row: Record<string, any>, ...keys: string[]) {
  for (const k of keys) {
    if (row[k] !== undefined && row[k] !== null && row[k] !== '') return row[k]
  }
  return ''
}

/** 时间列：后端可能给 ISO 串，统一成 YYYY-MM-DD HH:mm:ss；不是日期原样返回 */
function 时间文本(v: any): string {
  const s = String(v ?? '')
  if (!s) return ''
  const m = s.match(/^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2}))?/)
  return m ? `${m[1]}-${m[2]}-${m[3]} ${m[4]}:${m[5]}:${m[6] ?? '00'}` : s
}

/* ------------------------------------------------------------------ 数据 */

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r: any = await api.system.登录日志({ page: 查询.page, size: 查询.size })
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '登录日志加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
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
  关键词.value = ''
  只看失败.value = false
  查询.page = 1
  加载()
}

/** 导出当前页（所有页）为 CSV；导出的是"未筛选的整页"，避免误以为导出全库 */
function 导出() {
  if (!行.value.length) {
    ElMessage.warning('当前页没有可导出的记录')
    return
  }
  const 表头 = ['登录时间', '工号', '姓名', '登录IP', '客户端', '结果', '失败原因']
  const 体 = 行.value.map((r) =>
    [
      时间文本(取值(r, '登录时间')),
      取值(r, '工号', '尝试工号'),
      取值(r, '姓名'),
      取值(r, '登录IP', 'IP'),
      取值(r, '客户端'),
      结果名(取值(r, '登录结果', '结果')),
      取值(r, '失败原因')
    ]
      .map((v) => `"${String(v ?? '').replace(/"/g, '""')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `登录日志_第${查询.page}页_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出当前页 ${行.value.length} 条`)
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="登录日志"
      icon="Key"
      sub="每次登录尝试留一条（成功 / 失败 / 拒绝）· 按登录时间倒序 · 用于排查账号异常与共享账号"
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

    <!-- KPI 全部标注"本页"：接口不分页外聚合，写清楚才不会被误读成全库口径 -->
    <div class="stat-grid">
      <StatCard
        label="本页成功登录"
        icon="CircleCheck"
        tone="success"
        :value="本页成功数.toLocaleString('zh-CN')"
        unit="次"
        :sub="`本页共 <b>${行.length}</b> 条 · 成功率 <b>${本页成功率}%</b>`"
      />
      <StatCard
        label="本页失败 / 拒绝"
        icon="Warning"
        :tone="本页失败数 ? 'danger' : 'success'"
        :value="本页失败数.toLocaleString('zh-CN')"
        unit="次"
        :sub="本页失败数 ? `涉及 <b>${异常账号数}</b> 个工号，看下方失败原因` : '本页没有失败记录'"
      />
      <StatCard
        label="日志总数"
        icon="Document"
        tone="info"
        :value="总数.toLocaleString('zh-CN')"
        unit="条"
        sub="按登录表留存范围统计（受留存策略影响）"
      />
      <StatCard
        label="本页时间跨度"
        icon="Clock"
        :value="行.length ? `第 ${查询.page} 页` : '—'"
        :sub="本页区间"
      />
    </div>

    <el-card shadow="never">
      <!-- 移动端筛选开关：接口只认 page/size，这里的筛选都在已载入的本页内做 -->
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="有筛选" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input
          v-model="关键词"
          placeholder="工号 / 姓名 / IP / 客户端"
          clearable
          style="width: 220px"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>

        <el-checkbox v-model="只看失败" border>
          只看失败{{ 本页失败数 ? `（${本页失败数}）` : '' }}
        </el-checkbox>

        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <span class="hint">共 {{ 总数.toLocaleString('zh-CN') }} 条 · 本页 {{ 行.length }} 条</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!过滤后.length"
        :empty-title="有筛选 ? '本页没有符合条件的登录记录' : '还没有登录记录'"
        :empty-desc="
          有筛选
            ? '本页内的筛选只作用于当前页（接口不接收筛选条件），可清空条件或在其他页继续找。'
            : '任何账号通过登录页登录都会写入一条，成功后刷新即可看到。'
        "
        empty-icon="Key"
        :skeleton-rows="10"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置">
            <el-icon><RefreshLeft /></el-icon> 清除筛选
          </el-button>
        </template>

        <div class="table-wrap table-scroll-hint">
          <el-table
            :data="过滤后"
            size="small"
            stripe
            :row-class-name="({ row }) => (是成功(row) ? '' : 'row-failed')"
            max-height="calc(100vh - 380px)"
          >
            <el-table-column label="登录时间" width="170">
              <template #default="{ row }">
                <span class="num">{{ 时间文本(取值(row, '登录时间')) || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="工号" width="120">
              <template #default="{ row }">
                <span class="num">{{ 取值(row, '工号', '尝试工号') || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="姓名" width="120">
              <template #default="{ row }">{{ 取值(row, '姓名') || '—' }}</template>
            </el-table-column>
            <el-table-column label="登录 IP" width="150">
              <template #default="{ row }">
                <span class="num">{{ 取值(row, '登录IP', 'IP') || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="结果" width="120">
              <template #default="{ row }">
                <el-tag :type="结果类型(row)" size="small" effect="light">
                  {{ 结果名(取值(row, '登录结果', '结果')) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="客户端" min-width="200" show-overflow-tooltip>
              <template #default="{ row }">{{ 取值(row, '客户端') || '—' }}</template>
            </el-table-column>
            <el-table-column label="失败原因" min-width="180" show-overflow-tooltip>
              <template #default="{ row }">
                <span :class="取值(row, '失败原因') ? 'text-danger' : 'flat'">
                  {{ 取值(row, '失败原因') || '—' }}
                </span>
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
          :page-sizes="[30, 50, 100]"
          @current-change="换页"
          @size-change="换页大小"
        />

        <div class="caliber-note">
          <b>口径</b>：本页 KPI 与筛选都只算<b>当前页</b>（接口只接收 page / size，不做跨页聚合）；
          结果标签按后端返回的<b>登录结果</b>判定（0 失败 / 1 成功 / 2 成功·已锁定 / 3 拒绝）；
          日志由留存策略定期清理，<b>总数</b>是留存区间内的条数，不是建库以来的全部登录次数。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 桌面：筛选条横向排列。
   ⚠ 必须限定在 >767px：窄屏由 05-响应式.css 把 .filter-bar 改成纵向，
   这里若无条件写 display:flex 会覆盖它，导致 align-items:stretch 把
   输入框拉成整宽巨块（批次 3 实测到的问题）。 */
@media (min-width: 768px) {
  .filter-collapsible {
    display: flex;
  }
}

/* 失败行淡红底：扫一眼就知道哪几条要跟进 */
:deep(.row-failed) {
  background: var(--c-danger-50) !important;
}

:deep(.row-failed:hover > td.el-table__cell) {
  background: color-mix(in srgb, var(--c-danger-500) 12%, transparent) !important;
}
</style>

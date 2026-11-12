<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 操作日志：GET /api/system/oplogs?page&size&操作类型
 *
 * 行字段是 **v3 口径**（`dbo.vw_操作日志`）：
 *   操作时间 / 工号 / 操作人 / 模块 / 功能 / 操作类型 / 请求方法 / 请求地址 /
 *   响应码 / 是否成功 / 错误信息 / 耗时毫秒 / 业务单据类型 / 业务单号 / 客户端 / 客户端IP / 资源代码
 * v2 的「目标表 / 目标描述 / 请求路径」在 v3 已被 模块+功能+业务单号+请求地址 取代，
 * 页面不再按旧字段兜底取值——那会永远显示 "—"，比不显示更容易误导。
 *
 * 与登录日志的区别：这里只记"改过数据的动作"（增删改 + 审核过账冲销 + 授权导出），
 * 由后端的审计过滤器在真实调用时写入；登录/登出记在登录日志页。
 */

const 行 = ref<Record<string, any>[]>([])
const 总数 = ref(0)
const 加载中 = ref(false)
const 错误 = ref('')
const 查询 = reactive({ page: 1, size: 50, 操作类型: '' as string })

/** 后端支持"操作类型"服务端筛选；关键词、模块、只看失败只在当前页内做 */
const 关键词 = ref('')
const 模块筛选 = ref('')
const 只看失败 = ref(false)
const 筛选展开 = ref(false)

/** 与库里的 CHECK 约束 CK_操作日志_类型 一字不差：传别的值后端要么 0 行、要么被约束拒 */
const 类型选项 = ['查询', '新增', '修改', '删除', '审核', '过账', '打印', '导出', '登录', '其他']

const 过滤后 = computed(() => {
  let 集 = 行.value
  if (模块筛选.value) 集 = 集.filter((r) => 取值(r, '模块') === 模块筛选.value)
  if (只看失败.value) 集 = 集.filter((r) => !是成功(r))
  const k = 关键词.value.trim().toLowerCase()
  if (!k) return 集
  return 集.filter((r) =>
    [取值(r, '操作人'), 取值(r, '功能'), 取值(r, '业务单号'), 取值(r, '请求地址'), 取值(r, '错误信息')]
      .map((v) => String(v ?? '').toLowerCase())
      .some((v) => v.includes(k))
  )
})

const 有筛选 = computed(
  () => !!查询.操作类型 || !!关键词.value.trim() || !!模块筛选.value || 只看失败.value
)

/* ------------------------------------------------------------------ KPI */

/** 按操作类型聚合当前页：哪类动作最多，一眼看出"这一页在干什么" */
const 类型分布 = computed(() => {
  const 计 = new Map<string, number>()
  行.value.forEach((r) => {
    const t = String(取值(r, '操作类型') || '未标注')
    计.set(t, (计.get(t) ?? 0) + 1)
  })
  return Array.from(计.entries())
    .map(([名, 数]) => ({ 名, 数 }))
    .sort((a, b) => b.数 - a.数)
})

const 首类 = computed(() => 类型分布.value[0] ?? null)
const 操作人数 = computed(
  () => new Set(行.value.map((r) => 取值(r, '操作人')).filter(Boolean)).size
)
/** 高风险动作：删除 / 冲销类 + 任何失败的动作（追责时最先看这两类） */
function 高风险行(row: Record<string, any>) {
  const t = String(取值(row, '操作类型'))
  return t.includes('删') || t.includes('冲销') || !是成功(row)
}
const 高风险数 = computed(() => 行.value.filter(高风险行).length)
const 模块列表 = computed(() =>
  Array.from(new Set(行.value.map((r) => 取值(r, '模块')).filter(Boolean))).sort()
)
/** 本页平均耗时：慢接口是"页面卡"的第一现场证据 */
const 平均耗时 = computed(() => {
  const 数 = 行.value.map((r) => Number(取值(r, '耗时毫秒'))).filter((n) => Number.isFinite(n) && n > 0)
  return 数.length ? Math.round(数.reduce((a, b) => a + b, 0) / 数.length) : 0
})

/* ------------------------------------------------------------ 取值与判定 */

/** 是否成功：后端给 是否成功(bool) 优先；缺字段时退回按响应码 2xx 判定 */
function 是成功(row: Record<string, any>) {
  const v = row['是否成功']
  if (v === true || v === 1 || v === 'true' || v === 'True') return true
  if (v === false || v === 0 || v === 'false' || v === 'False') return false
  const 码 = Number(row['响应码'])
  return Number.isFinite(码) ? 码 >= 200 && 码 < 300 : true
}

function 类型色(t: any): 'danger' | 'success' | 'warning' | 'primary' | 'info' {
  const s = String(t ?? '')
  if (s.includes('删') || s.includes('冲销')) return 'danger'
  if (s.includes('过账') || s.includes('审核')) return 'success'
  if (s.includes('改') || s.includes('新增') || s.includes('提交')) return 'warning'
  if (s.includes('登录')) return 'primary'
  return 'info'
}

function 取值(row: Record<string, any>, ...keys: string[]) {
  for (const k of keys) {
    if (row[k] !== undefined && row[k] !== null && row[k] !== '') return row[k]
  }
  return ''
}

/** 时间列统一成 YYYY-MM-DD HH:mm:ss；不是日期原样返回 */
function 时间文本(v: any): string {
  const s = String(v ?? '')
  if (!s) return ''
  const m = s.match(/^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2}))?/)
  return m ? `${m[1]}-${m[2]}-${m[3]} ${m[4]}:${m[5]}:${m[6] ?? '00'}` : s
}

/** 操作人：v3 视图给 操作人（姓名字段别名），并带上工号便于对号 */
function 操作人(row: Record<string, any>): string {
  const 名 = String(取值(row, '操作人') || '')
  const 号 = String(取值(row, '工号') || '')
  if (名 && 号) return `${名}（${号}）`
  return 名 || 号 || '—'
}

/** 目标：优先业务单号（业务单据的追责入口），没有就显示请求地址 */
function 目标(row: Record<string, any>): string {
  const 单号 = String(取值(row, '业务单号') || '')
  if (单号) return 单号
  return String(取值(row, '请求地址') || '') || '—'
}

/** 功能：v3 把 v2 的"目标描述"拆成 模块 + 功能（动作名） */
function 功能(row: Record<string, any>): string {
  return String(取值(row, '功能') || '—')
}

/** 结果文本：成功 / 失败（响应码） */
function 结果文本(row: Record<string, any>): string {
  const 码 = 取值(row, '响应码')
  const 尾巴 = 码 === '' ? '' : `（${码}）`
  return (是成功(row) ? '成功' : '失败') + 尾巴
}

/* ------------------------------------------------------------------ 数据 */

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r: any = await api.system.操作日志({
      page: 查询.page,
      size: 查询.size,
      操作类型: 查询.操作类型 || undefined
    })
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '操作日志加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

/** 操作类型是服务端筛选：变了要回第 1 页重新取；页内筛选随之清掉避免"筛了又筛" */
function 换类型(t: string | undefined) {
  查询.操作类型 = t ?? ''
  查询.page = 1
  关键词.value = ''
  模块筛选.value = ''
  只看失败.value = false
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
  关键词.value = ''
  模块筛选.value = ''
  只看失败.value = false
  查询.操作类型 = ''
  查询.page = 1
  加载()
}

function 导出() {
  if (!过滤后.value.length) {
    ElMessage.warning('当前页没有可导出的记录')
    return
  }
  const 表头 = [
    '操作时间', '操作人', '工号', '操作类型', '模块', '功能',
    '业务单号', '请求方法', '请求地址', '响应码', '是否成功', '耗时毫秒', '客户端IP', '错误信息'
  ]
  const 体 = 过滤后.value.map((r) =>
    [
      时间文本(取值(r, '操作时间')),
      取值(r, '操作人'),
      取值(r, '工号'),
      取值(r, '操作类型'),
      取值(r, '模块'),
      取值(r, '功能'),
      取值(r, '业务单号'),
      取值(r, '请求方法'),
      取值(r, '请求地址'),
      取值(r, '响应码'),
      是成功(r) ? '成功' : '失败',
      取值(r, '耗时毫秒'),
      取值(r, '客户端IP'),
      取值(r, '错误信息')
    ]
      .map((v) => `"${String(v ?? '').replace(/"/g, '""')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `操作日志_第${查询.page}页_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出当前页 ${过滤后.value.length} 条`)
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="操作日志"
      icon="Document"
      sub="记录对数据的每一次改动（增删改 / 审核 / 过账 / 冲销 / 授权 / 导出）· 按操作时间倒序 · 用于事后追责与对账"
    >
      <template #actions>
        <el-button :disabled="!过滤后.length" @click="导出">
          <el-icon><Download /></el-icon> 导出本页
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="日志总数"
        icon="Document"
        tone="info"
        :value="总数.toLocaleString('zh-CN')"
        unit="条"
        :sub="查询.操作类型 ? `已按「${查询.操作类型}」筛选` : '全部操作类型'"
      />
      <StatCard
        label="本页最常见动作"
        icon="Histogram"
        tone="brand"
        :value="首类 ? 首类.名 : '—'"
        :sub="首类 ? `本页 <b>${首类.数}</b> 条（占 ${Math.round((首类.数 / Math.max(行.length, 1)) * 100)}%）` : '本页暂无记录'"
      />
      <StatCard
        label="本页操作人"
        icon="User"
        :value="操作人数.toLocaleString('zh-CN')"
        unit="人"
        :sub="`本页共 <b>${行.length}</b> 条 · 平均耗时 <b>${平均耗时}</b> ms`"
      />
      <StatCard
        label="本页失败 / 删除冲销"
        icon="Warning"
        :tone="高风险数 ? 'warning' : 'success'"
        :value="高风险数.toLocaleString('zh-CN')"
        unit="次"
        :sub="高风险数 ? '高风险或失败动作，追责时先看这几类' : '本页没有失败、删除或冲销'"
      />
    </div>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="有筛选" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-select
          :model-value="查询.操作类型"
          clearable
          placeholder="全部操作类型"
          style="width: 160px"
          @change="换类型"
        >
          <el-option v-for="t in 类型选项" :key="t" :label="t" :value="t" />
        </el-select>

        <el-select v-model="模块筛选" clearable placeholder="全部业务模块" style="width: 160px">
          <el-option v-for="m in 模块列表" :key="m" :label="m" :value="m" />
        </el-select>

        <el-input v-model="关键词" placeholder="操作人 / 功能 / 业务单号 / 地址" clearable style="width: 240px">
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>

        <el-checkbox v-model="只看失败" border>只看失败</el-checkbox>

        <el-button type="primary" @click="查询.page = 1; 加载()">
          <el-icon><Search /></el-icon> 查询
        </el-button>
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
        :empty-title="有筛选 ? '没有符合条件的操作记录' : '还没有操作记录'"
        :empty-desc="
          有筛选
            ? '「操作类型」是服务端筛选，其余条件只作用于当前页；可清空条件或换页继续找。'
            : '业务单据的提交 / 审核 / 过账、档案的增删改、权限授权都会写入一条（由后端审计过滤器在真实调用时记录），操作后刷新即可看到。'
        "
        empty-icon="Document"
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
            :row-class-name="({ row }) => (高风险行(row) ? 'row-risky' : '')"
            max-height="calc(100vh - 380px)"
          >
            <el-table-column label="操作时间" width="170">
              <template #default="{ row }">
                <span class="num">{{ 时间文本(取值(row, '操作时间')) || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="操作人" width="150" show-overflow-tooltip>
              <template #default="{ row }">{{ 操作人(row) }}</template>
            </el-table-column>
            <el-table-column label="操作类型" width="100" align="center">
              <template #default="{ row }">
                <el-tag :type="类型色(取值(row, '操作类型'))" size="small" effect="light">
                  {{ 取值(row, '操作类型') || '—' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="模块" width="100">
              <template #default="{ row }">{{ 取值(row, '模块') || '—' }}</template>
            </el-table-column>
            <el-table-column label="功能" width="130" show-overflow-tooltip>
              <template #default="{ row }">{{ 功能(row) }}</template>
            </el-table-column>
            <el-table-column label="目标（业务单号 / 请求地址）" min-width="260" show-overflow-tooltip>
              <template #default="{ row }">
                <span class="num">{{ 目标(row) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="结果" width="120" align="center">
              <template #default="{ row }">
                <el-tag :type="是成功(row) ? 'success' : 'danger'" size="small" effect="plain">
                  {{ 结果文本(row) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="耗时" width="90" align="right">
              <template #default="{ row }">
                <span class="num">{{ 取值(row, '耗时毫秒') === '' ? '—' : `${取值(row, '耗时毫秒')} ms` }}</span>
              </template>
            </el-table-column>
            <el-table-column label="客户端 IP" width="130">
              <template #default="{ row }">
                <span class="num">{{ 取值(row, '客户端IP') || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="错误信息" min-width="200" show-overflow-tooltip>
              <template #default="{ row }">
                <span :class="是成功(row) ? 'flat' : 'text-danger'">
                  {{ 取值(row, '错误信息') || '—' }}
                </span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <!-- 本页动作构成：不用翻表格也能看出"这一页在做什么" -->
        <div class="sum-bar">
          <span>本页动作构成</span>
          <span v-for="t in 类型分布" :key="t.名">
            {{ t.名 }} <b>{{ t.数 }}</b>
          </span>
          <span v-if="!类型分布.length" class="hint">—</span>
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
          <b>口径</b>：一行 = 一次改动动作，由后端审计过滤器在<b>真实调用</b>时写入（不预生成、不伪造）；
          <b>操作类型</b>与<b>分页</b>是服务端筛选，关键词 / 模块 / 只看失败只在<b>当前页</b>内过滤。
          <b>结果</b>按响应码判定（2xx 成功，其余失败），失败的请求也会留一条并在<b>错误信息</b>里写明原因。
          删除 / 冲销与失败行淡红底，是追责优先看的几类。日志按留存策略定期清理，<b>总数</b>为留存区间内的条数。
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

/* 删除 / 冲销 / 失败行淡红底：高风险动作不埋在一堆正常操作里 */
:deep(.row-risky) {
  background: var(--c-danger-50) !important;
}

:deep(.row-risky:hover > td.el-table__cell) {
  background: color-mix(in srgb, var(--c-danger-500) 12%, transparent) !important;
}
</style>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import api from '@/api'
import { 单据表, 单据资源, 单据状态, 状态名, 状态色, type 字段 } from '@/config/modules'
import { useLookupStore } from '@/stores/lookup'
import { useAuthStore } from '@/stores/auth'
import PageHead from '@/components/PageHead.vue'
import DataState from '@/components/DataState.vue'

const route = useRoute()
const router = useRouter()
const lookup = useLookupStore()
const auth = useAuthStore()

const kind = computed(() => String(route.params.kind))
const 配置 = computed(() => 单据表[kind.value])
/** 权限资源对象名（点分英文）：动作按钮的可见性按它判，不能用 kind */
const 资源名 = computed(() => 单据资源[kind.value])
const 行 = ref<any[]>([])
const 总数 = ref(0)
const 加载中 = ref(false)
const 错误 = ref('')
const 动作中 = ref<number | null>(null)
/** 刚操作成功的行号：用于"写后高亮"，让用户看到改的是哪一行 */
const 刚更新 = ref<number | null>(null)
/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)

const 查询 = reactive({
  page: 1,
  size: 20,
  keyword: '',
  状态: null as number | null,
  日期: [] as string[]
})

const 类型选项 = Object.values(单据表).map((c) => ({ label: c.title, value: c.kind }))

const 列 = computed<字段[]>(() => {
  const fs = 配置.value?.header ?? []
  return fs.filter((f) => !f.hideInTable)
})

/** 行主键：按配置的 keyField 取，取不到再退化为"第一列" */
function 主键值(row: any): number {
  const k = 配置.value?.keyField
  if (k && row[k] !== undefined) return Number(row[k])
  const first = Object.keys(row)[0]
  return Number(row[first])
}

function 是日期(f: 字段) {
  return f.type === 'date' || f.type === 'datetime'
}

function 显示(f: 字段, row: any) {
  if (f.key === '单据类型' || f.key === '单据代码') return 行中文单类型(row[f.key])
  const v = row[f.key]
  if (v === null || v === undefined || v === '') return ''
  if (f.key === '业务类别') {
    const n = Number(v)
    return n === 1 ? '收款' : n === 2 ? '预收' : n === 3 ? '其他' : v
  }
  if (f.key === '计价方式') {
    const n = Number(v)
    return n === 1 ? '按瓶' : n === 2 ? '按公斤' : n === 3 ? '按金额比例' : v
  }
  if (f.type === 'number' && f.key === '回单状态') return Number(v) ? '已回单' : '未回单'
  if (是日期(f)) return String(v).slice(0, 10)
  if (f.type === 'money')
    return Number(v).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  if (f.type === 'number' && Math.abs(Number(v)) >= 1000) return Number(v).toLocaleString('zh-CN')
  if (f.type === 'select' && f.lookup) return lookup.名称(f.lookup, v, f.lookupValue) || v
  if (f.type === 'switch') return v ? '是' : '否'
  if (f.type === 'select' && f.options) {
    const o = f.options.find((x) => String(x.value) === String(v))
    return o ? o.label : v
  }
  return v
}

function 行中文单类型(单据类型或代码: any) {
  const s = String(单据类型或代码 ?? '')
  return 单据表[s]?.title ?? s
}

/** 行上的状态值：费用单读 审批状态（配置 statusField），其余读 单据状态 */
function 行状态(row: any): any {
  const 键 = 配置.value?.statusField ?? '单据状态'
  const v = row[键] ?? row['单据状态']
  if (v !== null && v !== undefined && v !== '') return v
  return row['单据状态名称'] ?? null
}

/* ------------------------------------------------------------ 状态与按钮 */

const 状态值 = (row: any) => {
  const v = 行状态(row)
  const n = Number(v)
  return Number.isNaN(n) ? -1 : n
}

/** 是否红字单（红字单一律不能改、不能过账） */
const 是红字 = (row: any) => !!row['是否红字']

/** 可编辑：只有草稿且非红字 */
const 可编辑 = (row: any) => 状态值(row) === 0 && !是红字(row)
const 可提交 = (row: any) => 状态值(row) === 0 && !是红字(row)
const 可审核 = (row: any) => 状态值(row) === 1
const 可过账 = (row: any) => 状态值(row) === 2
/**
 * ⚠ 批次 15 修：v3 **没有反过账过程**（已生成的凭证走 `usp_财务_红冲`），
 * 原来按"状态=已过账"就把「反过账」按钮放出来，点一次必回 409 —— 死按钮。
 * 同理「冲销」目前只对送气单实现（走销售退货单），到货/盘点在 v3 没有冲销过程。
 */
/** 录入即记账的三张财务单（批次 15）：状态直接是 3（已过账），没有提交/审核/过账动作 */
const 一次性记账 = computed(() => ['receipt', 'payment', 'expense'].includes(kind.value))

const 可冲销 = (row: any) => kind.value === 'delivery' && 状态值(row) === 3 && !是红字(row)
const 可删除 = (row: any) => 状态值(row) === 0 && !一次性记账.value

/** 后端若在行里返回了 权限/操作 清单，则以它为准做二次收窄 */
function 后端允许(row: any, 动作: string) {
  const 权限清单 =
    row['可用操作'] ?? row['可用按钮'] ?? row['权限'] ?? row['操作权限'] ?? null
  if (!权限清单) return true
  if (Array.isArray(权限清单)) return 权限清单.map(String).includes(动作)
  if (typeof 权限清单 === 'object') {
    const v = (权限清单 as Record<string, any>)[动作]
    if (v === undefined || v === null) return true
    return !!v
  }
  return true
}

/**
 * 动作 → 权限代码。**必须与后端 `动态资源表.单据动作` 和库里《权限项.权限代码》一字不差**
 * （view/add/edit/del/audit/post/print/export）。
 * ⚠ 批次 20 修：原来直接拿动作名当代码（`submit`/`approve`/`reverse`）再乘一个 `delete`，
 *   这四个代码在权限项表里**根本不存在**，于是 `有权限(资源, 'submit')` 恒为 false ——
 *   非超管岗位明明有 edit/audit/post，却永远看不到「提交 / 审核 / 冲销 / 删除」按钮
 *   （超管走 是否超级管理员 直放行，所以这一路从来没人发现）。
 */
const 动作代码: Record<string, string> = {
  edit: 'edit',
  submit: 'edit',      // 提交 = 改单
  approve: 'audit',
  post: 'post',
  reverse: 'post',     // 冲销走红冲/退货过程，与过账同级
  delete: 'del'
}

function 显示动作(row: any, 动作: 'edit' | 'submit' | 'approve' | 'post' | 'reverse' | 'delete') {
  if (!auth.有资源(资源名.value)) return false
  const 按状态: Record<string, boolean> = {
    edit: 可编辑(row),
    submit: 可提交(row),
    approve: 可审核(row),
    post: 可过账(row),
    reverse: 可冲销(row),
    delete: 可删除(row)
  }
  if (!按状态[动作]) return false
  // 动作级权限代码（后端 /auth/me 下发 权限表；超管不下发 → 只靠资源级）
  if (!auth.有权限(资源名.value ?? '', 动作代码[动作])) return false
  return 后端允许(row, 动作)
}

const 有任意操作 = (row: any) =>
  可打印(row) ||
  显示动作(row, 'edit') ||
  显示动作(row, 'submit') ||
  显示动作(row, 'approve') ||
  显示动作(row, 'post') ||
  显示动作(row, 'reverse') ||
  显示动作(row, 'delete')

/** 主操作（按状态取当前最该做的那一步）：移动端只显示这一个，避免按钮堆叠 */
function 主操作(row: any): { 动作: 'submit' | 'approve' | 'post'; 名: string; 类型: string } | null {
  if (显示动作(row, 'submit')) return { 动作: 'submit', 名: '提交', 类型: 'primary' }
  if (显示动作(row, 'approve')) return { 动作: 'approve', 名: '审核', 类型: 'warning' }
  if (显示动作(row, 'post')) return { 动作: 'post', 名: '过账', 类型: 'success' }
  return null
}

/** 是否有任何筛选条件生效（用于空态给出"清除筛选"的出口） */
const 有筛选 = computed(
  () => !!查询.keyword || 查询.状态 !== null || (查询.日期?.length ?? 0) > 0
)

/* ---------------------------------------------------------------- 数据 */

async function 加载() {
  if (!配置.value) return
  加载中.value = true
  错误.value = ''
  try {
    const params: any = {
      page: 查询.page,
      size: 查询.size,
      keyword: 查询.keyword || undefined
    }
    if (查询.日期?.length === 2) {
      params.from = 查询.日期[0]
      params.to = 查询.日期[1]
    }
    if (查询.状态 !== null && 查询.状态 !== undefined) params.状态 = 查询.状态
    const r: any = await api.orders.列表(kind.value, params)
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '单据列表加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

function 新建() {
  router.push(`/orders/${kind.value}/0`)
}

function 编辑(row: any) {
  router.push(`/orders/${kind.value}/${主键值(row)}`)
}

/** 打印（批次 14）：任何状态都能打（只读），新标签打开独立 A4 打印页 */
function 打印(row: any) {
  if (!(主键值(row) > 0)) return
  const href = router.resolve(`/print/orders/${kind.value}/${主键值(row)}?print=1`).href
  window.open(href, '_blank')
}

// 批次 15：收付款/费用 还没有打印模板（OrderPrint 只做了 送气/到货/盘点 三联），
// 先不给入口 —— 免得点开是一张空单。打印留后续批次。
const 可打印 = (row: any) => !一次性记账.value && auth.有资源(资源名.value) && 主键值(row) > 0

async function 执行动作(row: any, 动作: 'submit' | 'approve' | 'post') {
  // v3 没有反过账：状态回退只能走红冲（送气单）或财务红冲，所以这里不再提供 unpost
  const 名: Record<string, string> = { submit: '提交', approve: '审核', post: '过账' }
  const 单号 = row[配置.value.noField] ?? 主键值(row)
  await ElMessageBox.confirm(
    `确定对 ${单号} 执行「${名[动作]}」吗？`,
    `${名[动作]}确认`,
    { type: 'warning', confirmButtonText: `确定${名[动作]}`, cancelButtonText: '取消' }
  )
  动作中.value = 主键值(row)
  try {
    await api.orders.操作(kind.value, 主键值(row), 动作)
    ElMessage.success(`已${名[动作]}：${单号}`)
    await 加载()
    标记刚更新(主键值(row))
  } finally {
    动作中.value = null
  }
}

/** 写后高亮：给那一行加 1 秒的品牌色底，用户知道"改的就是这行" */
function 标记刚更新(id: number) {
  刚更新.value = id
  setTimeout(() => {
    if (刚更新.value === id) 刚更新.value = null
  }, 1200)
}

async function 冲销(row: any) {
  const { value } = await ElMessageBox.prompt(
    `冲销会生成一张红字单，原单置为「已冲销」且不可再改。请填写冲销原因：`,
    '整单冲销',
    {
      inputPlaceholder: '例如：客户退单 / 单价录错',
      inputValidator: (v) => (v && v.trim() ? true : '必须填写原因'),
      type: 'warning',
      confirmButtonText: '确认冲销',
      cancelButtonText: '取消'
    }
  )
  动作中.value = 主键值(row)
  try {
    const r: any = await api.orders.冲销(kind.value, 主键值(row), String(value).trim())
    ElMessage.success(`已冲销，红字单：${r?.单据号码 ?? r?.id ?? ''}`)
    await 加载()
  } finally {
    动作中.value = null
  }
}

async function 删除(row: any) {
  await ElMessageBox.confirm(
    `确定删除这笔${配置.value.title}吗？明细会一起删除，删除后不可恢复。`,
    '删除确认',
    { type: 'warning', confirmButtonText: '确认删除', cancelButtonText: '取消' }
  )
  动作中.value = 主键值(row)
  try {
    await api.orders.删除(kind.value, 主键值(row))
    ElMessage.success('已删除')
    await 加载()
  } finally {
    动作中.value = null
  }
}

function 重置() {
  查询.keyword = ''
  查询.状态 = null
  查询.日期 = []
  查询.page = 1
  加载()
}

function 切换类型(e: string) {
  if (!e || e === kind.value) return
  查询.page = 1
  router.push(`/orders/${e}`)
}

/** 行样式：红字单置灰带删除线；刚更新的行加高亮动画 */
function 行类名({ row }: { row: any }) {
  const 类: string[] = []
  if (状态值(row) === 9) 类.push('row-reversed')
  if (刚更新.value !== null && 主键值(row) === 刚更新.value) 类.push('row-flash')
  return 类.join(' ')
}

/** 导出当前列表为 CSV（纯前端，不影响后端契约） */
function 导出() {
  if (!行.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = 列.value.map((f) => f.label)
  const 行文本 = 行.value.map((row) =>
    列.value
      .map((f) => {
        const v = 显示(f, row)
        return `"${String(v ?? '').replace(/"/g, '""')}"`
      })
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...行文本].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${配置.value.title}_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${行.value.length} 行`)
}

watch(
  () => route.params.kind,
  () => {
    查询.page = 1
    查询.状态 = null
    查询.keyword = ''
    加载()
  }
)

onMounted(async () => {
  await lookup.加载()
  await 加载()
})
</script>

<template>
  <!-- 未知单据类型（手改地址栏/旧书签）不能整页空白：给出口让人回到可用页面 -->
  <div v-if="!配置">
    <PageHead title="单据列表" icon="Tickets" sub="未识别的单据类型" />
    <el-card shadow="never">
      <div class="empty-state">
        <div class="empty-state__icon">
          <el-icon><WarningFilled /></el-icon>
        </div>
        <div class="empty-state__title">没有这种单据：{{ kind }}</div>
        <div class="empty-state__desc">
          单据类型由地址决定（例如「送气单」是 /orders/delivery）。请从左侧菜单进入，或选一个可用类型。
        </div>
        <div class="mt-3">
          <el-button
            v-for="o in 类型选项.slice(0, 6)"
            :key="o.value"
            class="mt-1"
            @click="切换类型(o.value)"
          >
            {{ o.label }}
          </el-button>
        </div>
      </div>
    </el-card>
  </div>

  <div v-else>
    <PageHead
      :title="配置.title"
      icon="Tickets"
      :sub="
        一次性记账
          ? `共 ${总数.toLocaleString('zh-CN')} 张 · 录入即记账：保存后自动核销并生成凭证（无草稿态）`
          : `共 ${总数.toLocaleString('zh-CN')} 张 · 状态机：草稿 → 已提交 → 已审核 → 已过账`
      "
    >
      <template #actions>
        <el-select :model-value="kind" style="width: 140px" @change="切换类型">
          <el-option v-for="o in 类型选项" :key="o.value" :label="o.label" :value="o.value" />
        </el-select>
        <el-button :disabled="!行.length" @click="导出">
          <el-icon><Download /></el-icon> 导出
        </el-button>
        <el-button type="success" @click="新建">
          <el-icon><Plus /></el-icon> 新建{{ 配置.title }}
        </el-button>
      </template>
    </PageHead>

    <el-card shadow="never">
      <!-- 移动端筛选开关：小屏把筛选条收进抽屉，页面留给数据 -->
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="有筛选" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input
          v-model="查询.keyword"
          placeholder="单号 / 备注 / 客户编码"
          clearable
          style="width: 220px"
          @keyup.enter="查询.page = 1; 加载()"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>

        <el-select
          v-model="查询.状态"
          clearable
          placeholder="全部状态"
          style="width: 130px"
          @change="查询.page = 1; 加载()"
        >
          <el-option v-for="s in 单据状态" :key="s.value" :label="s.label" :value="s.value" />
        </el-select>

        <el-date-picker
          v-model="查询.日期"
          type="daterange"
          value-format="YYYY-MM-DD"
          start-placeholder="开始日期"
          end-placeholder="结束日期"
          style="width: 250px"
        />

        <el-button type="primary" @click="查询.page = 1; 加载()">
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
        :empty-title="有筛选 ? '没有符合条件的单据' : `还没有${配置.title}`"
        :empty-desc="
          有筛选
            ? '试试放宽日期范围、换一个状态，或清空关键词。'
            : `点击右上角「新建${配置.title}」开始录入第一张单据。`
        "
        empty-icon="Tickets"
        :skeleton-rows="查询.size > 20 ? 12 : 8"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置">
            <el-icon><RefreshLeft /></el-icon> 清除筛选
          </el-button>
          <el-button v-else type="primary" @click="新建">
            <el-icon><Plus /></el-icon> 新建{{ 配置.title }}
          </el-button>
        </template>

        <div class="table-wrap table-scroll-hint">
          <el-table
            :data="行"
            size="small"
            stripe
            :row-class-name="行类名"
          >
            <el-table-column
              v-for="f in 列"
              :key="f.key"
              :prop="f.key"
              :label="f.label"
              :width="f.width"
              :min-width="f.width ? undefined : 110"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <el-tag
                  v-if="f.key === '单据状态'"
                  :type="状态色(行状态(row), 配置)"
                  size="small"
                  effect="light"
                >
                  {{ 状态名(行状态(row), 配置) }}
                </el-tag>
                <el-tag
                  v-else-if="f.key === '是否红字'"
                  :type="row[f.key] ? 'danger' : 'info'"
                  size="small"
                  effect="plain"
                >
                  {{ row[f.key] ? '红字' : '正常' }}
                </el-tag>
                <el-button
                  v-else-if="f.key === 配置.noField"
                  link
                  type="primary"
                  class="doc-no"
                  @click="编辑(row)"
                >
                  {{ 显示(f, row) }}
                </el-button>
                <span v-else-if="f.type === 'money'" class="num">{{ 显示(f, row) }}</span>
                <span v-else>{{ 显示(f, row) }}</span>
              </template>
            </el-table-column>

            <el-table-column label="操作" width="250" fixed="right">
              <template #default="{ row }">
                <!-- 桌面：全部可用动作 -->
                <div v-loading="动作中 === 主键值(row)" class="ops ops--desktop">
                  <el-button v-if="显示动作(row, 'edit')" link type="primary" @click="编辑(row)">
                    编辑
                  </el-button>
                  <el-button v-if="可打印(row)" link type="info" @click="打印(row)">
                    打印
                  </el-button>
                  <el-button
                    v-if="显示动作(row, 'submit')"
                    link
                    type="primary"
                    @click="执行动作(row, 'submit')"
                  >
                    提交
                  </el-button>
                  <el-button
                    v-if="显示动作(row, 'approve')"
                    link
                    type="warning"
                    @click="执行动作(row, 'approve')"
                  >
                    审核
                  </el-button>
                  <el-button
                    v-if="显示动作(row, 'post')"
                    link
                    type="success"
                    @click="执行动作(row, 'post')"
                  >
                    过账
                  </el-button>
                  <el-button
                    v-if="显示动作(row, 'reverse')"
                    link
                    type="danger"
                    @click="冲销(row)"
                  >
                    冲销
                  </el-button>
                  <el-button v-if="显示动作(row, 'delete')" link type="danger" @click="删除(row)">
                    删除
                  </el-button>
                  <span v-if="!有任意操作(row)" class="none">—</span>
                </div>

                <!-- 移动端：详情 + 当前该做的那一步，避免 7 个按钮挤成一团 -->
                <div class="ops ops--mobile">
                  <el-button v-if="显示动作(row, 'edit')" link type="primary" @click="编辑(row)">
                    详情
                  </el-button>
                  <el-button
                    v-if="主操作(row)"
                    link
                    :type="主操作(row)!.类型 as any"
                    @click="执行动作(row, 主操作(row)!.动作)"
                  >
                    {{ 主操作(row)!.名 }}
                  </el-button>
                  <span v-if="!显示动作(row, 'edit') && !主操作(row)" class="none">—</span>
                </div>
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
          v-model:current-page="查询.page"
          v-model:page-size="查询.size"
          :page-sizes="[20, 50, 100]"
          @current-change="加载"
          @size-change="查询.page = 1; 加载()"
        />
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

/* 单号做成可点链接：悬浮下划线，明确"能点开" */
.doc-no {
  font-family: var(--font-num);
  font-weight: var(--fw-medium);
  padding: 0;
}

.ops {
  display: flex;
  flex-wrap: wrap;
  gap: 0 6px;
}

.ops .el-button {
  margin-left: 0;
}

.ops--mobile {
  display: none;
}

.none {
  color: var(--c-ink-300);
}

/* 移动端只留"详情 + 主操作"，操作列收窄，给数据列让出宽度 */
@media (max-width: 767px) {
  .ops--desktop {
    display: none;
  }

  .ops--mobile {
    display: flex;
  }
}

:deep(.row-reversed) {
  color: var(--c-ink-400);
  text-decoration: line-through;
}

:deep(.row-reversed .el-tag),
:deep(.row-reversed .el-button) {
  text-decoration: none;
}
</style>


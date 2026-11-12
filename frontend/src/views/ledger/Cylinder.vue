<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api, { type 行数据, type 钢瓶统计 } from '@/api'
import { useLookupStore } from '@/stores/lookup'
import { useAuthStore } from '@/stores/auth'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 钢瓶台账（批次 18）—— 从只读视图升级为作业面。
 *
 * 一条记录 = 一只瓶（钢瓶档案），一物一码，钢瓶号码全局唯一。
 * 建档 / 检验 / 报废 / 遗失 / 移动 全部调库里的 usp_钢瓶_* 过程：
 *   · 检验记录只增不改（本页没有改/删入口）；
 *   · 移动只改一物一码的当前位置/轨迹，不改变库存结存数量；
 *   · 钢瓶过程不写库存流水与凭证，数量库存请走库存调拨（批次 19）。
 */

const lookup = useLookupStore()
const auth = useAuthStore()

const 行 = ref<行数据[]>([])
const 统计 = ref<钢瓶统计>(空统计())
const 总数 = ref(0)
const 加载中 = ref(false)
const 错误 = ref('')
const 筛选展开 = ref(false)

const 查询 = reactive({
  page: 1,
  size: 50,
  keyword: '',
  状态: '',
  仓库编号: undefined as number | undefined,
  逾期: false
})

const 状态选项 = ['在库', '在途', '在用', '借出', '报检', '报废', '遗失']

const 仓库选项 = computed(() => lookup.列表('仓库').filter((w) => !!w.是否有效 && !w.是否车仓))
const 商品选项 = computed(() => lookup.列表('商品').filter((p) => !!p.是否气品))
const 客户选项 = computed(() => lookup.列表('客户').filter((c) => !!c.是否有效))
const 供应商选项 = computed(() => lookup.列表('供应商').filter((s) => !!s.是否有效))

const 有筛选 = computed(
  () => !!查询.keyword.trim() || !!查询.状态 || 查询.仓库编号 !== undefined || 查询.逾期
)

function 空统计(): 钢瓶统计 {
  return { 总数: 0, 在库: 0, 在途: 0, 在用: 0, 借出: 0, 报检: 0, 报废: 0, 遗失: 0, 逾期未检: 0 }
}

function 数(v: any): number {
  const n = Number(v)
  return Number.isFinite(n) ? n : 0
}

function 文本(v: any): string {
  if (v === null || v === undefined || v === '') return '—'
  return String(v)
}

function 日期(v: any): string {
  if (v === null || v === undefined || v === '') return '—'
  const s = String(v)
  return /^\d{4}-\d{2}-\d{2}/.test(s) ? s.slice(0, 10) : s
}

function 数值(v: any): string {
  const n = Number(v)
  if (!Number.isFinite(n)) return '—'
  return n.toLocaleString('zh-CN', { maximumFractionDigits: 2 })
}

function 今天(): string {
  const d = new Date()
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

function 状态类型(s: any): 'success' | 'warning' | 'danger' | 'info' | 'primary' {
  switch (String(s ?? '')) {
    case '在库':
      return 'success'
    case '在用':
      return 'primary'
    case '在途':
    case '报检':
      return 'warning'
    case '借出':
      return 'info'
    case '报废':
    case '遗失':
      return 'danger'
    default:
      return 'info'
  }
}

function 逾期(row: 行数据): boolean {
  return !!row.是否逾期未检
}

function 可检验(row: 行数据): boolean {
  return auth.有权限('Cyl.Inspect', 'add') && !['报废', '遗失'].includes(String(row.状态))
}

function 可移动(row: 行数据): boolean {
  return auth.有权限('Cyl.Archive', 'edit') && String(row.状态) === '在库' && !!row.当前仓库编号
}

function 可报废(row: 行数据): boolean {
  return auth.有权限('Cyl.Scrap', 'add') && !['报废', '遗失'].includes(String(row.状态))
}

function 可遗失(row: 行数据): boolean {
  return auth.有权限('Cyl.Scrap', 'add') && !['报废', '遗失'].includes(String(row.状态))
}

const 可建档 = computed(() => auth.有权限('Cyl.Archive', 'add'))

const 有任意动作 = (row: 行数据) => 可检验(row) || 可移动(row) || 可报废(row) || 可遗失(row)

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r = await api.cylinders.列表({
      page: 查询.page,
      size: 查询.size,
      keyword: 查询.keyword.trim() || undefined,
      状态: 查询.状态 || undefined,
      仓库编号: 查询.仓库编号,
      逾期: 查询.逾期 || undefined
    })
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
    const s = r?.统计
    统计.value = {
      总数: 数(s?.总数),
      在库: 数(s?.在库),
      在途: 数(s?.在途),
      在用: 数(s?.在用),
      借出: 数(s?.借出),
      报检: 数(s?.报检),
      报废: 数(s?.报废),
      遗失: 数(s?.遗失),
      逾期未检: 数(s?.逾期未检)
    }
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '钢瓶台账加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

function 重置() {
  查询.keyword = ''
  查询.状态 = ''
  查询.仓库编号 = undefined
  查询.逾期 = false
  查询.page = 1
  加载()
}

function 导出() {
  if (!行.value.length) {
    ElMessage.warning('当前页没有可导出的钢瓶')
    return
  }
  const 列名 = ['钢瓶号码', '商品名称', '瓶型', '状态', '产权归属', '当前仓库', '当前客户', '上次检验', '下次检验', '条码']
  const 体 = 行.value.map((r) =>
    [
      r.钢瓶号码,
      r.商品名称,
      r.瓶型,
      r.状态,
      r.产权归属,
      r.仓库名称,
      r.客户简称,
      日期(r.上次检验日期),
      日期(r.下次检验日期),
      r.条码
    ]
      .map((v) => `"${String(v ?? '').replace(/"/g, '""')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [列名.join(','), ...体].join('\r\n')
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = `钢瓶台账_${今天()}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出当前页 ${行.value.length} 只`)
}

/* ------------------------------------------------------------ 批量建档 */

interface 建档行 {
  钢瓶号码: string
  条码: string
  生产日期: string
}

const 建档 = reactive({
  可见: false,
  提交中: false,
  商品编号: undefined as number | undefined,
  产权归属: '自有' as '自有' | '客户' | '供应商',
  客户编号: undefined as number | undefined,
  供应商编号: undefined as number | undefined,
  当前仓库编号: undefined as number | undefined,
  生产厂家: '',
  生产日期: '',
  生产批次: '',
  实际自重公斤: undefined as number | undefined,
  最大充装量公斤: undefined as number | undefined,
  行: [] as 建档行[]
})

const 已填行数 = computed(() => 建档.行.filter((r) => r.钢瓶号码.trim()).length)

function 新行(): 建档行 {
  return { 钢瓶号码: '', 条码: '', 生产日期: '' }
}

function 打开建档() {
  建档.可见 = true
  建档.提交中 = false
  建档.商品编号 = 商品选项.value[0]?.商品编号
  建档.产权归属 = '自有'
  建档.客户编号 = undefined
  建档.供应商编号 = undefined
  建档.当前仓库编号 = 仓库选项.value[0]?.仓库编号
  建档.生产厂家 = ''
  建档.生产日期 = ''
  建档.生产批次 = ''
  建档.实际自重公斤 = undefined
  建档.最大充装量公斤 = undefined
  建档.行 = Array.from({ length: 5 }, 新行)
}

function 加行(n = 5) {
  for (let i = 0; i < n; i++) 建档.行.push(新行())
}

function 删除行(i: number) {
  if (建档.行.length <= 1) return
  建档.行.splice(i, 1)
}

function 校验建档(): string {
  if (!建档.商品编号) return '请选择商品'
  if (!建档.当前仓库编号) return '请选择入库仓库'
  if (建档.产权归属 === '客户' && !建档.客户编号) return '产权归属为客户时请选择客户'
  if (建档.产权归属 === '供应商' && !建档.供应商编号) return '产权归属为供应商时请选择供应商'
  const 有效 = 建档.行.filter((r) => r.钢瓶号码.trim())
  if (!有效.length) return '至少填写一只钢瓶号码（一物一码，每行一个号）'
  const 集 = new Set<string>()
  for (const r of 有效) {
    const 号 = r.钢瓶号码.trim()
    if (集.has(号)) return `本批钢瓶号码重复：${号}`
    集.add(号)
  }
  return ''
}

async function 提交建档() {
  const 错 = 校验建档()
  if (错) {
    ElMessage.warning(错)
    return
  }
  建档.提交中 = true
  try {
    const 明细 = 建档.行
      .filter((r) => r.钢瓶号码.trim())
      .map((r) => ({
        钢瓶号码: r.钢瓶号码.trim(),
        条码: r.条码.trim() || null,
        生产日期: r.生产日期 || null
      }))
    const r: any = await api.cylinders.批量建档({
      商品编号: 建档.商品编号,
      产权归属: 建档.产权归属,
      客户编号: 建档.产权归属 === '客户' ? 建档.客户编号 : null,
      供应商编号: 建档.产权归属 === '供应商' ? 建档.供应商编号 : null,
      当前仓库编号: 建档.当前仓库编号,
      生产厂家: 建档.生产厂家.trim() || null,
      生产日期: 建档.生产日期 || null,
      生产批次: 建档.生产批次.trim() || null,
      实际自重公斤: 建档.实际自重公斤 ?? null,
      最大充装量公斤: 建档.最大充装量公斤 ?? null,
      明细
    })
    ElMessage.success(`已建档 ${r?.数量 ?? 明细.length} 只钢瓶`)
    建档.可见 = false
    await 加载()
  } catch {
    /* 拦截器已把后端 / 校验错误提示出来，保持对话框让用户改 */
  } finally {
    建档.提交中 = false
  }
}

/* ---------------------------------------------------------------- 检验 */

const 检验 = reactive({
  可见: false,
  提交中: false,
  行: null as 行数据 | null,
  检验日期: 今天(),
  检验类型: '定期检验',
  检验机构: '',
  检验结论: '合格',
  下次检验日期: '',
  检验报告号: '',
  检验费用: undefined as number | undefined,
  备注: ''
})

function 打开检验(row: 行数据) {
  检验.行 = row
  检验.检验日期 = 今天()
  检验.检验类型 = '定期检验'
  检验.检验机构 = ''
  检验.检验结论 = '合格'
  检验.下次检验日期 = ''
  检验.检验报告号 = ''
  检验.检验费用 = undefined
  检验.备注 = ''
  检验.可见 = true
}

async function 提交检验() {
  if (!检验.行) return
  if (!检验.检验机构.trim()) {
    ElMessage.warning('请填写检验机构')
    return
  }
  检验.提交中 = true
  try {
    const r: any = await api.cylinders.检验(Number(检验.行.钢瓶编号), {
      检验日期: 检验.检验日期,
      检验类型: 检验.检验类型,
      检验机构: 检验.检验机构.trim(),
      检验结论: 检验.检验结论,
      下次检验日期: 检验.下次检验日期 || null,
      检验报告号: 检验.检验报告号.trim() || null,
      检验费用: 检验.检验费用 ?? null,
      备注: 检验.备注.trim() || null
    })
    ElMessage.success(`已登记检验：${r?.状态 ?? ''}${r?.下次检验日期 ? ` · 下次 ${日期(r.下次检验日期)}` : ''}`)
    检验.可见 = false
    await 加载()
  } catch {
    /* 拦截器已提示 */
  } finally {
    检验.提交中 = false
  }
}

/* ---------------------------------------------------------------- 报废 */

const 报废 = reactive({
  可见: false,
  提交中: false,
  行: null as 行数据 | null,
  报废日期: 今天(),
  报废原因: '检验报废',
  报废原因说明: '',
  残值金额: 0 as number | undefined,
  备注: ''
})

const 报废原因选项 = ['检验报废', '腐蚀损坏', '事故损坏', '超期', '其他']

function 打开报废(row: 行数据) {
  报废.行 = row
  报废.报废日期 = 今天()
  报废.报废原因 = '检验报废'
  报废.报废原因说明 = ''
  报废.残值金额 = 0
  报废.备注 = ''
  报废.可见 = true
}

async function 提交报废() {
  if (!报废.行) return
  报废.提交中 = true
  try {
    const r: any = await api.cylinders.报废(Number(报废.行.钢瓶编号), {
      报废日期: 报废.报废日期,
      报废原因: 报废.报废原因,
      报废原因说明: 报废.报废原因说明.trim() || null,
      残值金额: 报废.残值金额 ?? 0,
      备注: 报废.备注.trim() || null
    })
    ElMessage.success(`已报废：${r?.单据号码 ?? ''}`)
    报废.可见 = false
    await 加载()
  } catch {
    /* 拦截器已提示 */
  } finally {
    报废.提交中 = false
  }
}

/* ---------------------------------------------------------------- 遗失 */

const 遗失 = reactive({
  可见: false,
  提交中: false,
  行: null as 行数据 | null,
  客户编号: undefined as number | undefined,
  遗失日期: 今天(),
  赔偿方式: '现金',
  赔偿金额: 0 as number | undefined,
  备注: ''
})

const 赔偿方式选项 = ['现金', '押金抵扣', '免赔', '换瓶']

function 打开遗失(row: 行数据) {
  遗失.行 = row
  遗失.客户编号 = row.当前客户编号 ? Number(row.当前客户编号) : undefined
  遗失.遗失日期 = 今天()
  遗失.赔偿方式 = '现金'
  遗失.赔偿金额 = 0
  遗失.备注 = ''
  遗失.可见 = true
}

async function 提交遗失() {
  if (!遗失.行) return
  if (!遗失.客户编号) {
    ElMessage.warning('请选择报失客户')
    return
  }
  遗失.提交中 = true
  try {
    const r: any = await api.cylinders.遗失(Number(遗失.行.钢瓶编号), {
      客户编号: 遗失.客户编号,
      遗失日期: 遗失.遗失日期,
      赔偿方式: 遗失.赔偿方式,
      赔偿金额: 遗失.赔偿金额 ?? 0,
      备注: 遗失.备注.trim() || null
    })
    ElMessage.success(`已登记遗失：${r?.单据号码 ?? ''}`)
    遗失.可见 = false
    await 加载()
  } catch {
    /* 拦截器已提示 */
  } finally {
    遗失.提交中 = false
  }
}

/* ---------------------------------------------------------------- 移动 */

const 移动 = reactive({
  可见: false,
  提交中: false,
  行: null as 行数据 | null,
  目标仓库编号: undefined as number | undefined,
  业务日期: 今天(),
  备注: ''
})

const 移动目标仓 = computed(() =>
  仓库选项.value.filter((w) => Number(w.仓库编号) !== Number(移动.行?.当前仓库编号))
)

function 打开移动(row: 行数据) {
  移动.行 = row
  移动.目标仓库编号 = 移动目标仓.value[0]?.仓库编号
  移动.业务日期 = 今天()
  移动.备注 = ''
  移动.可见 = true
}

async function 提交移动() {
  if (!移动.行) return
  if (!移动.目标仓库编号) {
    ElMessage.warning('请选择目标仓库')
    return
  }
  移动.提交中 = true
  try {
    const r: any = await api.cylinders.移动(Number(移动.行.钢瓶编号), {
      目标仓库编号: 移动.目标仓库编号,
      业务日期: 移动.业务日期,
      备注: 移动.备注.trim() || null
    })
    const 仓名 = 仓库选项.value.find((w) => Number(w.仓库编号) === Number(移动.目标仓库编号))?.仓库名称
    ElMessage.success(`已移动到：${仓名 ?? r?.当前仓库编号 ?? ''}`)
    移动.可见 = false
    await 加载()
  } catch {
    /* 拦截器已提示 */
  } finally {
    移动.提交中 = false
  }
}

/* ------------------------------------------------------------ 轨迹 / 历史 */

const 详情 = reactive({
  可见: false,
  加载中: false,
  错误: '',
  标签: 'trail',
  档案: null as 行数据 | null,
  检验记录: [] as 行数据[],
  轨迹: [] as 行数据[]
})

async function 打开详情(row: 行数据) {
  详情.可见 = true
  详情.加载中 = true
  详情.错误 = ''
  详情.标签 = 'trail'
  详情.档案 = row
  详情.检验记录 = []
  详情.轨迹 = []
  try {
    const r = await api.cylinders.详情(Number(row.钢瓶编号))
    详情.档案 = r?.档案 ?? row
    详情.检验记录 = r?.检验记录 ?? []
    详情.轨迹 = r?.轨迹 ?? []
  } catch (e: any) {
    详情.错误 = e?.response?.data?.message ?? '轨迹加载失败'
  } finally {
    详情.加载中 = false
  }
}

onMounted(async () => {
  await lookup.加载()
  await 加载()
})
</script>

<template>
  <div>
    <PageHead
      title="钢瓶台账"
      icon="Coin"
      :sub="`一物一码 · 在册 ${统计.总数.toLocaleString('zh-CN')} 只 · 状态 / 检验 / 位置都在这只瓶的档案上`"
    >
      <template #actions>
        <el-button :disabled="!行.length" @click="导出">
          <el-icon><Download /></el-icon> 导出本页
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
        <el-button v-if="可建档" type="success" @click="打开建档">
          <el-icon><Plus /></el-icon> 批量建档
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="在册钢瓶"
        icon="Coin"
        tone="brand"
        :value="统计.总数.toLocaleString('zh-CN')"
        unit="只"
        sub="钢瓶号码全局唯一，一物一码"
      />
      <StatCard
        label="在库"
        icon="Box"
        tone="success"
        :value="统计.在库.toLocaleString('zh-CN')"
        unit="只"
        :sub="`在途 <b>${统计.在途}</b> · 报检 <b>${统计.报检}</b>`"
      />
      <StatCard
        label="在用"
        icon="Van"
        tone="info"
        :value="统计.在用.toLocaleString('zh-CN')"
        unit="只"
        :sub="`借出 <b>${统计.借出}</b> · 在客户/车辆手上`"
      />
      <StatCard
        label="逾期未检"
        icon="Warning"
        :tone="统计.逾期未检 ? 'danger' : 'success'"
        :value="统计.逾期未检.toLocaleString('zh-CN')"
        unit="只"
        :sub="`报废 <b>${统计.报废}</b> · 遗失 <b>${统计.遗失}</b>`"
      />
    </div>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="有筛选" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input
          v-model="查询.keyword"
          placeholder="钢瓶号码 / 条码 / 商品 / 客户 / 仓库"
          clearable
          style="width: 240px"
          @keyup.enter="查询.page = 1; 加载()"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>

        <el-select v-model="查询.状态" clearable placeholder="全部状态" style="width: 130px">
          <el-option v-for="s in 状态选项" :key="s" :label="s" :value="s" />
        </el-select>

        <el-select v-model="查询.仓库编号" clearable filterable placeholder="全部仓库" style="width: 150px">
          <el-option
            v-for="w in 仓库选项"
            :key="w.仓库编号"
            :label="w.仓库名称"
            :value="w.仓库编号"
          />
        </el-select>

        <el-checkbox v-model="查询.逾期" border>
          只看逾期未检{{ 统计.逾期未检 ? `（${统计.逾期未检}）` : '' }}
        </el-checkbox>

        <el-button type="primary" @click="查询.page = 1; 加载()">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <span class="hint">共 {{ 总数.toLocaleString('zh-CN') }} 只</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        :empty-title="有筛选 ? '没有符合条件的钢瓶' : '还没有钢瓶档案'"
        :empty-desc="
          有筛选
            ? '放宽关键词或状态，或点「重置」看全部。'
            : '点右上角「批量建档」，一次录入 N 只钢瓶（一物一码）。'
        "
        empty-icon="Coin"
        :skeleton-rows="10"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置">
            <el-icon><RefreshLeft /></el-icon> 清除筛选
          </el-button>
          <el-button v-else-if="可建档" type="primary" @click="打开建档">
            <el-icon><Plus /></el-icon> 批量建档
          </el-button>
        </template>

        <div class="table-wrap table-scroll-hint">
          <el-table
            :data="行"
            size="small"
            stripe
            max-height="calc(100vh - 430px)"
            :row-class-name="({ row }) => (逾期(row) ? 'row-overdue' : '')"
          >
            <el-table-column label="钢瓶号码" width="150" fixed>
              <template #default="{ row }">
                <el-button link type="primary" class="doc-no" @click="打开详情(row)">
                  {{ row.钢瓶号码 }}
                </el-button>
              </template>
            </el-table-column>

            <el-table-column label="商品 / 瓶型" min-width="170" show-overflow-tooltip>
              <template #default="{ row }">
                <div>{{ 文本(row.商品名称) }}</div>
                <div class="muted">{{ 文本(row.瓶型) }}</div>
              </template>
            </el-table-column>

            <el-table-column label="状态" width="92" align="center">
              <template #default="{ row }">
                <el-tag :type="状态类型(row.状态)" size="small" effect="light">{{ row.状态 }}</el-tag>
              </template>
            </el-table-column>

            <el-table-column label="产权" width="88" align="center">
              <template #default="{ row }">
                <el-tag size="small" effect="plain" :type="row.产权归属 === '自有' ? 'info' : 'warning'">
                  {{ row.产权归属 }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column label="当前仓库" width="130" show-overflow-tooltip>
              <template #default="{ row }">{{ 文本(row.仓库名称) }}</template>
            </el-table-column>

            <el-table-column label="当前客户" width="130" show-overflow-tooltip>
              <template #default="{ row }">{{ 文本(row.客户简称) }}</template>
            </el-table-column>

            <el-table-column label="上次检验" width="108">
              <template #default="{ row }">{{ 日期(row.上次检验日期) }}</template>
            </el-table-column>

            <el-table-column label="下次检验" width="128">
              <template #default="{ row }">
                <div :class="{ 'text-danger': 逾期(row) }">{{ 日期(row.下次检验日期) }}</div>
                <div v-if="row.检验剩余天数 !== null && row.检验剩余天数 !== undefined" class="muted" :class="{ 'text-danger': 逾期(row) }">
                  {{ 逾期(row) ? `逾期 ${Math.abs(Number(row.检验剩余天数))} 天` : `剩余 ${row.检验剩余天数} 天` }}
                </div>
              </template>
            </el-table-column>

            <el-table-column label="条码" width="150" show-overflow-tooltip>
              <template #default="{ row }">{{ 文本(row.条码) }}</template>
            </el-table-column>

            <el-table-column label="操作" width="272" fixed="right">
              <template #default="{ row }">
                <div class="ops">
                  <el-button v-if="可检验(row)" link type="primary" @click="打开检验(row)">检验</el-button>
                  <el-button v-if="可移动(row)" link type="primary" @click="打开移动(row)">移动</el-button>
                  <el-button v-if="可报废(row)" link type="danger" @click="打开报废(row)">报废</el-button>
                  <el-button v-if="可遗失(row)" link type="warning" @click="打开遗失(row)">遗失</el-button>
                  <el-button link type="info" @click="打开详情(row)">轨迹</el-button>
                  <span v-if="!有任意动作(row)" class="none">—</span>
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
          :page-sizes="[50, 100, 200]"
          @current-change="加载"
          @size-change="查询.page = 1; 加载()"
        />

        <div class="caliber-note">
          <b>口径</b>：本页一只瓶 = 一条<b>钢瓶档案</b>，钢瓶号码全局唯一；建档 / 检验 / 报废 / 遗失 / 移动
          全部调库里的 <code>usp_钢瓶_*</code> 过程，检验记录<b>只增不改</b>（本页没有改 / 删入口）。
          <b>移动 ≠ 库存调拨</b>：这里的「移动」改的是<b>一物一码的当前位置与轨迹</b>，
          不改变 <code>库存结存</code> 的数量；按「仓库 × 商品 × 批次 × 状态 × 权属」转移数量请走
          「库存调拨」（批次 19）。本批钢瓶过程也一样<b>不生成凭证</b> —— 钢瓶资产与数量库存是两套口径。
        </div>
      </DataState>
    </el-card>

    <!-- ===================== 批量建档 ===================== -->
    <el-dialog
      v-model="建档.可见"
      title="批量建档 · 一物一码"
      width="1000px"
      top="4vh"
      :close-on-click-modal="false"
      destroy-on-close
    >
      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="每行一只瓶：钢瓶号码（钢印号）必填且全局唯一；空行会被忽略。整批一个事务，任何一只重复则整批不落库。"
      />

      <el-form label-width="92px" class="mt-3" size="default">
        <el-row :gutter="12">
          <el-col :xs="24" :sm="8">
            <el-form-item label="商品" required>
              <el-select v-model="建档.商品编号" filterable placeholder="选择气品商品" style="width: 100%">
                <el-option
                  v-for="p in 商品选项"
                  :key="p.商品编号"
                  :label="`${p.商品名称}${p.规格型号 ? ` · ${p.规格型号}` : ''}`"
                  :value="p.商品编号"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :xs="24" :sm="4">
            <el-form-item label="产权">
              <el-select v-model="建档.产权归属" style="width: 100%">
                <el-option label="自有" value="自有" />
                <el-option label="客户" value="客户" />
                <el-option label="供应商" value="供应商" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col v-if="建档.产权归属 === '客户'" :xs="24" :sm="4">
            <el-form-item label="客户" required>
              <el-select v-model="建档.客户编号" filterable placeholder="选择客户" style="width: 100%">
                <el-option v-for="c in 客户选项" :key="c.客户编号" :label="c.客户简称" :value="c.客户编号" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col v-if="建档.产权归属 === '供应商'" :xs="24" :sm="4">
            <el-form-item label="供应商" required>
              <el-select v-model="建档.供应商编号" filterable placeholder="选择供应商" style="width: 100%">
                <el-option v-for="s in 供应商选项" :key="s.供应商编号" :label="s.供应商简称" :value="s.供应商编号" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :xs="24" :sm="4">
            <el-form-item label="入库仓库" required>
              <el-select v-model="建档.当前仓库编号" filterable placeholder="实体仓" style="width: 100%">
                <el-option v-for="w in 仓库选项" :key="w.仓库编号" :label="w.仓库名称" :value="w.仓库编号" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :xs="24" :sm="4">
            <el-form-item label="生产厂家">
              <el-input v-model="建档.生产厂家" placeholder="可选" clearable />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="12">
          <el-col :xs="24" :sm="6">
            <el-form-item label="生产日期">
              <el-date-picker v-model="建档.生产日期" type="date" value-format="YYYY-MM-DD" placeholder="可选" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :xs="24" :sm="6">
            <el-form-item label="生产批次">
              <el-input v-model="建档.生产批次" placeholder="可选" clearable />
            </el-form-item>
          </el-col>
          <el-col :xs="24" :sm="6">
            <el-form-item label="实际自重">
              <el-input-number v-model="建档.实际自重公斤" :controls="false" :min="0" :precision="3" placeholder="kg" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :xs="24" :sm="6">
            <el-form-item label="最大充装">
              <el-input-number v-model="建档.最大充装量公斤" :controls="false" :min="0" :precision="3" placeholder="kg" style="width: 100%" />
            </el-form-item>
          </el-col>
        </el-row>
        <div class="hint mb-2">生产日期填写后，库过程按商品检验周期自动算「下次检验日期」；不填则留空。</div>
      </el-form>

      <div class="batch-toolbar">
        <el-button size="small" @click="加行(5)">
          <el-icon><Plus /></el-icon> 加 5 行
        </el-button>
        <span class="hint">已填写 <b>{{ 已填行数 }}</b> 只 · 空行忽略 · 单批最多 500 只</span>
      </div>

      <el-table :data="建档.行" size="small" border max-height="340">
        <el-table-column type="index" label="#" width="48" align="center" />
        <el-table-column label="钢瓶号码（钢印号）" min-width="190">
          <template #default="{ row }">
            <el-input v-model="row.钢瓶号码" placeholder="如 GY05-0001" clearable />
          </template>
        </el-table-column>
        <el-table-column label="条码" min-width="170">
          <template #default="{ row }">
            <el-input v-model="row.条码" placeholder="扫码枪录入 / 可选" clearable />
          </template>
        </el-table-column>
        <el-table-column label="生产日期（可选）" width="155">
          <template #default="{ row }">
            <el-date-picker v-model="row.生产日期" type="date" value-format="YYYY-MM-DD" placeholder="覆盖表头" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="70" align="center">
          <template #default="{ $index }">
            <el-button link type="danger" @click="删除行($index)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>

      <template #footer>
        <el-button @click="建档.可见 = false">取消</el-button>
        <el-button type="primary" :loading="建档.提交中" @click="提交建档">
          确认建档 {{ 已填行数 }} 只
        </el-button>
      </template>
    </el-dialog>

    <!-- ===================== 检验 ===================== -->
    <el-dialog v-model="检验.可见" title="登记检验" width="580px" :close-on-click-modal="false" destroy-on-close>
      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="检验记录只增不改；「不合格」会由过程自动把钢瓶转为「报检」，判废请另行走「报废」。"
      />
      <el-form label-width="96px" class="mt-3">
        <el-form-item label="钢瓶">
          <b>{{ 检验.行?.钢瓶号码 }}</b>
          <span class="muted ml-2">{{ 检验.行?.商品名称 }} · {{ 检验.行?.状态 }}</span>
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="检验日期" required>
              <el-date-picker v-model="检验.检验日期" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="检验类型">
              <el-select v-model="检验.检验类型" style="width: 100%">
                <el-option v-for="t in ['定期检验', '水压试验', '气密试验', '外观检查', '首次检验']" :key="t" :label="t" :value="t" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="检验结论" required>
              <el-select v-model="检验.检验结论" style="width: 100%">
                <el-option label="合格" value="合格" />
                <el-option label="不合格" value="不合格" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="下次检验">
              <el-date-picker
                v-model="检验.下次检验日期"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="合格留空则按周期自动算"
                style="width: 100%"
              />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="检验机构" required>
          <el-input v-model="检验.检验机构" placeholder="如：宜昌市特种设备检验所" clearable />
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="报告号">
              <el-input v-model="检验.检验报告号" placeholder="可选" clearable />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="检验费用">
              <el-input-number v-model="检验.检验费用" :controls="false" :min="0" :precision="2" placeholder="可选" style="width: 100%" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="备注">
          <el-input v-model="检验.备注" type="textarea" :rows="2" placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="检验.可见 = false">取消</el-button>
        <el-button type="primary" :loading="检验.提交中" @click="提交检验">登记检验</el-button>
      </template>
    </el-dialog>

    <!-- ===================== 报废 ===================== -->
    <el-dialog v-model="报废.可见" title="钢瓶报废" width="580px" :close-on-click-modal="false" destroy-on-close>
      <el-alert
        type="warning"
        :closable="false"
        show-icon
        title="报废不可逆：将生成一张报废单，并写入钢瓶状态、流转轨迹与产权变更。"
      />
      <el-form label-width="96px" class="mt-3">
        <el-form-item label="钢瓶">
          <b>{{ 报废.行?.钢瓶号码 }}</b>
          <span class="muted ml-2">{{ 报废.行?.商品名称 }} · {{ 报废.行?.状态 }}</span>
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="报废日期" required>
              <el-date-picker v-model="报废.报废日期" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="报废原因" required>
              <el-select v-model="报废.报废原因" style="width: 100%">
                <el-option v-for="r in 报废原因选项" :key="r" :label="r" :value="r" />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="原因说明">
          <el-input v-model="报废.报废原因说明" type="textarea" :rows="2" placeholder="如：筒体腐蚀 / 检验判废" />
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="残值金额">
              <el-input-number v-model="报废.残值金额" :controls="false" :min="0" :precision="2" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="备注">
              <el-input v-model="报废.备注" placeholder="可选" clearable />
            </el-form-item>
          </el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="报废.可见 = false">取消</el-button>
        <el-button type="danger" :loading="报废.提交中" @click="提交报废">确认报废</el-button>
      </template>
    </el-dialog>

    <!-- ===================== 遗失 ===================== -->
    <el-dialog v-model="遗失.可见" title="登记遗失" width="580px" :close-on-click-modal="false" destroy-on-close>
      <el-alert
        type="warning"
        :closable="false"
        show-icon
        title="遗失会生成一张遗失单，钢瓶状态置为「遗失」并写入轨迹；不能重复登记。"
      />
      <el-form label-width="96px" class="mt-3">
        <el-form-item label="钢瓶">
          <b>{{ 遗失.行?.钢瓶号码 }}</b>
          <span class="muted ml-2">{{ 遗失.行?.商品名称 }} · {{ 遗失.行?.状态 }}</span>
        </el-form-item>
        <el-form-item label="报失客户" required>
          <el-select v-model="遗失.客户编号" filterable placeholder="选择客户" style="width: 100%">
            <el-option v-for="c in 客户选项" :key="c.客户编号" :label="`${c.客户简称}（${c.客户编码}）`" :value="c.客户编号" />
          </el-select>
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="遗失日期" required>
              <el-date-picker v-model="遗失.遗失日期" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="赔偿方式">
              <el-select v-model="遗失.赔偿方式" style="width: 100%">
                <el-option v-for="w in 赔偿方式选项" :key="w" :label="w" :value="w" />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="赔偿金额">
              <el-input-number v-model="遗失.赔偿金额" :controls="false" :min="0" :precision="2" style="width: 100%" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="备注">
          <el-input v-model="遗失.备注" type="textarea" :rows="2" placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="遗失.可见 = false">取消</el-button>
        <el-button type="warning" :loading="遗失.提交中" @click="提交遗失">确认遗失</el-button>
      </template>
    </el-dialog>

    <!-- ===================== 仓间移动 ===================== -->
    <el-dialog v-model="移动.可见" title="仓间移动（一物一码）" width="540px" :close-on-click-modal="false" destroy-on-close>
      <el-alert type="info" :closable="false" show-icon>
        <template #title>
          移动只改这只瓶的<b>当前位置与流转轨迹</b>，<b>不改变库存结存数量</b>；按数量转移仓库请走「库存调拨」。
        </template>
      </el-alert>
      <el-form label-width="92px" class="mt-3">
        <el-form-item label="钢瓶">
          <b>{{ 移动.行?.钢瓶号码 }}</b>
          <span class="muted ml-2">{{ 移动.行?.商品名称 }}</span>
        </el-form-item>
        <el-form-item label="当前仓库">
          <b>{{ 移动.行?.仓库名称 || '—' }}</b>
        </el-form-item>
        <el-form-item label="目标仓库" required>
          <el-select v-model="移动.目标仓库编号" filterable placeholder="选择实体仓" style="width: 100%">
            <el-option v-for="w in 移动目标仓" :key="w.仓库编号" :label="w.仓库名称" :value="w.仓库编号" />
          </el-select>
        </el-form-item>
        <el-form-item label="业务日期">
          <el-date-picker v-model="移动.业务日期" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="移动.备注" placeholder="可选，如：调仓 / 门店补瓶" clearable />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="移动.可见 = false">取消</el-button>
        <el-button type="primary" :loading="移动.提交中" @click="提交移动">确认移动</el-button>
      </template>
    </el-dialog>

    <!-- ===================== 轨迹 / 检验历史 ===================== -->
    <el-drawer v-model="详情.可见" title="钢瓶档案 · 轨迹与检验历史" size="780px">
      <div v-if="详情.错误" class="error-panel">
        <el-icon><WarningFilled /></el-icon>
        <div class="error-panel__body">{{ 详情.错误 }}</div>
      </div>

      <div v-loading="详情.加载中">
        <template v-if="详情.档案">
          <el-descriptions :column="2" border size="small">
            <el-descriptions-item label="钢瓶号码">
              <b>{{ 详情.档案.钢瓶号码 }}</b>
            </el-descriptions-item>
            <el-descriptions-item label="商品">
              {{ 详情.档案.商品名称 }} {{ 详情.档案.瓶型 ? `· ${详情.档案.瓶型}` : '' }}
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              <el-tag :type="状态类型(详情.档案.状态)" size="small" effect="light">{{ 详情.档案.状态 }}</el-tag>
            </el-descriptions-item>
            <el-descriptions-item label="产权">{{ 详情.档案.产权归属 }}</el-descriptions-item>
            <el-descriptions-item label="当前位置">
              {{ 详情.档案.仓库名称 || 详情.档案.客户简称 || 详情.档案.车牌 || '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="条码">{{ 文本(详情.档案.条码) }}</el-descriptions-item>
            <el-descriptions-item label="上次检验">{{ 日期(详情.档案.上次检验日期) }}</el-descriptions-item>
            <el-descriptions-item label="下次检验">
              <span :class="{ 'text-danger': 详情.档案.是否逾期未检 }">{{ 日期(详情.档案.下次检验日期) }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="生产厂家">{{ 文本(详情.档案.生产厂家) }}</el-descriptions-item>
            <el-descriptions-item label="生产日期">{{ 日期(详情.档案.生产日期) }}</el-descriptions-item>
            <el-descriptions-item label="报废日期">{{ 日期(详情.档案.报废日期) }}</el-descriptions-item>
            <el-descriptions-item label="报废原因">{{ 文本(详情.档案.报废原因) }}</el-descriptions-item>
          </el-descriptions>

          <el-tabs v-model="详情.标签" class="mt-4">
            <el-tab-pane label="流转轨迹（只增不改）" name="trail">
              <el-table :data="详情.轨迹" size="small" stripe border max-height="420">
                <el-table-column label="业务日期" width="105">
                  <template #default="{ row }">{{ 日期(row.业务日期) }}</template>
                </el-table-column>
                <el-table-column prop="业务类型" label="业务类型" width="105" />
                <el-table-column label="来源 → 去向" min-width="170">
                  <template #default="{ row }">
                    {{ row.来源仓库 || row.来源客户 || '—' }} → {{ row.去向仓库 || row.去向客户 || '—' }}
                  </template>
                </el-table-column>
                <el-table-column label="单据" width="120">
                  <template #default="{ row }">
                    {{ row.单据类型代码 ? `${row.单据类型代码}${row.单据编号 ? ` #${row.单据编号}` : ''}` : '—' }}
                  </template>
                </el-table-column>
                <el-table-column prop="备注" label="备注" min-width="150" show-overflow-tooltip />
              </el-table>
              <div class="caliber-note mt-3">
                轨迹由 <code>钢瓶流转明细</code> 驱动，库里的触发器<b>禁止 UPDATE / DELETE</b>；这是资产追溯的底账。
              </div>
            </el-tab-pane>

            <el-tab-pane :label="`检验记录（${详情.检验记录.length}）`" name="inspect">
              <el-table :data="详情.检验记录" size="small" stripe border max-height="420">
                <el-table-column label="检验日期" width="105">
                  <template #default="{ row }">{{ 日期(row.检验日期) }}</template>
                </el-table-column>
                <el-table-column prop="检验类型" label="类型" width="100" />
                <el-table-column prop="检验机构" label="检验机构" min-width="150" show-overflow-tooltip />
                <el-table-column label="结论" width="80" align="center">
                  <template #default="{ row }">
                    <el-tag :type="row.检验结论 === '合格' ? 'success' : 'danger'" size="small" effect="light">
                      {{ row.检验结论 }}
                    </el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="下次检验" width="105">
                  <template #default="{ row }">{{ 日期(row.下次检验日期) }}</template>
                </el-table-column>
                <el-table-column prop="检验报告号" label="报告号" width="120" show-overflow-tooltip />
                <el-table-column label="费用" width="90" align="right">
                  <template #default="{ row }">{{ 数值(row.检验费用) }}</template>
                </el-table-column>
              </el-table>
              <div class="caliber-note mt-3">
                检验记录<b>只增不改</b>：本页只提供登记入口，不提供修改 / 删除；合格记录由触发器回填档案的下次检验日期。
              </div>
            </el-tab-pane>
          </el-tabs>
        </template>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
@media (min-width: 768px) {
  .filter-collapsible {
    display: flex;
  }
}

.doc-no {
  font-family: var(--font-num);
  font-weight: var(--fw-medium);
  padding: 0;
}

.muted {
  color: var(--c-ink-400);
  font-size: var(--fs-xs);
}

.ml-2 {
  margin-left: 8px;
}

.mb-2 {
  margin-bottom: 8px;
}

.ops {
  display: flex;
  flex-wrap: wrap;
  gap: 0 4px;
}

.ops .el-button {
  margin-left: 0;
}

.none {
  color: var(--c-ink-300);
}

.text-danger {
  color: var(--c-danger-600);
}

.batch-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
}

:deep(.row-overdue) {
  background: var(--c-danger-50) !important;
}

:deep(.row-overdue:hover > td.el-table__cell) {
  background: color-mix(in srgb, var(--c-danger-500) 14%, var(--c-danger-50)) !important;
}
</style>

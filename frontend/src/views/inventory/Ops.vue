<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import api, { type 行数据, type 结存行 } from '@/api'
import { useLookupStore } from '@/stores/lookup'
import { useAuthStore } from '@/stores/auth'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 库存作业（批次 19）：调拨 / 调整 / 冻结。
 *
 * 这一页只有"建单 + 动作"，没有任何"改结存"的入口：
 *   · 数量账（库存结存）是库存流水的镜像，只能由 usp_库存_调拨 / 调整 / 冻结 写；
 *   · 调拨一行明细 = 出调出仓 + 入调入仓两条流水；走在途时调入仓先进 TRANSIT，到达再转 AVAIL；
 *   · 调整（盘盈/盘亏/报损…）**必须填原因**，过账时同事务生成盘盈亏凭证；
 *   · 冻结是同五维的状态迁移（可用 → 冻结），所以「合计」不变，只有「可用」和「冻结」变。
 * 与钢瓶台账（批次 18）的分工：那里是"一物一码"的移动，这里是"数量"的转移，两套账互不搬。
 */

const lookup = useLookupStore()
const auth = useAuthStore()

const 页签 = ref('结存')
const 提交中 = ref(false)

const 仓库选项 = computed(() => lookup.列表('仓库').filter((w) => !!w.是否有效))
const 车仓编号集 = computed(() => new Set(仓库选项.value.filter((w) => w.是否车仓).map((w) => w.仓库编号)))
const 商品选项 = computed(() => lookup.列表('商品').filter((p) => !!p.是否气品))

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

function 时间(v: any): string {
  if (v === null || v === undefined || v === '') return '—'
  return String(v).replace('T', ' ').slice(0, 19)
}

function 金额(v: any): string {
  const n = Number(v)
  if (!Number.isFinite(n)) return '0.00'
  return n.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function 今天(): string {
  const d = new Date()
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

function 错(e: any, 兜底: string): string {
  const m = e?.response?.data?.message ?? e?.message ?? 兜底
  return String(m)
}

/* ==================================================================== 结存 */

const 结存 = reactive({
  加载中: false,
  错误: '',
  行: [] as 结存行[],
  总数: 0,
  合计: {} as 行数据,
  page: 1,
  size: 50,
  keyword: '',
  仓库编号: undefined as number | undefined,
  只看有量: true,
  只看可冻结: false
})

async function 加载结存() {
  结存.加载中 = true
  结存.错误 = ''
  try {
    const r = await api.inventory.结存({
      page: 结存.page,
      size: 结存.size,
      keyword: 结存.keyword.trim() || undefined,
      仓库编号: 结存.仓库编号,
      只看有量: 结存.只看有量 || undefined,
      只看可冻结: 结存.只看可冻结 || undefined
    })
    结存.行 = r?.items ?? []
    结存.总数 = r?.total ?? 0
    结存.合计 = r?.合计行 ?? {}
  } catch (e: any) {
    结存.错误 = 错(e, '结存加载失败')
    结存.行 = []
    结存.总数 = 0
  } finally {
    结存.加载中 = false
  }
}

const 可冻结 = (row: 结存行) => auth.有权限('Inv.Adjust', 'add') && 数(row.可用) > 0
const 可调拨 = (row: 结存行) => auth.有权限('Inv.Transfer', 'add') && 数(row.可用) > 0

/* ==================================================================== 调拨 */

interface 调拨明细 {
  商品编号?: number
  批次编号?: number
  库存状态代码: string
  权属代码: string
  调拨数量?: number
  成本单价?: number
}

const 调拨 = reactive({
  加载中: false,
  错误: '',
  行: [] as 行数据[],
  总数: 0,
  page: 1,
  size: 20,
  keyword: '',
  单据状态: undefined as number | undefined,
  在途状态: ''
})

async function 加载调拨() {
  调拨.加载中 = true
  调拨.错误 = ''
  try {
    const r = await api.inventory.调拨列表({
      page: 调拨.page,
      size: 调拨.size,
      keyword: 调拨.keyword.trim() || undefined,
      单据状态: 调拨.单据状态,
      在途状态: 调拨.在途状态 || undefined
    })
    调拨.行 = r?.items ?? []
    调拨.总数 = r?.total ?? 0
  } catch (e: any) {
    调拨.错误 = 错(e, '调拨单加载失败')
    调拨.行 = []
  } finally {
    调拨.加载中 = false
  }
}

const 调拨单 = reactive({
  可见: false,
  调出仓库编号: undefined as number | undefined,
  调入仓库编号: undefined as number | undefined,
  调拨日期: 今天(),
  是否经过在途: false,
  关联车辆编号: undefined as number | undefined,
  备注: '',
  明细: [] as 调拨明细[]
})

/** 「商品 × 仓库」→ 该仓这个商品的批次（可用/冻结/待检数量都在里面，选状态后就知道能动多少） */
const 批次缓存 = reactive<Record<string, 结存行[]>>({})

async function 载入批次(仓库编号?: number, 商品编号?: number): Promise<结存行[]> {
  if (!仓库编号 || !商品编号) return []
  const 键 = `${仓库编号}|${商品编号}`
  if (批次缓存[键]) return 批次缓存[键]
  try {
    const r = await api.inventory.结存({ page: 1, size: 200, 仓库编号, 商品编号 })
    批次缓存[键] = r?.items ?? []
  } catch (e: any) {
    ElMessage.error(错(e, '批次读取失败'))
    批次缓存[键] = []
  }
  return 批次缓存[键]
}

function 清批次缓存() {
  Object.keys(批次缓存).forEach((k) => delete 批次缓存[k])
}

function 新调拨行(): 调拨明细 {
  return { 商品编号: undefined, 批次编号: undefined, 库存状态代码: 'AVAIL', 权属代码: 'OWN', 调拨数量: undefined, 成本单价: undefined }
}

function 打开调拨(预填?: { 商品编号: number; 批次编号: number; 仓库编号: number; 可用: number }) {
  调拨单.可见 = true
  const 非车仓 = 仓库选项.value.filter((w) => !w.是否车仓)
  调拨单.调出仓库编号 = 预填?.仓库编号 ?? 非车仓[0]?.仓库编号
  调拨单.调入仓库编号 = 非车仓.find((w) => w.仓库编号 !== 调拨单.调出仓库编号)?.仓库编号
  调拨单.调拨日期 = 今天()
  调拨单.是否经过在途 = false
  调拨单.关联车辆编号 = undefined
  调拨单.备注 = ''
  调拨单.明细 = [新调拨行()]
  if (预填) {
    const 行 = 调拨单.明细[0]
    行.商品编号 = 预填.商品编号
    行.调拨数量 = Math.min(10, 预填.可用)
    void 载入批次(预填.仓库编号, 预填.商品编号).then((们) => {
      行.批次编号 = 们.find((b) => b.批次编号 === 预填.批次编号)?.批次编号
    })
  }
}

function 调拨批次候选(行: 调拨明细): 结存行[] {
  return 批次缓存[`${调拨单.调出仓库编号}|${行.商品编号}`] ?? []
}

function 调拨行上限(行: 调拨明细): number {
  const 批 = 调拨批次候选(行).find((b) => b.批次编号 === 行.批次编号)
  if (!批) return 0
  if (行.库存状态代码 === 'FROZEN') return 数(批.冻结)
  if (行.库存状态代码 === 'INSPECT') return 数(批.待检)
  return 数(批.可用)
}

async function 调拨商品变了(行: 调拨明细) {
  行.批次编号 = undefined
  await 载入批次(调拨单.调出仓库编号, 行.商品编号)
}

function 校验调拨(): string {
  if (!调拨单.调出仓库编号 || !调拨单.调入仓库编号) return '请选择调出仓库和调入仓库'
  if (调拨单.调出仓库编号 === 调拨单.调入仓库编号) return '调出仓与调入仓相同，无需调拨'
  const 涉车 = 车仓编号集.value.has(调拨单.调出仓库编号) || 车仓编号集.value.has(调拨单.调入仓库编号)
  if (涉车 && !调拨单.是否经过在途) return '车仓两端必须走在途：车仓只持有 TRANSIT 数量，到达后再转可用'
  const 有效 = 调拨单.明细.filter((x) => x.商品编号 && x.批次编号 && 数(x.调拨数量) > 0)
  if (!有效.length) return '至少填一行（商品 + 批次 + 数量 > 0）'
  for (const x of 有效) {
    if (数(x.调拨数量) > 调拨行上限(x))
      return `批次最多只能调 ${调拨行上限(x)}（按所选状态/权属的账面量），当前填了 ${数(x.调拨数量)}`
  }
  return ''
}

async function 提交调拨(过账: boolean) {
  const 错文 = 校验调拨()
  if (错文) return ElMessage.warning(错文)
  提交中.value = true
  try {
    const 明细 = 调拨单.明细
      .filter((x) => x.商品编号 && x.批次编号 && 数(x.调拨数量) > 0)
      .map((x) => ({
        商品编号: x.商品编号,
        批次编号: x.批次编号,
        库存状态代码: x.库存状态代码,
        权属代码: x.权属代码,
        调拨数量: 数(x.调拨数量),
        成本单价: x.成本单价 ?? undefined
      }))
    const r = await api.inventory.建调拨单({
      调出仓库编号: 调拨单.调出仓库编号,
      调入仓库编号: 调拨单.调入仓库编号,
      调拨日期: 调拨单.调拨日期,
      是否经过在途: 调拨单.是否经过在途,
      关联车辆编号: 调拨单.关联车辆编号 ?? undefined,
      备注: 调拨单.备注 || undefined,
      过账,
      明细
    })
    ElMessage.success(过账 ? `已${调拨单.是否经过在途 ? '发出（在途）' : '调拨'}：${r?.单据号码}` : `已存草稿：${r?.单据号码}`)
    调拨单.可见 = false
    清批次缓存()
    await Promise.all([加载调拨(), 加载结存()])
  } catch (e: any) {
    ElMessage.error(错(e, '调拨失败'))
  } finally {
    提交中.value = false
  }
}

async function 过账调拨(row: 行数据) {
  try {
    await ElMessageBox.confirm(
      `过账「${row.单据号码}」：从${row.调出仓库}扣减、加到${row.调入仓库}${row.是否经过在途 ? '（先进在途 TRANSIT）' : ''}。`,
      '调拨过账', { type: 'warning', confirmButtonText: '过账' }
    )
  } catch {
    return
  }
  提交中.value = true
  try {
    const r = await api.inventory.过账调拨(Number(row.调拨单编号))
    ElMessage.success(`已过账：${r?.单据号码 ?? row.单据号码}`)
    清批次缓存()
    await Promise.all([加载调拨(), 加载结存()])
  } catch (e: any) {
    ElMessage.error(错(e, '过账失败'))
  } finally {
    提交中.value = false
  }
}

async function 到达调拨(row: 行数据) {
  提交中.value = true
  try {
    const r = await api.inventory.到达调拨(Number(row.调拨单编号))
    ElMessage.success(`已到达：在途转可用（${r?.单据号码 ?? row.单据号码}）`)
    清批次缓存()
    await Promise.all([加载调拨(), 加载结存()])
  } catch (e: any) {
    ElMessage.error(错(e, '到达登记失败'))
  } finally {
    提交中.value = false
  }
}

const 调拨详情 = reactive({ 可见: false, 主表: {} as 行数据, 明细: [] as 行数据[], 在途: [] as 行数据[] })

async function 看调拨(id: number) {
  try {
    const r = await api.inventory.调拨详情(id)
    调拨详情.主表 = r?.主表 ?? {}
    调拨详情.明细 = r?.明细 ?? []
    调拨详情.在途 = r?.在途 ?? []
    调拨详情.可见 = true
  } catch (e: any) {
    ElMessage.error(错(e, '详情加载失败'))
  }
}

/* ==================================================================== 调整 */

const 调整类型集 = ['盘盈', '盘亏', '报损', '赠品', '样品', '其他']

const 调整 = reactive({
  加载中: false,
  错误: '',
  行: [] as 行数据[],
  总数: 0,
  page: 1,
  size: 20,
  keyword: '',
  单据状态: undefined as number | undefined,
  调整类型: ''
})

async function 加载调整() {
  调整.加载中 = true
  调整.错误 = ''
  try {
    const r = await api.inventory.调整列表({
      page: 调整.page,
      size: 调整.size,
      keyword: 调整.keyword.trim() || undefined,
      单据状态: 调整.单据状态,
      调整类型: 调整.调整类型 || undefined
    })
    调整.行 = r?.items ?? []
    调整.总数 = r?.total ?? 0
  } catch (e: any) {
    调整.错误 = 错(e, '调整单加载失败')
    调整.行 = []
  } finally {
    调整.加载中 = false
  }
}

interface 调整明细 {
  商品编号?: number
  批次编号?: number
  库存状态代码: string
  权属代码: string
  调整数量?: number
}

const 调整单 = reactive({
  可见: false,
  仓库编号: undefined as number | undefined,
  调整日期: 今天(),
  调整类型: '盘盈',
  调整原因: '',
  备注: '',
  明细: [] as 调整明细[]
})

function 新调整行(): 调整明细 {
  return { 商品编号: undefined, 批次编号: undefined, 库存状态代码: 'AVAIL', 权属代码: 'OWN', 调整数量: undefined }
}

function 打开调整(预填?: { 商品编号: number; 批次编号: number; 仓库编号: number }) {
  调整单.可见 = true
  调整单.仓库编号 = 预填?.仓库编号 ?? 仓库选项.value[0]?.仓库编号
  调整单.调整日期 = 今天()
  调整单.调整类型 = '盘盈'
  调整单.调整原因 = ''
  调整单.备注 = ''
  调整单.明细 = [新调整行()]
  if (预填) {
    const 行 = 调整单.明细[0]
    行.商品编号 = 预填.商品编号
    void 载入批次(预填.仓库编号, 预填.商品编号).then(() => {
      行.批次编号 = 预填.批次编号
    })
  }
}

function 调整批次候选(行: 调整明细): 结存行[] {
  return 批次缓存[`${调整单.仓库编号}|${行.商品编号}`] ?? []
}

/** 调减的上限是账面量，调增不设上限（这正是"调整"的用途：把账面调到与实物一致） */
function 调整行下限(行: 调整明细): number {
  const 批 = 调整批次候选(行).find((b) => b.批次编号 === 行.批次编号)
  if (!批) return 0
  if (行.库存状态代码 === 'FROZEN') return -数(批.冻结)
  if (行.库存状态代码 === 'INSPECT') return -数(批.待检)
  return -数(批.可用结存)
}

function 校验调整(): string {
  if (!调整单.仓库编号) return '请选择调整仓库'
  if (!调整单.调整原因.trim()) return '必须填写调整原因（这就是审计线索，缺它等于无痕改账）'
  const 有效 = 调整单.明细.filter((x) => x.商品编号 && x.批次编号 && 数(x.调整数量) !== 0)
  if (!有效.length) return '至少填一行（商品 + 批次 + 调整数量 ≠ 0）'
  for (const x of 有效) {
    if (数(x.调整数量) < 调整行下限(x))
      return `调减不能超过账面（该五维账面 ${-调整行下限(x)}），多减请先盘点确认`
  }
  return ''
}

async function 提交调整(过账: boolean) {
  const 错文 = 校验调整()
  if (错文) return ElMessage.warning(错文)
  提交中.value = true
  try {
    const 明细 = 调整单.明细
      .filter((x) => x.商品编号 && x.批次编号 && 数(x.调整数量) !== 0)
      .map((x) => ({
        商品编号: x.商品编号,
        批次编号: x.批次编号,
        库存状态代码: x.库存状态代码,
        权属代码: x.权属代码,
        调整数量: 数(x.调整数量)
      }))
    const r = await api.inventory.建调整单({
      仓库编号: 调整单.仓库编号,
      调整日期: 调整单.调整日期,
      调整类型: 调整单.调整类型,
      调整原因: 调整单.调整原因.trim(),
      备注: 调整单.备注 || undefined,
      过账,
      明细
    })
    ElMessage.success(过账 ? `已调整并过账：${r?.单据号码}` : `调整单已存草稿：${r?.单据号码}`)
    调整单.可见 = false
    清批次缓存()
    await Promise.all([加载调整(), 加载结存(), 加载冻结()])
  } catch (e: any) {
    ElMessage.error(错(e, '调整失败'))
  } finally {
    提交中.value = false
  }
}

async function 过账调整(row: 行数据) {
  try {
    await ElMessageBox.confirm(
      `过账「${row.单据号码}」（${row.调整类型}，净调整 ${数(row.净调整)}）：会同时生成盘盈亏凭证。`,
      '调整过账', { type: 'warning', confirmButtonText: '过账' }
    )
  } catch {
    return
  }
  提交中.value = true
  try {
    const r = await api.inventory.过账调整(Number(row.调整单编号))
    ElMessage.success(`已过账：${r?.单据号码 ?? row.单据号码}`)
    清批次缓存()
    await Promise.all([加载调整(), 加载结存()])
  } catch (e: any) {
    ElMessage.error(错(e, '过账失败'))
  } finally {
    提交中.value = false
  }
}

const 调整详情 = reactive({ 可见: false, 主表: {} as 行数据, 明细: [] as 行数据[], 凭证: [] as 行数据[] })

async function 看调整(id: number) {
  try {
    const r = await api.inventory.调整详情(id)
    调整详情.主表 = r?.主表 ?? {}
    调整详情.明细 = r?.明细 ?? []
    调整详情.凭证 = r?.凭证 ?? []
    调整详情.可见 = true
  } catch (e: any) {
    ElMessage.error(错(e, '详情加载失败'))
  }
}

/* ==================================================================== 冻结 */

const 冻结来源集 = ['质检', '纠纷', '盘点', '其他']

const 冻结 = reactive({
  加载中: false,
  错误: '',
  行: [] as 行数据[],
  总数: 0,
  page: 1,
  size: 20,
  keyword: '',
  单据状态: undefined as number | undefined
})

async function 加载冻结() {
  冻结.加载中 = true
  冻结.错误 = ''
  try {
    const r = await api.inventory.冻结列表({
      page: 冻结.page,
      size: 冻结.size,
      keyword: 冻结.keyword.trim() || undefined,
      单据状态: 冻结.单据状态
    })
    冻结.行 = r?.items ?? []
    冻结.总数 = r?.total ?? 0
  } catch (e: any) {
    冻结.错误 = 错(e, '冻结单加载失败')
    冻结.行 = []
  } finally {
    冻结.加载中 = false
  }
}

const 冻结单 = reactive({
  可见: false,
  仓库编号: undefined as number | undefined,
  商品编号: undefined as number | undefined,
  批次编号: undefined as number | undefined,
  库存状态代码: 'AVAIL',
  冻结数量: undefined as number | undefined,
  冻结原因: '',
  冻结来源: '质检',
  可用提示: 0
})

async function 打开冻结(预填?: 结存行) {
  冻结单.可见 = true
  冻结单.仓库编号 = 预填?.仓库编号 ?? 仓库选项.value[0]?.仓库编号
  冻结单.商品编号 = 预填?.商品编号
  冻结单.批次编号 = 预填?.批次编号
  冻结单.库存状态代码 = 'AVAIL'
  冻结单.冻结数量 = 预填 ? Math.min(10, 数(预填.可用)) : undefined
  冻结单.冻结原因 = ''
  冻结单.冻结来源 = '质检'
  冻结单.可用提示 = 数(预填?.可用 ?? 0)
  if (预填) await 载入批次(预填.仓库编号, 预填.商品编号)
}

function 冻结批次候选(): 结存行[] {
  return 批次缓存[`${冻结单.仓库编号}|${冻结单.商品编号}`] ?? []
}

async function 冻结商品变了() {
  冻结单.批次编号 = undefined
  冻结单.可用提示 = 0
  await 载入批次(冻结单.仓库编号, 冻结单.商品编号)
}

function 冻结可用量(): number {
  const 批 = 冻结批次候选().find((b) => b.批次编号 === 冻结单.批次编号)
  if (!批) return 0
  if (冻结单.库存状态代码 === 'FROZEN') return 数(批.冻结)
  if (冻结单.库存状态代码 === 'INSPECT') return 数(批.待检)
  return 数(批.可用)
}

async function 提交冻结() {
  if (!冻结单.仓库编号 || !冻结单.商品编号 || !冻结单.批次编号)
    return ElMessage.warning('五维冻结要给全：仓库、商品、批次都要选')
  if (!(数(冻结单.冻结数量) > 0)) return ElMessage.warning('冻结数量必须大于 0')
  if (数(冻结单.冻结数量) > 冻结可用量())
    return ElMessage.warning(`可用只有 ${冻结可用量()}，不能冻结 ${数(冻结单.冻结数量)}`)
  if (!冻结单.冻结原因.trim()) return ElMessage.warning('必须填写冻结原因')
  提交中.value = true
  try {
    const r = await api.inventory.建冻结单({
      仓库编号: 冻结单.仓库编号,
      商品编号: 冻结单.商品编号,
      批次编号: 冻结单.批次编号,
      库存状态代码: 冻结单.库存状态代码,
      冻结数量: 数(冻结单.冻结数量),
      冻结原因: 冻结单.冻结原因.trim(),
      冻结来源: 冻结单.冻结来源
    })
    ElMessage.success(`已冻结 ${r?.冻结数量}：${r?.单据号码}`)
    冻结单.可见 = false
    清批次缓存()
    await Promise.all([加载冻结(), 加载结存()])
  } catch (e: any) {
    ElMessage.error(错(e, '冻结失败'))
  } finally {
    提交中.value = false
  }
}

async function 解冻(row: 行数据) {
  try {
    await ElMessageBox.confirm(
      `解冻「${row.单据号码}」：把还在冻结的 ${数(row.冻结中数量)} 放回可用。`,
      '解冻', { type: 'warning', confirmButtonText: '解冻' }
    )
  } catch {
    return
  }
  提交中.value = true
  try {
    const r = await api.inventory.解冻(Number(row.冻结单编号))
    ElMessage.success(`已解冻：${r?.单据号码 ?? row.单据号码}`)
    清批次缓存()
    await Promise.all([加载冻结(), 加载结存()])
  } catch (e: any) {
    ElMessage.error(错(e, '解冻失败'))
  } finally {
    提交中.value = false
  }
}

/* ==================================================================== 汇总 */

const 结存合计 = computed(() => ({
  可用: 数(结存.合计.可用),
  冻结: 数(结存.合计.冻结),
  在途: 数(结存.合计.在途),
  合计: 数(结存.合计.合计),
  金额: 数(结存.合计.成本金额)
}))

const 有冻结 = computed(() => 结存合计.value.冻结 > 0)

function 切页签() {
  if (页签.value === '结存' && !结存.行.length) void 加载结存()
  if (页签.value === '调拨' && !调拨.行.length) void 加载调拨()
  if (页签.value === '调整' && !调整.行.length) void 加载调整()
  if (页签.value === '冻结' && !冻结.行.length) void 加载冻结()
}

async function 全部刷新() {
  清批次缓存()
  await Promise.all([加载结存(), 加载调拨(), 加载调整(), 加载冻结()])
}

onMounted(async () => {
  await lookup.加载()
  await 全部刷新()
})
</script>

<template>
  <div>
    <PageHead
      title="库存作业"
      icon="Box"
      sub="调拨（数量在仓库间转移）· 调整（盘盈盘亏/报损，必填原因）· 冻结（可用→冻结，五维）· 数量账一律走库里的存储过程"
    >
      <template #actions>
        <el-button
          v-if="auth.有权限('Inv.Transfer', 'add')"
          type="primary"
          plain
          @click="打开调拨()"
        >
          <el-icon><Right /></el-icon> 新建调拨
        </el-button>
        <el-button
          v-if="auth.有权限('Inv.Adjust', 'add')"
          plain
          @click="打开调整()"
        >
          <el-icon><EditPen /></el-icon> 新建调整
        </el-button>
        <el-button
          v-if="auth.有权限('Inv.Adjust', 'add')"
          plain
          @click="打开冻结()"
        >
          <el-icon><Lock /></el-icon> 新建冻结
        </el-button>
        <el-button :loading="结存.加载中" @click="全部刷新">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="可用"
        icon="Box"
        tone="success"
        :value="结存合计.可用.toLocaleString('zh-CN')"
        unit="瓶"
        :sub="`当前筛选：${结存.总数} 个五维组合`"
      />
      <StatCard
        label="冻结"
        icon="Lock"
        :tone="有冻结 ? 'danger' : 'info'"
        :value="结存合计.冻结.toLocaleString('zh-CN')"
        unit="瓶"
        :sub="有冻结 ? '质检/纠纷扣留中，出库取不到' : '当前没有冻结数量'"
      />
      <StatCard
        label="在途"
        icon="Van"
        tone="warning"
        :value="结存合计.在途.toLocaleString('zh-CN')"
        unit="瓶"
        sub="已发出未到达（到达后自动转可用）"
      />
      <StatCard
        label="合计"
        icon="Coin"
        tone="info"
        :value="结存合计.合计.toLocaleString('zh-CN')"
        unit="瓶"
        :sub="`库存金额 <b>¥${金额(结存合计.金额)}</b>（可用+冻结+在途+待检+报废）`"
      />
    </div>

    <el-card shadow="never">
      <el-tabs v-model="页签" @tab-change="切页签">
        <!-- -------------------------------------------------- 结存 -->
        <el-tab-pane label="结存（可用/冻结/合计）" name="结存">
          <div class="filter-bar">
            <el-input
              v-model="结存.keyword"
              placeholder="规格 / 批次号 / 仓库"
              clearable
              style="width: 200px"
              @keyup.enter="((结存.page = 1), 加载结存())"
            >
              <template #prefix><el-icon><Search /></el-icon></template>
            </el-input>
            <span class="filter-bar__label">仓库</span>
            <el-select v-model="结存.仓库编号" clearable placeholder="全部仓库" style="width: 170px" @change="((结存.page = 1), 加载结存())">
              <el-option v-for="w in 仓库选项" :key="w.仓库编号" :label="w.仓库名称 + (w.是否车仓 ? '（车仓）' : '')" :value="w.仓库编号" />
            </el-select>
            <el-checkbox v-model="结存.只看有量" border @change="((结存.page = 1), 加载结存())">只看有量</el-checkbox>
            <el-checkbox v-model="结存.只看可冻结" border @change="((结存.page = 1), 加载结存())">只看可冻结</el-checkbox>
            <el-button @click="加载结存()">
              <el-icon><Search /></el-icon> 查询
            </el-button>
          </div>

          <DataState
            :loading="结存.加载中"
            :error="结存.错误"
            :empty="!结存.行.length"
            empty-title="没有符合条件的结存"
            empty-desc="放宽筛选，或取消「只看有量」。五维结存由过账过程维护，这里不提供直接改数的入口。"
            empty-icon="Box"
            @retry="加载结存"
          >
            <div class="table-wrap">
              <el-table :data="结存.行" size="small" stripe max-height="520">
                <el-table-column prop="仓库名称" label="仓库" min-width="130" show-overflow-tooltip />
                <el-table-column prop="商品名称" label="规格" min-width="140" show-overflow-tooltip />
                <el-table-column prop="批次号" label="批次" width="150" show-overflow-tooltip />
                <el-table-column prop="权属名称" label="权属" width="80" />
                <el-table-column label="可用" width="90" align="right">
                  <template #default="{ row }"><span class="num ok">{{ 数(row.可用) }}</span></template>
                </el-table-column>
                <el-table-column label="冻结" width="90" align="right">
                  <template #default="{ row }">
                    <span class="num" :class="{ down: 数(row.冻结) > 0 }">{{ 数(row.冻结) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="在途" width="90" align="right">
                  <template #default="{ row }"><span class="num">{{ 数(row.在途) }}</span></template>
                </el-table-column>
                <el-table-column label="待检" width="90" align="right">
                  <template #default="{ row }"><span class="num">{{ 数(row.待检) }}</span></template>
                </el-table-column>
                <el-table-column label="合计" width="100" align="right">
                  <template #default="{ row }"><b class="num">{{ 数(row.合计) }}</b></template>
                </el-table-column>
                <el-table-column label="锁定" width="90" align="right">
                  <template #default="{ row }"><span class="num">{{ 数(row.锁定量) }}</span></template>
                </el-table-column>
                <el-table-column label="成本金额" width="130" align="right">
                  <template #default="{ row }"><span class="num">¥{{ 金额(row.成本金额) }}</span></template>
                </el-table-column>
                <el-table-column label="动作" width="170" align="center" fixed="right">
                  <template #default="{ row }">
                    <el-button v-if="可调拨(row)" link type="primary" @click="打开调拨(row)">调拨</el-button>
                    <el-button v-if="可冻结(row)" link type="warning" @click="打开冻结(row)">冻结</el-button>
                    <el-button v-if="auth.有权限('Inv.Adjust', 'add')" link @click="打开调整(row)">调整</el-button>
                    <span v-if="!可调拨(row) && !可冻结(row)" class="muted">—</span>
                  </template>
                </el-table-column>
              </el-table>
            </div>
            <el-pagination
              v-model:current-page="结存.page"
              v-model:page-size="结存.size"
              :total="结存.总数"
              :page-sizes="[20, 50, 100, 200]"
              layout="total, sizes, prev, pager, next"
              class="pager"
              @change="加载结存"
            />
            <div class="caliber-note">
              <b>口径</b>：一行 = 一个「仓库 × 商品 × 批次 × 权属」五维组，<b>库存状态转成列</b>；
              可用 = 可用状态数量 − 锁定量，合计 = 可用 + 冻结 + 在途 + 待检 + 报废。
              <b>冻结只搬状态、不搬数量</b>，所以冻结前后合计不变。本页不提供"直接改结存"的入口。
            </div>
          </DataState>
        </el-tab-pane>

        <!-- -------------------------------------------------- 调拨 -->
        <el-tab-pane label="调拨单" name="调拨">
          <div class="filter-bar">
            <el-input
              v-model="调拨.keyword"
              placeholder="单据号码 / 仓库 / 网点"
              clearable
              style="width: 200px"
              @keyup.enter="((调拨.page = 1), 加载调拨())"
            >
              <template #prefix><el-icon><Search /></el-icon></template>
            </el-input>
            <span class="filter-bar__label">单据状态</span>
            <el-select v-model="调拨.单据状态" clearable placeholder="全部" style="width: 130px" @change="((调拨.page = 1), 加载调拨())">
              <el-option label="草稿" :value="0" />
              <el-option label="已审核" :value="2" />
              <el-option label="已过账" :value="3" />
              <el-option label="已作废" :value="9" />
            </el-select>
            <span class="filter-bar__label">在途状态</span>
            <el-select v-model="调拨.在途状态" clearable placeholder="全部" style="width: 120px" @change="((调拨.page = 1), 加载调拨())">
              <el-option label="未发出" value="未发出" />
              <el-option label="在途" value="在途" />
              <el-option label="已到达" value="已到达" />
            </el-select>
            <el-button @click="((调拨.page = 1), 加载调拨())">
              <el-icon><Search /></el-icon> 查询
            </el-button>
          </div>

          <DataState
            :loading="调拨.加载中"
            :error="调拨.错误"
            :empty="!调拨.行.length"
            empty-title="还没有调拨单"
            empty-desc="点右上角「新建调拨」：选调出仓、调入仓与批次数量即可。跨网点是否走在途由单据上的「是否经过在途」决定（库里没有相关参数开关）。"
            empty-icon="Right"
            @retry="加载调拨"
          >
            <div class="table-wrap">
              <el-table :data="调拨.行" size="small" stripe max-height="520">
                <el-table-column prop="单据号码" label="单据号码" width="150" />
                <el-table-column label="调拨日期" width="110">
                  <template #default="{ row }">{{ 日期(row.调拨日期) }}</template>
                </el-table-column>
                <el-table-column prop="调出仓库" label="调出仓" min-width="130" show-overflow-tooltip />
                <el-table-column prop="调入仓库" label="调入仓" min-width="130" show-overflow-tooltip />
                <el-table-column prop="调拨类型" label="类型" width="90" />
                <el-table-column label="行数/数量" width="110" align="right">
                  <template #default="{ row }">
                    <span class="num">{{ 数(row.行数) }} 行 / {{ 数(row.调拨总量) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="状态" width="150" align="center">
                  <template #default="{ row }">
                    <el-tag :type="row.单据状态 === 3 ? 'success' : row.单据状态 === 9 ? 'danger' : 'info'" size="small" effect="light">
                      {{ row.状态名称 }}
                    </el-tag>
                    <el-tag v-if="row.在途状态 === '在途'" type="warning" size="small" effect="light">在途 {{ 数(row.在途行数) }}</el-tag>
                  </template>
                </el-table-column>
                <el-table-column prop="经办人" label="经办人" width="90" />
                <el-table-column label="动作" width="210" align="center" fixed="right">
                  <template #default="{ row }">
                    <el-button
                      v-if="row.单据状态 !== 3 && auth.有权限('Inv.Transfer', 'post')"
                      link
                      type="primary"
                      :loading="提交中"
                      @click="过账调拨(row)"
                    >过账</el-button>
                    <el-button
                      v-if="row.在途状态 === '在途' && auth.有权限('Inv.Transfer', 'edit')"
                      link
                      type="warning"
                      :loading="提交中"
                      @click="到达调拨(row)"
                    >到达</el-button>
                    <el-button link @click="看调拨(Number(row.调拨单编号))">详情</el-button>
                  </template>
                </el-table-column>
              </el-table>
            </div>
            <el-pagination
              v-model:current-page="调拨.page"
              v-model:page-size="调拨.size"
              :total="调拨.总数"
              :page-sizes="[20, 50]"
              layout="total, sizes, prev, pager, next"
              class="pager"
              @change="加载调拨"
            />
            <div class="caliber-note">
              <b>口径</b>：过账 = <b>usp_库存_调拨</b>，一行明细产生两条流水（出调出仓 + 入调入仓）；
              「是否经过在途」= 1 时调入仓先进 <b>TRANSIT</b>，点「到达」后由 <b>usp_库存_到达在途</b> 转成可用。
              调拨<b>不生成凭证</b>（仓间移动不改存货总额），也不搬钢瓶一物一码档案（那是钢瓶台账的「移动」）。
            </div>
          </DataState>
        </el-tab-pane>

        <!-- -------------------------------------------------- 调整 -->
        <el-tab-pane label="调整单" name="调整">
          <div class="filter-bar">
            <el-input
              v-model="调整.keyword"
              placeholder="单据号码 / 仓库 / 原因"
              clearable
              style="width: 200px"
              @keyup.enter="((调整.page = 1), 加载调整())"
            >
              <template #prefix><el-icon><Search /></el-icon></template>
            </el-input>
            <span class="filter-bar__label">类型</span>
            <el-select v-model="调整.调整类型" clearable placeholder="全部" style="width: 120px" @change="((调整.page = 1), 加载调整())">
              <el-option v-for="t in 调整类型集" :key="t" :label="t" :value="t" />
            </el-select>
            <span class="filter-bar__label">状态</span>
            <el-select v-model="调整.单据状态" clearable placeholder="全部" style="width: 120px" @change="((调整.page = 1), 加载调整())">
              <el-option label="草稿" :value="0" />
              <el-option label="已过账" :value="3" />
              <el-option label="已作废" :value="9" />
            </el-select>
            <el-button @click="((调整.page = 1), 加载调整())">
              <el-icon><Search /></el-icon> 查询
            </el-button>
          </div>

          <DataState
            :loading="调整.加载中"
            :error="调整.错误"
            :empty="!调整.行.length"
            empty-title="还没有调整单"
            empty-desc="盘盈/盘亏/报损都走这里：调整原因必填，过账时同事务生成盘盈亏凭证。"
            empty-icon="EditPen"
            @retry="加载调整"
          >
            <div class="table-wrap">
              <el-table :data="调整.行" size="small" stripe max-height="520">
                <el-table-column prop="单据号码" label="单据号码" width="150" />
                <el-table-column label="调整日期" width="110">
                  <template #default="{ row }">{{ 日期(row.调整日期) }}</template>
                </el-table-column>
                <el-table-column prop="仓库名称" label="仓库" min-width="120" show-overflow-tooltip />
                <el-table-column prop="调整类型" label="类型" width="90" />
                <el-table-column label="净调整" width="100" align="right">
                  <template #default="{ row }">
                    <span class="num" :class="数(row.净调整) > 0 ? 'ok' : 'down'">
                      {{ 数(row.净调整) > 0 ? '+' : '' }}{{ 数(row.净调整) }}
                    </span>
                  </template>
                </el-table-column>
                <el-table-column label="金额" width="130" align="right">
                  <template #default="{ row }"><span class="num">¥{{ 金额(row.金额) }}</span></template>
                </el-table-column>
                <el-table-column prop="调整原因" label="原因（审计线索）" min-width="200" show-overflow-tooltip />
                <el-table-column label="状态" width="130" align="center">
                  <template #default="{ row }">
                    <el-tag :type="row.单据状态 === 3 ? 'success' : 'info'" size="small" effect="light">{{ row.状态名称 }}</el-tag>
                    <el-tag v-if="数(row.凭证数)" type="warning" size="small" effect="light">凭证 {{ 数(row.凭证数) }}</el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="动作" width="150" align="center" fixed="right">
                  <template #default="{ row }">
                    <el-button
                      v-if="row.单据状态 !== 3 && auth.有权限('Inv.Adjust', 'post')"
                      link
                      type="primary"
                      :loading="提交中"
                      @click="过账调整(row)"
                    >过账</el-button>
                    <el-button link @click="看调整(Number(row.调整单编号))">详情</el-button>
                  </template>
                </el-table-column>
              </el-table>
            </div>
            <el-pagination
              v-model:current-page="调整.page"
              v-model:page-size="调整.size"
              :total="调整.总数"
              :page-sizes="[20, 50]"
              layout="total, sizes, prev, pager, next"
              class="pager"
              @change="加载调整"
            />
            <div class="caliber-note">
              <b>口径</b>：过账 = <b>usp_库存_调整</b>，正数走「调整增加」入库、负数走「调整减少」出库，
              并在同一个事务里由 <b>usp_财务_记账_盘盈亏</b> 生成凭证（数量动了账必须动）。
              <b>调整是唯一"没有业务单据也能改账"的入口，所以原因必填</b>。
            </div>
          </DataState>
        </el-tab-pane>

        <!-- -------------------------------------------------- 冻结 -->
        <el-tab-pane label="冻结单" name="冻结">
          <div class="filter-bar">
            <el-input
              v-model="冻结.keyword"
              placeholder="单据号码 / 原因 / 仓库 / 规格"
              clearable
              style="width: 220px"
              @keyup.enter="((冻结.page = 1), 加载冻结())"
            >
              <template #prefix><el-icon><Search /></el-icon></template>
            </el-input>
            <span class="filter-bar__label">状态</span>
            <el-select v-model="冻结.单据状态" clearable placeholder="全部" style="width: 130px" @change="((冻结.page = 1), 加载冻结())">
              <el-option label="冻结中" :value="1" />
              <el-option label="已解冻" :value="9" />
            </el-select>
            <el-button @click="((冻结.page = 1), 加载冻结())">
              <el-icon><Search /></el-icon> 查询
            </el-button>
          </div>

          <DataState
            :loading="冻结.加载中"
            :error="冻结.错误"
            :empty="!冻结.行.length"
            empty-title="没有冻结单"
            empty-desc="冻结按「仓库 × 商品 × 批次 × 状态 × 权属」五维下账：可用数量减、冻结数量加，合计不变。"
            empty-icon="Lock"
            @retry="加载冻结"
          >
            <div class="table-wrap">
              <el-table :data="冻结.行" size="small" stripe max-height="520">
                <el-table-column prop="单据号码" label="单据号码" width="150" />
                <el-table-column prop="仓库名称" label="仓库" min-width="120" show-overflow-tooltip />
                <el-table-column prop="商品名称" label="规格" min-width="140" show-overflow-tooltip />
                <el-table-column prop="批次号" label="批次" width="150" show-overflow-tooltip />
                <el-table-column label="冻结 / 已解冻" width="130" align="right">
                  <template #default="{ row }">
                    <span class="num">{{ 数(row.冻结数量) }} / {{ 数(row.已解冻数量) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="冻结中" width="100" align="right">
                  <template #default="{ row }">
                    <b class="num" :class="{ down: 数(row.冻结中数量) > 0 }">{{ 数(row.冻结中数量) }}</b>
                  </template>
                </el-table-column>
                <el-table-column prop="冻结原因" label="原因" min-width="180" show-overflow-tooltip />
                <el-table-column prop="冻结来源" label="来源" width="90" />
                <el-table-column label="冻结时间" width="160">
                  <template #default="{ row }">{{ 时间(row.冻结时间) }}</template>
                </el-table-column>
                <el-table-column label="状态" width="100" align="center">
                  <template #default="{ row }">
                    <el-tag :type="row.单据状态 === 9 ? 'info' : 'danger'" size="small" effect="light">{{ row.状态名称 }}</el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="动作" width="100" align="center" fixed="right">
                  <template #default="{ row }">
                    <el-button
                      v-if="数(row.冻结中数量) > 0 && auth.有权限('Inv.Adjust', 'edit')"
                      link
                      type="primary"
                      :loading="提交中"
                      @click="解冻(row)"
                    >解冻</el-button>
                    <span v-else class="muted">—</span>
                  </template>
                </el-table-column>
              </el-table>
            </div>
            <el-pagination
              v-model:current-page="冻结.page"
              v-model:page-size="冻结.size"
              :total="冻结.总数"
              :page-sizes="[20, 50]"
              layout="total, sizes, prev, pager, next"
              class="pager"
              @change="加载冻结"
            />
          </DataState>
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <!-- ================================================== 新建调拨 -->
    <el-dialog v-model="调拨单.可见" title="新建调拨单" width="900px" :close-on-click-modal="false">
      <div class="form-grid">
        <div class="form-item">
          <label>调出仓库 <em>*</em></label>
          <el-select v-model="调拨单.调出仓库编号" placeholder="选择调出仓" style="width: 100%" @change="清批次缓存()">
            <el-option v-for="w in 仓库选项" :key="w.仓库编号" :label="w.仓库名称 + (w.是否车仓 ? '（车仓）' : '')" :value="w.仓库编号" />
          </el-select>
        </div>
        <div class="form-item">
          <label>调入仓库 <em>*</em></label>
          <el-select v-model="调拨单.调入仓库编号" placeholder="选择调入仓" style="width: 100%">
            <el-option
              v-for="w in 仓库选项"
              :key="w.仓库编号"
              :label="w.仓库名称 + (w.是否车仓 ? '（车仓）' : '')"
              :value="w.仓库编号"
              :disabled="w.仓库编号 === 调拨单.调出仓库编号"
            />
          </el-select>
        </div>
        <div class="form-item">
          <label>调拨日期</label>
          <el-date-picker v-model="调拨单.调拨日期" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </div>
        <div class="form-item">
          <label>是否经过在途</label>
          <el-switch v-model="调拨单.是否经过在途" active-text="走在途（先到 TRANSIT，到达再转可用）" />
        </div>
        <div class="form-item form-item--wide">
          <label>备注</label>
          <el-input v-model="调拨单.备注" maxlength="300" placeholder="为什么调、谁批准的（进单据备注）" />
        </div>
      </div>

      <div class="line-head">
        <span>调拨明细</span>
        <el-button link type="primary" @click="调拨单.明细.push(新调拨行())">
          <el-icon><Plus /></el-icon> 加一行
        </el-button>
      </div>
      <el-table :data="调拨单.明细" size="small" border>
        <el-table-column label="商品" min-width="170">
          <template #default="{ row }">
            <el-select v-model="row.商品编号" placeholder="选商品" filterable style="width: 100%" @change="调拨商品变了(row)">
              <el-option v-for="p in 商品选项" :key="p.商品编号" :label="p.商品名称" :value="p.商品编号" />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="批次" min-width="190">
          <template #default="{ row }">
            <el-select v-model="row.批次编号" placeholder="选批次" filterable style="width: 100%" :disabled="!row.商品编号">
              <el-option
                v-for="b in 调拨批次候选(row)"
                :key="b.批次编号"
                :label="`${b.批次号}（可用 ${数(b.可用)} / 冻结 ${数(b.冻结)}）`"
                :value="b.批次编号"
              />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-select v-model="row.库存状态代码" style="width: 100%">
              <el-option label="可用" value="AVAIL" />
              <el-option label="冻结" value="FROZEN" />
              <el-option label="待检" value="INSPECT" />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="数量" width="150">
          <template #default="{ row }">
            <el-input-number v-model="row.调拨数量" :min="1" :max="调拨行上限(row) || undefined" :controls="false" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column label="成本单价" width="120">
          <template #default="{ row }">
            <el-input-number v-model="row.成本单价" :min="0" :precision="4" :controls="false" placeholder="取账面" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column label="" width="60" align="center">
          <template #default="{ $index }">
            <el-button link type="danger" :disabled="调拨单.明细.length <= 1" @click="调拨单.明细.splice($index, 1)">
              <el-icon><Delete /></el-icon>
            </el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="form-hint">
        成本单价留空 = 取<b>调出仓该五维的账面移动加权成本</b>（推荐）；手填只在盘点确认后使用。
      </div>

      <template #footer>
        <el-button @click="调拨单.可见 = false">取消</el-button>
        <el-button :loading="提交中" @click="提交调拨(false)">存草稿</el-button>
        <el-button type="primary" :loading="提交中" @click="提交调拨(true)">建单并过账</el-button>
      </template>
    </el-dialog>

    <!-- ================================================== 新建调整 -->
    <el-dialog v-model="调整单.可见" title="新建库存调整单" width="880px" :close-on-click-modal="false">
      <div class="form-grid">
        <div class="form-item">
          <label>仓库 <em>*</em></label>
          <el-select v-model="调整单.仓库编号" placeholder="选择仓库" style="width: 100%" @change="清批次缓存()">
            <el-option v-for="w in 仓库选项" :key="w.仓库编号" :label="w.仓库名称" :value="w.仓库编号" />
          </el-select>
        </div>
        <div class="form-item">
          <label>调整日期</label>
          <el-date-picker v-model="调整单.调整日期" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </div>
        <div class="form-item">
          <label>调整类型 <em>*</em></label>
          <el-select v-model="调整单.调整类型" style="width: 100%">
            <el-option v-for="t in 调整类型集" :key="t" :label="t" :value="t" />
          </el-select>
        </div>
        <div class="form-item form-item--wide">
          <label>调整原因 <em>*</em></label>
          <el-input
            v-model="调整单.调整原因"
            maxlength="300"
            show-word-limit
            placeholder="例：门店复盘 5kg 丙烷多 2 只 / 瓶体漏气判废 3 只 —— 调整单是唯一无痕改账入口，原因会进单据与操作日志"
          />
        </div>
      </div>

      <div class="line-head">
        <span>调整明细（正数 = 增加，负数 = 减少）</span>
        <el-button link type="primary" @click="调整单.明细.push(新调整行())">
          <el-icon><Plus /></el-icon> 加一行
        </el-button>
      </div>
      <el-table :data="调整单.明细" size="small" border>
        <el-table-column label="商品" min-width="170">
          <template #default="{ row }">
            <el-select v-model="row.商品编号" placeholder="选商品" filterable style="width: 100%" @change="载入批次(调整单.仓库编号, row.商品编号)">
              <el-option v-for="p in 商品选项" :key="p.商品编号" :label="p.商品名称" :value="p.商品编号" />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="批次" min-width="190">
          <template #default="{ row }">
            <el-select v-model="row.批次编号" placeholder="选批次" filterable style="width: 100%" :disabled="!row.商品编号">
              <el-option
                v-for="b in 调整批次候选(row)"
                :key="b.批次编号"
                :label="`${b.批次号}（可用 ${数(b.可用)} / 冻结 ${数(b.冻结)}）`"
                :value="b.批次编号"
              />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-select v-model="row.库存状态代码" style="width: 100%">
              <el-option label="可用" value="AVAIL" />
              <el-option label="冻结" value="FROZEN" />
              <el-option label="待检" value="INSPECT" />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="调整数量" width="150">
          <template #default="{ row }">
            <el-input-number v-model="row.调整数量" :min="调整行下限(row)" :controls="false" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column label="" width="60" align="center">
          <template #default="{ $index }">
            <el-button link type="danger" :disabled="调整单.明细.length <= 1" @click="调整单.明细.splice($index, 1)">
              <el-icon><Delete /></el-icon>
            </el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="form-hint">
        调减不能超过该五维的账面量（多减说明账实本身对不上，请先做<b>盘点单</b>）；
        过账会同时生成盘盈/盘亏凭证，月结后不能再改。
      </div>

      <template #footer>
        <el-button @click="调整单.可见 = false">取消</el-button>
        <el-button :loading="提交中" @click="提交调整(false)">存草稿</el-button>
        <el-button type="primary" :loading="提交中" @click="提交调整(true)">建单并过账</el-button>
      </template>
    </el-dialog>

    <!-- ================================================== 新建冻结 -->
    <el-dialog v-model="冻结单.可见" title="五维冻结" width="620px" :close-on-click-modal="false">
      <div class="form-grid">
        <div class="form-item">
          <label>仓库 <em>*</em></label>
          <el-select v-model="冻结单.仓库编号" style="width: 100%" @change="冻结商品变了">
            <el-option v-for="w in 仓库选项" :key="w.仓库编号" :label="w.仓库名称" :value="w.仓库编号" />
          </el-select>
        </div>
        <div class="form-item">
          <label>商品 <em>*</em></label>
          <el-select v-model="冻结单.商品编号" placeholder="选商品" filterable style="width: 100%" @change="冻结商品变了">
            <el-option v-for="p in 商品选项" :key="p.商品编号" :label="p.商品名称" :value="p.商品编号" />
          </el-select>
        </div>
        <div class="form-item">
          <label>批次 <em>*</em></label>
          <el-select v-model="冻结单.批次编号" placeholder="选批次" filterable style="width: 100%" :disabled="!冻结单.商品编号" @change="冻结单.可用提示 = 冻结可用量()">
            <el-option
              v-for="b in 冻结批次候选()"
              :key="b.批次编号"
              :label="`${b.批次号}（可用 ${数(b.可用)} / 冻结 ${数(b.冻结)} / 待检 ${数(b.待检)}）`"
              :value="b.批次编号"
            />
          </el-select>
        </div>
        <div class="form-item">
          <label>从哪个状态冻</label>
          <el-select v-model="冻结单.库存状态代码" style="width: 100%" @change="冻结单.可用提示 = 冻结可用量()">
            <el-option label="可用 AVAIL" value="AVAIL" />
            <el-option label="待检 INSPECT" value="INSPECT" />
          </el-select>
        </div>
        <div class="form-item">
          <label>冻结数量 <em>*</em></label>
          <el-input-number v-model="冻结单.冻结数量" :min="1" :max="冻结可用量() || undefined" style="width: 100%" />
          <div class="field-tip">该五维当前可动：<b>{{ 冻结可用量() }}</b></div>
        </div>
        <div class="form-item">
          <label>冻结来源</label>
          <el-select v-model="冻结单.冻结来源" style="width: 100%">
            <el-option v-for="s in 冻结来源集" :key="s" :label="s" :value="s" />
          </el-select>
        </div>
        <div class="form-item form-item--wide">
          <label>冻结原因 <em>*</em></label>
          <el-input v-model="冻结单.冻结原因" maxlength="200" show-word-limit placeholder="例：质检抽检不合格，等待特检所判定" />
        </div>
      </div>
      <div class="form-hint">
        冻结 = <b>同一五维的状态迁移</b>（可用 → 冻结），合计数量与总金额都不变，出库取不到这部分；
        解冻会把数量放回可用。批次必须选：库里过程在批次为空时会退到"该商品最小批次"，那不是你要冻的维度。
      </div>
      <template #footer>
        <el-button @click="冻结单.可见 = false">取消</el-button>
        <el-button type="primary" :loading="提交中" @click="提交冻结">冻结</el-button>
      </template>
    </el-dialog>

    <!-- ================================================== 调拨详情 -->
    <el-dialog v-model="调拨详情.可见" title="调拨单详情" width="880px">
      <div class="detail-grid">
        <span>单据号码 <b>{{ 文本(调拨详情.主表.单据号码) }}</b></span>
        <span>状态 <b>{{ 文本(调拨详情.主表.单据状态) }}</b></span>
        <span>在途 <b>{{ 文本(调拨详情.主表.在途状态) }}</b></span>
        <span>调出 <b>{{ 文本(调拨详情.主表.调出仓库) }}</b></span>
        <span>调入 <b>{{ 文本(调拨详情.主表.调入仓库) }}</b></span>
        <span>发出 <b>{{ 时间(调拨详情.主表.发出时间) }}</b></span>
        <span>实到 <b>{{ 时间(调拨详情.主表.实际到达时间) }}</b></span>
        <span>备注 <b>{{ 文本(调拨详情.主表.备注) }}</b></span>
      </div>
      <el-table :data="调拨详情.明细" size="small" border>
        <el-table-column prop="行号" label="行" width="60" />
        <el-table-column prop="商品名称" label="商品" min-width="150" />
        <el-table-column prop="批次号" label="批次" width="150" />
        <el-table-column prop="状态名称" label="状态" width="90" />
        <el-table-column prop="权属名称" label="权属" width="80" />
        <el-table-column prop="调拨数量" label="调拨" width="90" align="right" />
        <el-table-column label="实到" width="90" align="right">
          <template #default="{ row }">{{ row.实到数量 === null || row.实到数量 === undefined ? '—' : row.实到数量 }}</template>
        </el-table-column>
        <el-table-column label="成本" width="110" align="right">
          <template #default="{ row }">¥{{ 金额(row.成本单价) }}</template>
        </el-table-column>
      </el-table>
      <template v-if="调拨详情.在途.length">
        <div class="line-head"><span>在途记录</span></div>
        <el-table :data="调拨详情.在途" size="small" border>
          <el-table-column prop="商品名称" label="商品" min-width="150" />
          <el-table-column prop="批次号" label="批次" width="150" />
          <el-table-column prop="数量" label="数量" width="90" align="right" />
          <el-table-column prop="状态" label="状态" width="100" />
          <el-table-column label="发出" width="170">
            <template #default="{ row }">{{ 时间(row.发出时间) }}</template>
          </el-table-column>
          <el-table-column label="实到" width="170">
            <template #default="{ row }">{{ 时间(row.实到时间) }}</template>
          </el-table-column>
        </el-table>
      </template>
      <div class="form-hint">
        实到差异不在这里改：过程参数 @实到数量 在库里未被使用，差异请用<b>调整单</b>（或再做一次盘点）。
      </div>
    </el-dialog>

    <!-- ================================================== 调整详情 -->
    <el-dialog v-model="调整详情.可见" title="调整单详情" width="820px">
      <div class="detail-grid">
        <span>单据号码 <b>{{ 文本(调整详情.主表.单据号码) }}</b></span>
        <span>类型 <b>{{ 文本(调整详情.主表.调整类型) }}</b></span>
        <span>状态 <b>{{ 文本(调整详情.主表.状态名称) }}</b></span>
        <span>仓库 <b>{{ 文本(调整详情.主表.仓库名称) }}</b></span>
        <span>日期 <b>{{ 日期(调整详情.主表.调整日期) }}</b></span>
        <span>原因 <b>{{ 文本(调整详情.主表.调整原因) }}</b></span>
      </div>
      <el-table :data="调整详情.明细" size="small" border>
        <el-table-column prop="行号" label="行" width="60" />
        <el-table-column prop="商品名称" label="商品" min-width="150" />
        <el-table-column prop="批次号" label="批次" width="150" />
        <el-table-column prop="状态名称" label="状态" width="90" />
        <el-table-column prop="调整数量" label="调整" width="90" align="right" />
        <el-table-column label="成本" width="110" align="right">
          <template #default="{ row }">¥{{ 金额(row.成本单价) }}</template>
        </el-table-column>
        <el-table-column label="金额" width="120" align="right">
          <template #default="{ row }">¥{{ 金额(row.行金额) }}</template>
        </el-table-column>
      </el-table>
      <template v-if="调整详情.凭证.length">
        <div class="line-head"><span>生成的凭证</span></div>
        <el-table :data="调整详情.凭证" size="small" border>
          <el-table-column prop="凭证字号" label="字号" width="120" />
          <el-table-column label="日期" width="110">
            <template #default="{ row }">{{ 日期(row.凭证日期) }}</template>
          </el-table-column>
          <el-table-column prop="摘要" label="摘要" min-width="180" />
          <el-table-column prop="单据状态" label="状态" width="90" />
        </el-table>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.pager {
  margin-top: 12px;
  justify-content: flex-end;
}

.line-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: 14px 0 8px;
  font-weight: var(--fw-semibold);
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 10px 16px;
}

.form-item label {
  display: block;
  margin-bottom: 4px;
  font-size: var(--fs-xs);
  color: var(--c-ink-600);
}

.form-item label em {
  color: var(--c-danger-500);
  font-style: normal;
}

.form-item--wide {
  grid-column: 1 / -1;
}

.form-hint {
  margin-top: 10px;
  padding: 8px 10px;
  border-radius: var(--r-sm);
  background: var(--bg-surface-alt);
  color: var(--c-ink-400);
  font-size: var(--fs-xs);
  line-height: var(--lh-normal);
}

.form-hint b {
  color: var(--c-ink-600);
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 6px 16px;
  margin-bottom: 12px;
  font-size: var(--fs-xs);
  color: var(--c-ink-400);
}

.detail-grid b {
  color: var(--c-ink-700);
  font-weight: var(--fw-medium);
}

.field-tip {
  margin-top: 4px;
  color: var(--c-ink-400);
  font-size: var(--fs-xs);
}

.field-tip b {
  color: var(--c-ink-600);
}

.muted {
  color: var(--c-ink-300);
}

.num.ok {
  color: var(--c-success-500);
}

.num.down {
  color: var(--c-danger-500);
}
</style>

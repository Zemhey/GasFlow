<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import api from '@/api'
import { 单据表, 单据资源, 状态名, 状态色, type 字段 } from '@/config/modules'
import { useLookupStore } from '@/stores/lookup'
import { useAuthStore } from '@/stores/auth'
import PageHead from '@/components/PageHead.vue'
import DataState from '@/components/DataState.vue'
import MoneyText from '@/components/MoneyText.vue'

const route = useRoute()
const router = useRouter()
const lookup = useLookupStore()
const auth = useAuthStore()

const kind = computed(() => String(route.params.kind))
const 配置 = computed(() => 单据表[kind.value])
const id = computed(() => Number(route.params.id ?? 0))

/**
 * 按钮可见性 = 后端权限代码（批次 20 对齐，不让用户点出一个 403）：
 *   新建 → add；改已有草稿 → edit；删除草稿 → del。
 * 与后端 `OrdersController.校验权限(kind, 是否编辑 ? EDIT : ADD)` + `动态资源表.单据` 一字对应。
 * 未知 kind（配置里没有）不判权限，交给后端回 400 说明 —— 别把"参数写错"说成"没权限"。
 */
const 单据权限 = computed(() => {
  const 资源 = 单据资源[kind.value]
  if (!资源) return { 可新增: true, 可修改: true, 可删除: true, 资源: '' }
  if (!auth.有资源(资源)) return { 可新增: false, 可修改: false, 可删除: false, 资源 }
  return {
    可新增: auth.有权限(资源, 'add'),
    可修改: auth.有权限(资源, 'edit'),
    可删除: auth.有权限(资源, 'del'),
    资源
  }
})
const 可保存 = computed(() => (id.value > 0 ? 单据权限.value.可修改 : 单据权限.value.可新增))
const 无保存权限提示 = computed(() =>
  可保存.value
    ? ''
    : `没有「${单据权限.value.资源}」的${id.value > 0 ? '修改（edit）' : '新增（add）'}权限，本页只读`
)
const 主表 = ref<any>({})
const 明细 = ref<any[]>([])
const 加载中 = ref(false)
const 保存中 = ref(false)
const 带价中 = ref(false)
/** 加载失败信息：就地显示 + 可重试，不用 toast 一闪而过 */
const 加载错误 = ref('')
/** 保存前的校验/接口错误：就地显示在表单上方 */
const 校验错误 = ref('')

/** 状态：新建(-1) / 0 草稿 / 1 已提交 / 2 已审核 / 3 已过账 / 9 已冲销 */
const 当前状态 = computed<number>(() => {
  if (id.value <= 0) return -1
  // 费用单没有 单据状态（配置里 statusField = 审批状态）
  const 键 = 配置.value?.statusField ?? '单据状态'
  const v = 主表.value[键] ?? 主表.value['单据状态'] ?? 主表.value['单据状态名称']
  const n = Number(v)
  return Number.isNaN(n) ? -1 : n
})

/** 草稿（或新建）才允许改；已提交及以后只读 */
const 可编辑 = computed(() => 当前状态.value === -1 || 当前状态.value === 0)
const 只读提示 = computed(() => {
  if (可编辑.value) return ''
  if (当前状态.value === 9) return '该单已冲销，不能修改'
  return `该单已是「${状态名(当前状态.value, 配置.value)}」，不能修改；如需变更请先反过账/冲销，或复制为新单`
})

/** 表头里可编辑字段（不含隐藏的、只读的） */
const 表头字段 = computed<字段[]>(() => (配置.value?.header ?? []).filter((f) => !f.hideInForm))
const 明细字段 = computed<字段[]>(() => (配置.value?.lines ?? []).filter((f) => !f.hideInForm))

/**
 * 由明细逐行累加回填的表头字段（即下面 重算() 会写的那几个 key）。
 * 只列"从明细算出来"的字段：已收金额 / 已核销金额 / 已付金额 这类是后端回填的，
 * 不在其中 —— 所以汇总面板不会把"接口回填值"说成"明细汇总值"。
 */
const 汇总键表: Record<string, string[]> = {
  delivery: ['销量合计', '金额合计', '押金合计', '应收金额', '气重合计'],
  intake: ['金额', '实瓶合计', '空瓶合计', '气重合计公斤'],
  borrow: ['借出合计', '押金合计'],
  return: ['归还合计', '报废合计', '退押金合计'],
  warehouse: ['入库合计', '出库合计'],
  stocktake: ['实盘合计', '账面合计', '差异合计', '差异金额'],
  deposit: ['收款金额'],
  receipt: [],
  payment: [],
  expense: [],
  commission: ['数量合计', '提成合计']
}

/**
 * 「录入即记账」的单据（批次 15）：后端一次过程调用就完成 建单 → 核销 → 生成凭证，
 * 单据状态直接是 3（已过账）。所以没有草稿态、也没有「保存并提交」这一步 ——
 * 页面上要把这两个动作藏掉，否则点下去必然 409。
 */
const 一次性记账 = computed(() => ['receipt', 'payment', 'expense'].includes(kind.value))

/** 表头金额/数量汇总：由明细算出来的主表字段（只读展示，口径见页面底部） */
const 汇总字段 = computed<字段[]>(() => {
  const 键 = 汇总键表[kind.value] ?? []
  return 表头字段.value.filter((f) => 键.includes(f.key))
})

/** 表单网格里要渲染的表头字段：汇总字段单独成区，避免同一个数在页面出现两次 */
const 表单字段 = computed<字段[]>(() => 表头字段.value.filter((f) => !汇总字段.value.includes(f)))

const 明细合计 = computed(() => {
  const 金额 =
    配置.value?.lineSum?.reduce((s, c) => s + 明细.value.reduce((a, r) => a + Number(r[c.key] ?? 0), 0), 0) ?? 0
  const 数量 = 明细.value.reduce((s, r) => s + Number(r.数量 ?? r.实瓶数量 ?? r.归还数量 ?? 0), 0)
  return { 金额, 数量 }
})

/** 明细金额列的展示名：配置里 lineSum 给了就用它，否则退回"金额" */
const 明细金额名 = computed(() => 配置.value?.lineSum?.[0]?.label ?? '金额')

const 单据号码 = computed(() => 主表.value?.[配置.value?.noField ?? ''] ?? '')

/** 当前单据的计算口径（逐 kind 写清，避免"这个数从哪来"说不清） */
const 口径 = computed(() => {
  const m: Record<string, string> = {
    delivery:
      '明细「行金额 = 数量 × 单价」；表头「销售数量 / 销售金额」由明细累加，「应收金额 = 销售金额 − 优惠金额」。'
      + '保存走后端 v3 过程链：销售订单 → 加明细 → 提交 → 审核 → 下单送气单（FIFO 跨批次分配由库完成，前端不直接写表）。',
    intake:
      '明细「不含税单价 = 含税单价 ÷ (1 + 税率)」，不含税金额 / 税额由库计算列算出；表头「净重(kg) = 毛重 − 皮重」也是计算列。'
      + '保存只建到货单草稿，入库要走「质检 → 过账」（参数 biz.采购入库先质检 = 1）。',
    borrow:
      '表头「借出合计 = Σ(临时借瓶 + 长期借瓶)」「押金合计 = Σ(临时借瓶 + 长期借瓶) × 押金单价」，均按明细逐行累加。',
    return:
      '明细「退押金金额 = 归还数量 × 退押金单价」；表头「归还合计 / 报废合计 / 退押金合计」由明细累加。',
    warehouse:
      '明细「入库数量 = 新瓶入仓 + 实瓶入仓 + 客户瓶入仓 + 空瓶入仓」「成本金额 = 新瓶入仓数量 × 钢瓶成本」；表头「入库合计 / 出库合计」由明细累加。',
    stocktake:
      '明细「差异数量 = 实盘 − 账面」「差异金额 = 差异 × 成本单价」由库计算列算出；账面数量留空时，后端按「仓 × 商品 × 批次 × 状态 × 权属」从库存结存自动取。'
      + '⚠ 盘点按批次：保存后接口会返回实际选中的批次与账面数量，请核对（实盘要按批次填，不是整仓合计）。',
    deposit:
      '本单无明细行：「收款金额 = 数量 × 押金单价」，表头可自动带价（押金单价）。',
    receipt:
      '本单无明细行，也**不需要另外核销**：保存后 `usp_财务_收款` 会调 `usp_财务_记账_收款`，'
      + '按「最早到期」FIFO 自动冲该客户的未收应收单并写 `收款核销明细`，触发器再把「已核销金额」回写；'
      + '「未核销金额 = 收款金额 − 已核销金额」是库里的计算列，冲不完的部分留作预收。'
      + '一次保存同时生成凭证，单据状态直接是「已过账」。',
    payment:
      '本单无明细行，也**不需要另外核销**：保存后 `usp_财务_付款` 会按 FIFO 自动冲未付应付单并写 `付款核销明细`；'
      + '「未核销金额 = 付款金额 − 已核销金额」由库计算。一次保存同时生成凭证，单据状态直接是「已过账」。',
    expense:
      '本单无明细行：后端按「费用类型」带出默认科目，建一张费用单 + 一行明细（`不含税金额 = 费用金额 − 税额` 是库计算列），'
      + '同时置为已审批、已付并生成凭证，单据状态直接是「已过账」。公司留空时取默认有效公司。',
    commission:
      '明细「提成金额 = 数量 × 单位提成」；表头「数量合计 / 提成合计」由明细累加，「已发金额」为本页录入值。'
  }
  return m[kind.value] ?? ''
})

function 默认主表() {
  const o: any = {}
  配置.value.header.forEach((f) => {
    if (f.type === 'switch') o[f.key] = false
    else if (f.type === 'number' || f.type === 'money') o[f.key] = 0
    else o[f.key] = null
  })
  const d = new Date()
  const 今天 = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  o[配置.value.dateField] = 今天
  return o
}

function 空行() {
  const o: any = {}
  配置.value.lines?.forEach((f) => {
    if (f.type === 'number' || f.type === 'money') o[f.key] = 0
    else if (f.key === '单位') o[f.key] = '瓶'
    else o[f.key] = null
  })
  return o
}

async function 加载() {
  if (!配置.value) return
  加载中.value = true
  加载错误.value = ''
  校验错误.value = ''
  try {
    if (id.value > 0) {
      const r: any = await api.orders.详情(kind.value, id.value)
      主表.value = { ...默认主表(), ...(r?.主表 ?? {}) }
      明细.value = (r?.明细 ?? []).map((x: any) => ({ ...x }))
      重算()
    } else {
      主表.value = 默认主表()
      明细.value = []
      if (配置.value.lines) 明细.value.push(空行())
      if (配置.value.noField) {
        try {
          const r: any = await api.orders.下一个单号(kind.value)
          主表.value[配置.value.noField] = r?.no ?? ''
        } catch {
          /* 取号失败不阻塞录单 */
        }
      }
    }
  } catch (e: any) {
    加载错误.value = e?.response?.data?.message ?? `${配置.value.title}加载失败，请稍后重试`
    主表.value = {}
    明细.value = []
  } finally {
    加载中.value = false
  }
}

function 加行() {
  明细.value.push(空行())
}

function 删行(i: number) {
  明细.value.splice(i, 1)
  重算()
}

/**
 * 明细重算：
 * - 明细金额 = 计价数量 × 单价（口径与后端一致，前端只做预览，保存后以服务端为准）
 * - 表头汇总字段由明细回填，页面上只读展示
 */
function 重算() {
  const k = kind.value
  明细.value.forEach((r) => {
    if (k === 'delivery') {
      // v3：明细列是 数量 / 单价 / 行金额（v2 的 实瓶数量 / 销售单价 / 金额 已不存在）
      r.行金额 = Number(r.数量 ?? 0) * Number(r.单价 ?? 0)
    } else if (k === 'intake') {
      r.金额 = Number(r.实瓶数量 ?? 0) * Number(r.单瓶合算价格 ?? 0)
    } else if (k === 'return') {
      r.退押金金额 = Number(r.归还数量 ?? 0) * Number(r.退押金单价 ?? 0)
    } else if (k === 'warehouse') {
      r.入库数量 =
        Number(r.新瓶入仓数量 ?? 0) +
        Number(r.实瓶入仓数量 ?? 0) +
        Number(r.客户瓶入仓数量 ?? 0) +
        Number(r.空瓶入仓数量 ?? 0)
      r.成本金额 = Number(r.新瓶入仓数量 ?? 0) * Number(r.钢瓶成本 ?? 0)
    } else if (k === 'stocktake') {
      // v3：差异数量 = 实盘 − 账面（服务端由计算列算出，这里只做录入预览）
      r.差异数量 = Number(r.实盘数量 ?? 0) - Number(r.账面数量 ?? 0)
      r.差异金额 = r.差异数量 * Number(r.成本单价 ?? 0)
    } else if (k === 'commission') {
      r.提成金额 = Number(r.数量 ?? 0) * Number(r.单位提成 ?? 0)
    }
  })

  const k2 = kind.value
  const 和 = (key: string) => 明细.value.reduce((s, r) => s + Number(r[key] ?? 0), 0)
  if (k2 === 'delivery') {
    // 主表汇总由明细回填（服务端会按过程口径重算，这里只做录入预览）
    主表.value.销售数量 = 和('数量')
    主表.value.销售金额 = 和('行金额')
    主表.value.应收金额 = Number(主表.value.销售金额 ?? 0) - Number(主表.value.优惠金额 ?? 0)
  } else if (k2 === 'intake') {
    主表.value.金额 = 和('金额')
    主表.value.实瓶合计 = 和('实瓶数量')
    主表.value.空瓶合计 = 和('空瓶数量')
    主表.value.气重合计公斤 = Number(主表.value.实瓶总重公斤 ?? 0) - Number(主表.value.空瓶总重公斤 ?? 0)
  } else if (k2 === 'borrow') {
    主表.value.借出合计 = 明细.value.reduce(
      (s, r) => s + Number(r.临时借瓶数量 ?? 0) + Number(r.长期借瓶数量 ?? 0),
      0
    )
    主表.value.押金合计 = 明细.value.reduce(
      (s, r) => s + (Number(r.临时借瓶数量 ?? 0) + Number(r.长期借瓶数量 ?? 0)) * Number(r.押金单价 ?? 0),
      0
    )
  } else if (k2 === 'return') {
    主表.value.归还合计 = 和('归还数量')
    主表.value.报废合计 = 和('报废数量')
    主表.value.退押金合计 = 和('退押金金额')
  } else if (k2 === 'warehouse') {
    主表.value.入库合计 = 和('入库数量')
    主表.value.出库合计 = 和('旧瓶出仓数量')
  } else if (k2 === 'stocktake') {
    主表.value.实盘合计 = 明细.value.reduce((s, r) => s + Number(r.实盘实瓶 ?? 0) + Number(r.实盘空瓶 ?? 0), 0)
    主表.value.账面合计 = 明细.value.reduce((s, r) => s + Number(r.账面实瓶 ?? 0) + Number(r.账面空瓶 ?? 0), 0)
    主表.value.差异合计 = 和('差异数量')
    主表.value.差异金额 = 和('差异金额')
  } else if (k2 === 'deposit') {
    主表.value.收款金额 = Number(主表.value.数量 ?? 0) * Number(主表.value.押金单价 ?? 0)
  } else if (k2 === 'expense') {
    // 不含税金额 是库里的计算列（费用金额 − 税额），这里只做录入预览，不回传
    主表.value.不含税金额 = Number(主表.value.费用金额 ?? 0) - Number(主表.value.税额 ?? 0)
  } else if (k2 === 'commission') {
    主表.value.数量合计 = 和('数量')
    主表.value.提成合计 = 和('提成金额')
  }
}

/**
 * 自动带价：GET /api/orders/{kind}/price?客户编号&规格编号
 * 失败（404 / 未配价）时静默留空，不打扰录单。
 */
async function 带价(row: any, i: number) {
  if (!配置.value?.autoPrice || !可编辑.value) return
  const 规格字段 = 明细字段.value.find((f) => f.lookup === '钢瓶规格' || f.lookup === '商品')?.key
  const 价格字段 = 明细字段.value.find((f) => f.priceOf)?.key
  // 销售单据是「客户」，采购到货是「供应商」——两者都当"取价对象"传给后端
  const 客户字段 = 表头字段.value.find((f) => f.lookup === '客户' || f.lookup === '供应商')?.key
  if (!规格字段 || !价格字段) return
  const 规格编号 = row[规格字段]
  const 客户编号 = 客户字段 ? 主表.value[客户字段] : null
  if (!规格编号) return
  带价中.value = true
  try {
    const r: any = await api.orders.价格(kind.value, 客户编号, 规格编号)
    // 兼容后端可能返回的几种形态：{ 单价 } / { 价格 } / { 销售单价 } / 纯数字
    const 单价 =
      typeof r === 'number'
        ? r
        : r?.单价 ?? r?.销售单价 ?? r?.价格 ?? r?.[价格字段] ?? null
    if (单价 !== null && 单价 !== undefined && 单价 !== '') {
      if (row[价格字段] === null || row[价格字段] === undefined || Number(row[价格字段]) === 0) {
        明细.value[i][价格字段] = Number(单价)
        重算()
      }
    }
  } catch {
    /* 取价失败：留空，由录单员手工填 */
  } finally {
    带价中.value = false
  }
}

/** 客户一变，把已有明细的价格重新取一遍（只在草稿态） */
async function 客户变更() {
  if (!配置.value?.autoPrice || !可编辑.value) return
  for (let i = 0; i < 明细.value.length; i++) {
    const 价格字段 = 明细字段.value.find((f) => f.priceOf)?.key
    const 规格字段 = 明细字段.value.find((f) => f.lookup === '钢瓶规格' || f.lookup === '商品')?.key
    if (!价格字段 || !规格字段) return
    if (明细.value[i][规格字段]) {
      // 清掉旧价再取，避免沿用上一个客户的价格
      明细.value[i][价格字段] = 0
      await 带价(明细.value[i], i)
    }
  }
}

/** 必填校验：返回全部问题（表头 + 明细逐行），一次说清，不让人改一处再来一次 */
function 校验(): string[] {
  const 提示: string[] = []
  const 缺 = 表头字段.value.filter((f) => f.required && !主表.value[f.key])
  if (缺.length) 提示.push(`表头必填项未填写：${缺.map((f) => f.label).join('、')}`)
  const 坏行: number[] = []
  明细.value.forEach((r, i) => {
    if (明细字段.value.some((f) => f.required && (r[f.key] === null || r[f.key] === undefined || r[f.key] === ''))) {
      坏行.push(i + 1)
    }
  })
  if (坏行.length) 提示.push(`明细第 ${坏行.join('、')} 行未填写必填项（钢瓶规格等）`)
  return 提示
}

/**
 * 保存请求本身（POST /api/orders/{kind}）。
 * 三个动作（保存 / 保存并提交 / 保存并返回）共用同一次契约调用，避免口径分叉。
 */
async function 提交保存(): Promise<any> {
  const 提交主表 = { ...主表.value }
  // 新建时传入 0 会被后端当成主键，这里清掉
  if (!id.value || id.value <= 0) {
    const k = 配置.value?.keyField
    if (k && 提交主表[k] !== undefined) delete 提交主表[k]
  }
  return await api.orders.保存(kind.value, { 主表: 提交主表, 明细: 明细.value })
}

/** 统一的保存前置检查：返回 false 表示不该继续 */
function 可以保存(): boolean {
  if (!配置.value) return false
  if (!可保存.value) {
    ElMessage.warning(无保存权限提示.value || '当前岗位没有这张单据的录入权限')
    return false
  }
  if (!可编辑.value) {
    ElMessage.warning(只读提示.value || '当前状态不可修改')
    return false
  }
  const 提示 = 校验()
  if (提示.length) {
    校验错误.value = 提示.join('；')
    ElMessage.warning(提示[0])
    return false
  }
  校验错误.value = ''
  return true
}

async function 保存() {
  if (!可以保存()) return
  保存中.value = true
  try {
    const r: any = await 提交保存()
    const 是修改 = id.value > 0
    const 单号 = r?.单据号码 ?? 主表.value[配置.value!.noField] ?? r?.id ?? ''
    ElMessage.success(
      一次性记账.value
        ? `已记账：${单号}${r?.凭证编号 ? ` · 凭证 ${r.凭证编号}` : ''}${
            r?.已核销金额 !== undefined && r?.已核销金额 !== null
              ? ` · 已核销 ¥${Number(r.已核销金额).toFixed(2)}`
              : ''
          }`
        : `${是修改 ? '已保存修改' : '已保存'}：${单号}（状态：草稿，需提交/审核/过账后才计入库存与往来）`
    )
    if (r?.id && !是修改) router.replace(`/orders/${kind.value}/${r.id}`)
    else await 加载()
  } catch (e: any) {
    校验错误.value = e?.response?.data?.message ?? '保存失败，请检查填写内容后重试'
  } finally {
    保存中.value = false
  }
}

/** 保存并返回列表（先真正保存，再离开；状态变更在列表里做） */
async function 保存并返回() {
  if (!可以保存()) return
  保存中.value = true
  try {
    const r: any = await 提交保存()
    ElMessage.success(
      一次性记账.value
        ? `已记账：${r?.单据号码 ?? r?.id ?? ''}（已核销并生成凭证，无需再提交/审核/过账）`
        : `已保存：${r?.单据号码 ?? 主表.value[配置.value!.noField] ?? r?.id ?? ''}；请在列表中继续「提交 / 审核 / 过账」`
    )
    router.push(`/orders/${kind.value}`)
  } catch (e: any) {
    校验错误.value = e?.response?.data?.message ?? '保存失败，请检查填写内容后重试'
  } finally {
    保存中.value = false
  }
}

/** 保存并提交：保存后立即对同一张单执行提交动作（仍是列表页用的同一个提交接口） */
async function 保存并提交() {
  if (!可以保存()) return
  保存中.value = true
  try {
    const r: any = await 提交保存()
    const 目标 = Number(r?.id ?? id.value ?? 0)
    const 单号 = r?.单据号码 ?? 主表.value[配置.value!.noField] ?? 目标
    if (!目标) {
      ElMessage.warning(`已保存：${单号}，但未取到单据主键，请到列表中提交`)
      router.push(`/orders/${kind.value}`)
      return
    }
    await api.orders.操作(kind.value, 目标, 'submit')
    ElMessage.success(`已保存并提交：${单号}（状态：已提交，待审核）`)
    router.push(`/orders/${kind.value}`)
  } catch (e: any) {
    校验错误.value = e?.response?.data?.message ?? '保存或提交失败，请稍后重试'
  } finally {
    保存中.value = false
  }
}

async function 删除() {
  try {
    await ElMessageBox.confirm('确定删除这张草稿单吗？明细会一起删除。', '删除确认', { type: 'warning' })
  } catch {
    return // 用户取消：ElMessageBox 会 reject，这里吞掉避免未处理的 Promise
  }
  try {
    await api.orders.删除(kind.value, id.value)
    ElMessage.success('已删除')
    router.push(`/orders/${kind.value}`)
  } catch (e: any) {
    校验错误.value = e?.response?.data?.message ?? '删除失败，请稍后重试'
  }
}

function 返回列表() {
  router.push(`/orders/${kind.value}`)
}

/** 打印：新标签打开独立打印页（A4 正式单据，不带侧栏/表单） */
function 打印单据() {
  const href = router.resolve(`/print/orders/${kind.value}/${id.value}?print=1`).href
  window.open(href, '_blank')
}

/** 导出当前单据明细为 CSV（纯前端，不改任何接口） */
function 导出明细() {
  if (!明细.value.length) {
    ElMessage.warning('当前没有可导出的明细')
    return
  }
  const 表头 = 明细字段.value.map((f) => f.label)
  const 体 = 明细.value.map((r) =>
    明细字段.value.map((f) => `"${String(r[f.key] ?? '').replace(/"/g, '""')}"`).join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${配置.value?.title ?? kind.value}_${单据号码.value || '新建'}_明细.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${明细.value.length} 行明细`)
}

/** 键盘可达性：Ctrl/⌘ + S 保存（只在草稿/新建态生效） */
function 键盘(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
    e.preventDefault()
    if (可编辑.value && !保存中.value) void 保存()
  }
}

/** 数量/整数展示：千分位（与后端返回口径一致） */
function 数量文本(v: any): string {
  const n = Number(v ?? 0)
  return Number.isFinite(n) ? n.toLocaleString('zh-CN') : '0'
}

/** 明细列对齐：数字/金额列右对齐（等宽字体才对齐） */
function 列对齐(f: 字段): 'left' | 'right' {
  return f.type === 'money' || f.type === 'number' ? 'right' : 'left'
}

/**
 * 只有"负数有业务含义"的计算列才上红绿色（盘点差异：负=盘亏）。
 * 金额/成本这类列即使为 0 或正数也不着色，避免整列花掉。
 */
function 是差异字段(f: 字段): boolean {
  return f.key === '差异数量' || f.key === '差异金额'
}

function 列宽(f: 字段): number {
  if (f.type === 'select') return 170
  if (f.type === 'money') return 120
  if (f.type === 'number') return 110
  return 130
}

watch(() => route.fullPath, 加载)

onMounted(async () => {
  window.addEventListener('keydown', 键盘)
  await lookup.加载()
  await 加载()
})

onUnmounted(() => window.removeEventListener('keydown', 键盘))
</script>

<template>
  <div v-if="配置">
    <PageHead
      :title="`${id > 0 ? '编辑' : '新建'}${配置.title}`"
      icon="EditPen"
      :sub="`${配置.title} · 状态机：草稿 → 已提交 → 已审核 → 已过账（只有草稿可改）· Ctrl/⌘ + S 保存`"
    >
      <template #actions>
        <el-button v-if="id > 0 && !一次性记账" @click="打印单据">
          <el-icon><Printer /></el-icon> 打印
        </el-button>
        <el-button :disabled="!明细.length" @click="导出明细">
          <el-icon><Download /></el-icon> 导出明细
        </el-button>
        <el-button @click="返回列表">
          <el-icon><Back /></el-icon> 返回列表
        </el-button>
      </template>
    </PageHead>

    <DataState
      :loading="加载中"
      :error="加载错误"
      :empty="false"
      :skeleton-rows="8"
      @retry="加载"
    >
      <el-card shadow="never">
        <!-- 单据标识：单号 / 状态 / 红字 / 源单，一眼确认"我在改哪张单" -->
        <div class="row-wrap mb-3">
          <span class="doc-no num">{{ 单据号码 || '（保存后生成单号）' }}</span>
          <el-tag v-if="id > 0" :type="状态色(当前状态, 配置)" size="small" effect="light">
            {{ 状态名(当前状态, 配置) || '未定状态' }}
          </el-tag>
          <el-tag v-else type="info" size="small" effect="light">新建（保存后为草稿）</el-tag>
          <el-tag v-if="主表['是否红字']" type="danger" size="small" effect="light">红字单</el-tag>
          <el-tag v-if="主表['源单编号']" type="info" size="small" effect="light">
            源单 #{{ 主表['源单编号'] }}
          </el-tag>
          <el-tag v-if="带价中" type="warning" size="small" effect="light">
            <el-icon><Loading /></el-icon> 正在取价…
          </el-tag>
          <span class="spacer" />
          <span class="hint">
            {{ 配置.lines ? `明细 ${明细.length} 行` : '本单无明细行' }}
          </span>
        </div>

        <el-alert
          v-if="!可编辑"
          :title="只读提示"
          type="warning"
          :closable="false"
          show-icon
          class="mb-3"
        />

        <!-- 没权限时不只禁用按钮，还要说清缺的是哪个资源的哪个代码（批次 20：按钮与后端权限对齐） -->
        <el-alert
          v-if="!可保存"
          :title="无保存权限提示"
          type="error"
          :closable="false"
          show-icon
          class="mb-3"
        />

        <!-- 校验/保存失败：就地显示，带上原因，不遮挡表单 -->
        <div v-if="校验错误" class="error-panel">
          <el-icon><WarningFilled /></el-icon>
          <div class="error-panel__body">{{ 校验错误 }}</div>
          <el-button class="error-panel__retry" size="small" @click="校验错误 = ''">知道了</el-button>
        </div>

        <!-- 表头：单据级信息（客户 / 日期 / 员工 / 结算方式 / 备注） -->
        <div class="section-title mb-3">单据信息</div>
        <el-form label-width="104px" size="small" :disabled="!可编辑">
          <el-row :gutter="16">
            <el-col
              v-for="f in 表单字段"
              :key="f.key"
              :xs="24"
              :sm="f.type === 'textarea' ? 24 : 12"
              :md="f.type === 'textarea' ? 24 : 8"
              :lg="f.type === 'textarea' ? 24 : 6"
            >
              <el-form-item :label="f.label" :required="f.required">
                <!-- 单据状态：用状态标签呈现（与列表页状态色一致），不再裸露数字 -->
                <template v-if="f.key === (配置.statusField ?? '单据状态')">
                  <el-tag v-if="id > 0" :type="状态色(主表[f.key], 配置)" size="small" effect="light">
                    {{ 状态名(主表[f.key], 配置) }}
                  </el-tag>
                  <span v-else class="hint">新建单保存后即为「草稿」</span>
                </template>

                <!-- 只读汇总/回填字段：以只读文本呈现，明确"这是算出来的，不是填的" -->
                <template v-else-if="f.readonly && (f.type === 'money' || f.type === 'number')">
                  <MoneyText
                    v-if="f.type === 'money'"
                    :value="主表[f.key]"
                    prefix
                    class="readonly-value"
                  />
                  <span v-else class="num readonly-value">{{ 数量文本(主表[f.key]) }}</span>
                </template>

                <el-date-picker
                  v-else-if="f.type === 'date'"
                  v-model="主表[f.key]"
                  type="date"
                  value-format="YYYY-MM-DD"
                  style="width: 100%"
                  :disabled="!可编辑"
                  @change="重算"
                />
                <el-select
                  v-else-if="f.type === 'select' && f.lookup"
                  v-model="主表[f.key]"
                  filterable
                  clearable
                  style="width: 100%"
                  :disabled="!可编辑"
                  @change="f.lookup === '客户' ? 客户变更() : 重算()"
                >
                  <el-option
                    v-for="o in lookup.选项(f.lookup, f.lookupValue)"
                    :key="o.value"
                    :label="o.label"
                    :value="o.value"
                  />
                </el-select>
                <el-select
                  v-else-if="f.type === 'select'"
                  v-model="主表[f.key]"
                  clearable
                  style="width: 100%"
                  :disabled="!可编辑"
                  @change="重算"
                >
                  <el-option v-for="o in f.options" :key="o.value" :label="o.label" :value="o.value" />
                </el-select>
                <el-input-number
                  v-else-if="f.type === 'money'"
                  v-model="主表[f.key]"
                  :controls="false"
                  :precision="2"
                  style="width: 100%"
                  :disabled="f.readonly || !可编辑"
                  @change="重算"
                />
                <el-input-number
                  v-else-if="f.type === 'number'"
                  v-model="主表[f.key]"
                  :controls="false"
                  style="width: 100%"
                  :disabled="f.readonly || !可编辑"
                  @change="重算"
                />
                <el-switch
                  v-else-if="f.type === 'switch'"
                  v-model="主表[f.key]"
                  :disabled="f.readonly || !可编辑"
                />
                <el-input
                  v-else-if="f.type === 'textarea'"
                  v-model="主表[f.key]"
                  type="textarea"
                  :rows="2"
                  :disabled="!可编辑"
                  :placeholder="`请输入${f.label}`"
                />
                <el-input
                  v-else
                  v-model="主表[f.key]"
                  :disabled="f.readonly || !可编辑"
                  :placeholder="f.readonly ? '' : `请输入${f.label}`"
                  clearable
                />
              </el-form-item>
            </el-col>
          </el-row>
        </el-form>

        <!-- 明细：逐行录入，只读列以文本呈现（金额/数量等由本页重算） -->
        <template v-if="配置.lines">
          <div class="section-title mt-4 mb-3">
            明细
            <span class="hint">｜金额与数量列由数量 × 单价自动算出，保存后以服务端为准</span>
          </div>

          <div class="table-wrap table-scroll-hint">
            <el-table :data="明细" size="small" stripe max-height="calc(100vh - 320px)">
              <el-table-column type="index" width="46" align="center" label="#" />
              <el-table-column
                v-for="f in 明细字段"
                :key="f.key"
                :label="f.label"
                :min-width="列宽(f)"
                :align="列对齐(f)"
                :show-overflow-tooltip="f.type === 'text'"
              >
                <template #default="{ row, $index }">
                  <el-select
                    v-if="f.type === 'select' && f.lookup"
                    v-model="row[f.key]"
                    filterable
                    size="small"
                    style="width: 100%"
                    :disabled="!可编辑"
                    :placeholder="`选择${f.label}`"
                    @change="带价(row, $index)"
                  >
                    <el-option
                      v-for="o in lookup.选项(f.lookup, f.lookupValue)"
                      :key="o.value"
                      :label="o.label"
                      :value="o.value"
                    />
                  </el-select>
                  <el-select
                    v-else-if="f.type === 'select'"
                    v-model="row[f.key]"
                    size="small"
                    style="width: 100%"
                    :disabled="!可编辑"
                    @change="重算"
                  >
                    <el-option v-for="o in f.options" :key="o.value" :label="o.label" :value="o.value" />
                  </el-select>
                  <!-- 只读计算列：文本呈现，避免"看起来能填其实是灰的输入框" -->
                  <template v-else-if="f.readonly && (f.type === 'money' || f.type === 'number')">
                    <MoneyText
                      v-if="f.type === 'money'"
                      :value="row[f.key]"
                      :colored="是差异字段(f)"
                    />
                    <span
                      v-else
                      class="num"
                      :class="是差异字段(f) && Number(row[f.key] ?? 0) < 0 ? 'down' : ''"
                    >
                      {{ 数量文本(row[f.key]) }}
                    </span>
                  </template>
                  <el-input-number
                    v-else-if="f.type === 'money'"
                    v-model="row[f.key]"
                    :controls="false"
                    :precision="2"
                    size="small"
                    style="width: 100%"
                    :disabled="f.readonly || !可编辑"
                    @change="重算"
                  />
                  <el-input-number
                    v-else-if="f.type === 'number'"
                    v-model="row[f.key]"
                    :controls="false"
                    size="small"
                    style="width: 100%"
                    :disabled="f.readonly || !可编辑"
                    @change="重算"
                  />
                  <el-input
                    v-else
                    v-model="row[f.key]"
                    size="small"
                    :disabled="!可编辑"
                    :placeholder="`请输入${f.label}`"
                  />
                </template>
              </el-table-column>
              <el-table-column v-if="可编辑" label="操作" width="76" fixed="right" align="center">
                <template #default="{ $index }">
                  <el-button link type="danger" @click="删行($index)">删除</el-button>
                </template>
              </el-table-column>
            </el-table>
          </div>

          <!-- 明细合计：行数 / 数量 / 金额（金额列名取自配置 lineSum） -->
          <div class="sum-bar">
            <span>明细合计（{{ 明细.length }} 行）</span>
            <span>数量合计 <b>{{ 数量文本(明细合计.数量) }}</b></span>
            <span>
              {{ 明细金额名 }}
              <b><MoneyText :value="明细合计.金额" prefix /></b>
            </span>
            <span v-if="可编辑">
              <el-button size="small" @click="加行">
                <el-icon><Plus /></el-icon> 添加一行
              </el-button>
            </span>
            <span class="spacer" />
            <span class="hint">金额列由「数量 × 单价」实时算出，仅作录入预览</span>
          </div>
        </template>

        <!-- 表头自动汇总：由明细回填的只读字段，集中列一次便于核对 -->
        <template v-if="汇总字段.length">
          <div class="section-title mt-4 mb-3">表头汇总（由明细回填，只读）</div>
          <div class="sum-bar">
            <span v-for="f in 汇总字段" :key="f.key">
              {{ f.label }}
              <b>
                <MoneyText v-if="f.type === 'money'" :value="主表[f.key]" prefix />
                <template v-else>{{ 数量文本(主表[f.key]) }}</template>
              </b>
            </span>
          </div>
        </template>
      </el-card>

      <div class="caliber-note">
        <b>口径</b>：{{ 口径 }}
        明细里的金额列是本页 <code>重算()</code> 的<b>前端预览</b>（数据源为本页录入的数量 × 单价），
        保存时整单通过 <code>POST /api/orders/{{ kind }}</code> 提交，<b>最终数值以服务端过账重算为准</b>；
        <b>表头汇总字段</b>（{{ 汇总字段.length ? 汇总字段.map((f) => f.label).join(' / ') : '本单无表头汇总字段' }}）由明细逐行累加回填，
        接口只读取值，不在本页写入业务规则；<b>「已收金额 / 已核销金额 / 已付金额 / 已退金额 / 已还数量」等字段不由本页计算</b>，
        它们是服务端在过账/核销时回填的余额，页面只做只读展示。
        <template v-if="配置.autoPrice">
          单价列走 <code>GET /api/orders/{{ kind }}/price</code>（参数：客户编号/供应商编号 + 商品编号）自动带价，
          <b>仅在草稿态且该行单价为空/0 时写入</b>，取价失败静默留空由录单员手工填；切换客户会重取一遍。
        </template>
        单据状态：<b>0 草稿 / 1 已提交 / 2 已审核 / 3 已过账 / 9 已冲销</b>，
        只有草稿可改；「保存并提交」= 保存成功后对同一张单调用列表页同一个提交接口，
        之后仍需在列表里完成审核与过账才会影响库存与往来。
      </div>

      <!-- 底部吸底操作条：长表单滚到底也能直接保存/提交，不用回到页头找按钮 -->
      <div v-if="可编辑" class="action-bar no-print">
        <span class="row">
          <span class="hint">
            {{ 明细.length ? `明细 ${明细.length} 行` : '无明细行' }} ·
            {{ 明细金额名 }} <MoneyText :value="明细合计.金额" prefix />
          </span>
        </span>
        <span class="spacer" />
        <el-button
          v-if="id > 0"
          type="danger"
          plain
          :disabled="保存中 || !单据权限.可删除"
          @click="删除"
        >删除草稿</el-button>
        <el-button :disabled="保存中 || !可保存" @click="保存并返回">保存并返回</el-button>
        <!-- 录入即记账的单据没有草稿态：保存即核销 + 生成凭证，不存在"保存并提交" -->
        <el-button v-if="!一次性记账" :disabled="保存中 || !可保存" @click="保存并提交">保存并提交</el-button>
        <el-button type="primary" :loading="保存中" :disabled="!可保存" @click="保存">
          {{ 一次性记账 ? '保存并记账' : '保存' }}
        </el-button>
      </div>
    </DataState>
  </div>

  <!-- 未登记的单据类型：说明原因，而不是白屏 -->
  <div v-else class="empty-state">
    <div class="empty-state__icon"><el-icon><DocumentDelete /></el-icon></div>
    <div class="empty-state__title">未登记的单据类型：{{ kind }}</div>
    <div class="empty-state__desc">
      该类型还没有在 <code>src/config/modules.ts</code> 的「单据表」里登记
      header / lines 字段配置，因此无法渲染录入界面。
    </div>
    <div class="mt-2">
      <el-button @click="router.push('/dashboard')">
        <el-icon><Back /></el-icon> 返回首页
      </el-button>
    </div>
  </div>
</template>

<style scoped>
/* 单号：等宽 + 加粗，和列表页的单号链接视觉一致 */
.doc-no {
  font-weight: var(--fw-semibold);
  color: var(--c-ink-900);
}

/* 只读值（算出来的汇总/回填值）：加一点底色，和可填输入框区分开 */
.readonly-value {
  display: inline-flex;
  align-items: center;
  min-height: var(--ctl-h-sm);
  padding: 0 var(--sp-2);
  border-radius: var(--r-sm);
  background: var(--bg-surface-alt);
  border: 1px dashed var(--c-border);
  color: var(--c-ink-700);
}

:deep(.el-form-item) {
  margin-bottom: var(--sp-3);
}

/* 吸底操作条：长表单不必回到页头找保存按钮 */
.action-bar {
  position: sticky;
  bottom: 0;
  z-index: var(--z-sticky);
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  flex-wrap: wrap;
  margin-top: var(--sp-3);
  padding: var(--sp-3) var(--sp-4);
  background: var(--bg-surface);
  border: 1px solid var(--c-border-light);
  border-radius: var(--r-lg);
  box-shadow: var(--sh-lg);
}

/* 手机上按钮撑满一行，点按热区更大 */
@media (max-width: 767px) {
  .action-bar .el-button {
    flex: 1 1 auto;
    margin-left: 0;
  }

  .action-bar .row {
    width: 100%;
  }
}
</style>

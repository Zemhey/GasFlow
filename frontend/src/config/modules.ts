/**
 * 模块配置：页面、单据、档案的字段定义都在这里。
 *
 * 后端是元数据驱动的：
 *   /api/orders/{kind}      单据主表 + 明细（kind 统一用英文代码）
 *   /api/records/{table}    档案 / 字典
 *   字段 key 与后端 JSON 字段名、数据库列名一字不差（全部中文），前端只做渲染与格式，
 *   不做名称翻译 —— 加一个字段只改这里，不用改页面代码。
 */

export type 字段类型 =
  | 'text'
  | 'number'
  | 'money'
  | 'date'
  | 'datetime'
  | 'select'
  | 'switch'
  | 'textarea'

export interface 字段 {
  key: string
  label: string
  type?: 字段类型
  lookup?: string // 下拉数据源：/api/lookups 返回对象里的键名
  /**
   * 下拉取值列：默认取数据源主键（编号），需要提交"编码"这类业务键时指定
   * （如 费用类型 的 value 要传 `类型编码`，而不是 `费用类型编号`）。
   */
  lookupValue?: string
  required?: boolean
  width?: number
  readonly?: boolean
  hideInTable?: boolean
  hideInForm?: boolean
  /** 自动兜底价：明细里"自动带出单价"写回哪个字段（如 销售单价） */
  priceOf?: boolean
  /** 明细里代表"数量"的字段，用于汇总与提成计算 */
  quantityOf?: boolean
  options?: { label: string; value: any }[]
  precision?: number
}

export interface 单据配置 {
  kind: string
  title: string
  /** 单据日期字段（列表默认按它排序 / 日期筛选） */
  dateField: string
  /** 单号字段：绝大部分是"单据号码"，押金收据是"收据号码" */
  noField: string
  /** 主表主键字段（详情/修改/操作按钮都靠它定位） */
  keyField: string
  /** 主表金额字段（列表展示 / 汇总） */
  amountField?: string
  /**
   * 状态字段名（默认 `单据状态`）。
   * 例外：费用单**没有 单据状态 列** —— 它是「审批状态 + 是否已付 + 凭证编号」表达的，
   * 这里填 `审批状态`，页面就会去读那一列。
   */
  statusField?: string
  /** 状态值 → 中文（不给就用 单据状态 那套「草稿/已提交/已审核/已过账/已冲销」） */
  statusLabels?: Record<number, string>
  /** 走 /api/orders/{kind}/price 自动带价的单据 */
  autoPrice?: boolean
  header: 字段[]
  lines?: 字段[]
  /** 明细自动计算列：key 是明细里的金额列，label 是表头展示名 */
  lineSum?: { key: string; label: string }[]
}

/** 单据状态机（与 db/01_结构_B_单据.sql 完全一致：0 草稿 → 1 已提交 → 2 已审核 → 3 已过账；9 已冲销） */
export const 单据状态 = [
  { value: 0, label: '草稿', tag: 'info' as const },
  { value: 1, label: '已提交', tag: 'primary' as const },
  { value: 2, label: '已审核', tag: 'warning' as const },
  { value: 3, label: '已过账', tag: 'success' as const },
  { value: 9, label: '已冲销', tag: 'danger' as const }
]

/** 状态值 → 中文名称（后端可能给数字，也可能直接给名称字符串，这里都兜住） */
export function 状态名称(v: any): string {
  if (v === null || v === undefined || v === '') return ''
  const n = Number(v)
  if (!Number.isNaN(n) && String(v).trim() !== '') {
    return 单据状态.find((s) => s.value === n)?.label ?? String(v)
  }
  return String(v)
}

/** 状态值 → el-tag 颜色 */
export function 状态标签(v: any): 'primary' | 'success' | 'info' | 'warning' | 'danger' {
  const n = Number(v)
  return 单据状态.find((s) => s.value === n)?.tag ?? 'info'
}

/** 带单据配置的状态名称：费用单这类用自己词表的单据才不会显示成"已审核" */
export function 状态名(值: any, 配置?: 单据配置): string {
  if (值 === null || 值 === undefined || 值 === '') return ''
  const n = Number(值)
  if (配置?.statusLabels && !Number.isNaN(n) && 配置.statusLabels[n]) return 配置.statusLabels[n]
  return 状态名称(值)
}

/** 带单据配置的状态色：非标准词表时统一走 info，避免"已驳回"被涂成绿色 */
export function 状态色(值: any, 配置?: 单据配置): 'primary' | 'success' | 'info' | 'warning' | 'danger' {
  if (配置?.statusLabels) {
    const n = Number(值)
    if (n === 2) return 'success'
    if (n === 3 || n === 9) return 'danger'
    if (n === 1) return 'warning'
    return 'info'
  }
  return 状态标签(值)
}

/**
 * 配送阶段（批次 17）：跨 配送计划 / 配送任务 两张表归一化。
 * 0 计划 → 1 已转任务 → 2 已装车 → 3 在途 → 4 已回场。
 */
export const 配送阶段 = [
  { value: 0, label: '计划', tag: 'info' as const },
  { value: 1, label: '已转任务', tag: 'primary' as const },
  { value: 2, label: '已装车', tag: 'warning' as const },
  { value: 3, label: '在途', tag: 'success' as const },
  { value: 4, label: '已回场', tag: 'success' as const }
]

export function 配送阶段名称(v: any): string {
  const n = Number(v)
  return 配送阶段.find((s) => s.value === n)?.label ?? '—'
}

export function 配送阶段标签(v: any): 'primary' | 'success' | 'info' | 'warning' | 'danger' {
  const n = Number(v)
  return 配送阶段.find((s) => s.value === n)?.tag ?? 'info'
}

const 状态字段: 字段 = { key: '单据状态', label: '单据状态', type: 'number', readonly: true, width: 96 }
const 备注字段: 字段 = { key: '备注', label: '备注', type: 'textarea', hideInTable: true }
const 部门字段: 字段 = { key: '部门编号', label: '部门', type: 'select', lookup: '部门', hideInTable: true }
const 红冲原因: 字段 = { key: '红冲原因', label: '红冲原因', readonly: true, hideInTable: true }

/**
 * 单据：送气 / 进气 / 借瓶 / 还瓶 / 入仓 / 每日盘点 / 押金 / 收款 / 付款 / 费用 / 提成结算
 *
 * ⚠ 批次 7：**只有 delivery / intake / stocktake 三个 kind 在 v3 还有同名单据**。
 * ⚠ 批次 15：**receipt / payment / expense 也已是真单据**（收款单 / 付款单 / 费用单），
 *   字段已按 v3 真实列名重写，写入口走 usp_财务_收款|付款|费用。
 *   仍然只是 v2 遗留的：borrow / return / warehouse / deposit / commission —— 后端统一回 400 + 替代入口。
/**
 * 单据 kind → 权限资源对象名。
 *
 * ⚠ 批次 14 修：v3 库里的对象名是点分英文（`Sale.Delivery`），而列表页的动作判定
 * 一直拿 kind（`delivery`）去问 `有资源()` —— 恒为 false，于是**整列操作按钮全被藏掉**
 * （编辑/提交/审核/过账/冲销/删除/打印一个都不显示），单据状态机在界面上等于不可用。
 * 路由守卫（router/index.ts）也依赖这张表，所以它必须只有一份，放在配置中枢里。
 */
export const 单据资源: Record<string, string> = {
  delivery: 'Sale.Delivery',
  intake: 'Pur.Receipt',
  stocktake: 'Inv.Stocktake',
  // 批次 15：财务作业三张单（对象名与 dbo.权限资源.对象名 一字不差，写错 = 全员 403）
  receipt: 'Fin.Receivable',
  payment: 'Fin.Payable',
  expense: 'Fin.Expense'
}

export const 单据表: Record<string, 单据配置> = {
  /* ------------------------------------------------------------------ 销售 */
  delivery: {
    kind: 'delivery',
    title: '送气单',
    dateField: '送货日期',
    noField: '单据号码',
    keyField: '送气单编号',
    amountField: '销售金额',
    autoPrice: true,
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '送货日期', label: '送货日期', type: 'date', required: true, width: 120 },
      { key: '客户编号', label: '客户', type: 'select', lookup: '客户', required: true, width: 160 },
      { key: '业务员编号', label: '业务员', type: 'select', lookup: '员工', width: 110 },
      // 出库仓库：留空时后端按「客户所属网点 → 可销售实体仓（按可用库存降序）」自动选
      { key: '出库仓库编号', label: '出库仓库', type: 'select', lookup: '仓库', hideInTable: true },
      { key: '销售数量', label: '销售数量', type: 'number', readonly: true, width: 100 },
      { key: '销售金额', label: '销售金额', type: 'money', readonly: true, width: 120 },
      { key: '应收金额', label: '应收金额', type: 'money', readonly: true, width: 120 },
      { key: '优惠金额', label: '优惠金额', type: 'money', hideInTable: true },
      { key: '实收现金', label: '实收现金', type: 'money', hideInTable: true },
      { key: '挂账金额', label: '挂账金额', type: 'money', readonly: true, hideInTable: true },
      { key: '回收空瓶数', label: '回收空瓶', type: 'number', hideInTable: true },
      { key: '签收状态', label: '签收状态', readonly: true, width: 100 },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '是否已过账', label: '已过账', type: 'switch', readonly: true, width: 80 },
      { key: '备注', ...备注字段 } as 字段
    ],
    lines: [
      { key: '商品编号', label: '商品', type: 'select', lookup: '商品', required: true },
      { key: '数量', label: '数量', type: 'number', required: true, quantityOf: true },
      { key: '单价', label: '单价', type: 'money', priceOf: true },
      { key: '行金额', label: '金额', type: 'money', readonly: true },
      { key: '回收空瓶数', label: '回收空瓶', type: 'number', hideInTable: true },
      { key: '备注', label: '明细备注', type: 'text' }
    ],
    lineSum: [{ key: '行金额', label: '合计金额' }]
  },

  deposit: {
    kind: 'deposit',
    title: '押金收据',
    dateField: '收款日期',
    noField: '收据号码',
    keyField: '收据编号',
    amountField: '收款金额',
    autoPrice: true,
    header: [
      { key: '收据号码', label: '收据号码', readonly: true, width: 150 },
      { key: '收款日期', label: '收款日期', type: 'date', required: true, width: 120 },
      { key: '客户编号', label: '客户', type: 'select', lookup: '客户', required: true, width: 160 },
      { key: '规格编号', label: '钢瓶规格', type: 'select', lookup: '钢瓶规格', required: true, width: 140 },
      { key: '数量', label: '数量', type: 'number', required: true, quantityOf: true, width: 90 },
      { key: '押金单价', label: '押金单价', type: 'money', priceOf: true, width: 110 },
      { key: '收款金额', label: '收款金额', type: 'money', readonly: true, width: 120 },
      { key: '付款方式编号', label: '付款方式', type: 'select', lookup: '付款方式', hideInTable: true },
      { key: '经办员工编号', label: '经办员工', type: 'select', lookup: '员工' },
      { key: '借瓶单编号', label: '关联借瓶单', type: 'number', hideInTable: true },
      { key: '已退金额', label: '已退金额', type: 'money', readonly: true, hideInTable: true, hideInForm: true },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '部门编号', ...部门字段 } as 字段,
      { key: '备注', ...备注字段 } as 字段
    ]
  },

  /* ------------------------------------------------------------------ 采购 */
  intake: {
    kind: 'intake',
    title: '进气单',
    // v3：进气单主干已不存在，采购入库是「到货单」（列名全部按到货单）
    dateField: '到货日期',
    noField: '单据号码',
    keyField: '到货单编号',
    amountField: '净重公斤',
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '到货日期', label: '到货日期', type: 'date', required: true, width: 120 },
      { key: '供应商编号', label: '供应商', type: 'select', lookup: '供应商', required: true, width: 160 },
      { key: '收货仓库编号', label: '收货仓库', type: 'select', lookup: '仓库', hideInTable: true },
      { key: '车牌', label: '车牌', width: 110 },
      { key: '司机姓名', label: '司机', width: 100 },
      { key: '到货车次', label: '车次', type: 'number', hideInTable: true },
      { key: '过磅单号', label: '过磅单号', hideInTable: true },
      { key: '毛重公斤', label: '毛重(kg)', type: 'number', hideInTable: true },
      { key: '皮重公斤', label: '皮重(kg)', type: 'number', hideInTable: true },
      { key: '净重公斤', label: '净重(kg)', type: 'number', width: 110 },
      { key: '实收数量', label: '实收数量', type: 'number', readonly: true, width: 100 },
      { key: '入库数量', label: '入库数量', type: 'number', readonly: true, width: 100 },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '备注', ...备注字段 } as 字段
    ],
    lines: [
      { key: '商品编号', label: '商品', type: 'select', lookup: '商品', required: true },
      { key: '应收数量', label: '应收数量', type: 'number' },
      { key: '实收数量', label: '实收数量', type: 'number', required: true, quantityOf: true },
      { key: '拒收数量', label: '拒收数量', type: 'number' },
      { key: '含税单价', label: '含税单价', type: 'money', priceOf: true },
      { key: '不含税单价', label: '不含税单价', type: 'money', readonly: true },
      { key: '不含税金额', label: '不含税金额', type: 'money', readonly: true },
      { key: '税额', label: '税额', type: 'money', readonly: true },
      { key: '备注', label: '明细备注', type: 'text' }
    ],
    lineSum: [{ key: '不含税金额', label: '合计不含税金额' }]
  },

  /* ------------------------------------------------------------ 钢瓶流转 */
  borrow: {
    kind: 'borrow',
    title: '借瓶单',
    dateField: '借瓶日期',
    noField: '单据号码',
    keyField: '借瓶单编号',
    amountField: '押金合计',
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '借瓶日期', label: '借瓶日期', type: 'date', required: true, width: 120 },
      { key: '客户编号', label: '客户', type: 'select', lookup: '客户', required: true, width: 160 },
      { key: '经办员工编号', label: '经办员工', type: 'select', lookup: '员工' },
      { key: '应还日期', label: '应还日期', type: 'date', width: 120 },
      { key: '借出合计', label: '借出合计', type: 'number', readonly: true, width: 100 },
      { key: '已还数量', label: '已还数量', type: 'number', readonly: true, width: 100 },
      { key: '押金合计', label: '押金合计', type: 'money', readonly: true, width: 120 },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '是否红字', label: '红字', type: 'switch', readonly: true, width: 70, hideInForm: true },
      { key: '源单编号', label: '源单编号', type: 'number', readonly: true, hideInTable: true, hideInForm: true },
      { key: '红冲原因', ...红冲原因 } as 字段,
      { key: '部门编号', ...部门字段 } as 字段,
      { key: '备注', ...备注字段 } as 字段
    ],
    lines: [
      { key: '规格编号', label: '钢瓶规格', type: 'select', lookup: '钢瓶规格', required: true },
      { key: '临时借瓶数量', label: '临时借瓶', type: 'number', quantityOf: true },
      { key: '长期借瓶数量', label: '长期借瓶', type: 'number' },
      { key: '押金单价', label: '押金单价', type: 'money', priceOf: true },
      { key: '钢瓶单价', label: '钢瓶单价', type: 'money' },
      { key: '已还数量', label: '已还数量', type: 'number', readonly: true, hideInForm: true }
    ],
    lineSum: []
  },
  return: {
    kind: 'return',
    title: '还瓶单',
    dateField: '还瓶日期',
    noField: '单据号码',
    keyField: '还瓶单编号',
    amountField: '退押金合计',
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '还瓶日期', label: '还瓶日期', type: 'date', required: true, width: 120 },
      { key: '客户编号', label: '客户', type: 'select', lookup: '客户', required: true, width: 160 },
      { key: '经办员工编号', label: '经办员工', type: 'select', lookup: '员工' },
      { key: '归还合计', label: '归还合计', type: 'number', readonly: true, width: 100 },
      { key: '报废合计', label: '报废合计', type: 'number', readonly: true, width: 100 },
      { key: '退押金合计', label: '退押金合计', type: 'money', readonly: true, width: 120 },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '是否红字', label: '红字', type: 'switch', readonly: true, width: 70, hideInForm: true },
      { key: '源单编号', label: '源单编号', type: 'number', readonly: true, hideInTable: true, hideInForm: true },
      { key: '红冲原因', ...红冲原因 } as 字段,
      { key: '部门编号', ...部门字段 } as 字段,
      { key: '备注', ...备注字段 } as 字段
    ],
    lines: [
      { key: '规格编号', label: '钢瓶规格', type: 'select', lookup: '钢瓶规格', required: true },
      { key: '借瓶单编号', label: '关联借瓶单', type: 'number', hideInTable: true },
      { key: '归还数量', label: '归还数量', type: 'number', required: true, quantityOf: true },
      { key: '报废数量', label: '报废数量', type: 'number' },
      { key: '钢瓶单价', label: '钢瓶单价', type: 'money' },
      { key: '退押金单价', label: '退押金单价', type: 'money', priceOf: true },
      { key: '退押金金额', label: '退押金金额', type: 'money', readonly: true, hideInForm: true }
    ],
    lineSum: []
  },
  warehouse: {
    kind: 'warehouse',
    title: '入仓单',
    dateField: '入仓日期',
    noField: '单据号码',
    keyField: '入仓单编号',
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '入仓日期', label: '入仓日期', type: 'date', required: true, width: 120 },
      { key: '经办员工编号', label: '经办员工', type: 'select', lookup: '员工' },
      { key: '打气车编号', label: '打气车', type: 'select', lookup: '打气车' },
      { key: '车牌号码', label: '车牌号码', width: 120 },
      { key: '入库合计', label: '入库合计', type: 'number', readonly: true, width: 100 },
      { key: '出库合计', label: '出库合计', type: 'number', readonly: true, width: 100 },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '是否红字', label: '红字', type: 'switch', readonly: true, width: 70, hideInForm: true },
      { key: '源单编号', label: '源单编号', type: 'number', readonly: true, hideInTable: true, hideInForm: true },
      { key: '红冲原因', ...红冲原因 } as 字段,
      { key: '部门编号', ...部门字段 } as 字段,
      { key: '备注', ...备注字段 } as 字段
    ],
    lines: [
      { key: '规格编号', label: '钢瓶规格', type: 'select', lookup: '钢瓶规格', required: true },
      { key: '新瓶入仓数量', label: '新瓶入仓', type: 'number', quantityOf: true },
      { key: '实瓶入仓数量', label: '实瓶入仓', type: 'number' },
      { key: '客户瓶入仓数量', label: '客户瓶入仓', type: 'number' },
      { key: '空瓶入仓数量', label: '空瓶入仓', type: 'number' },
      { key: '旧瓶出仓数量', label: '旧瓶出仓', type: 'number' },
      { key: '入库数量', label: '入库数量', type: 'number', readonly: true },
      { key: '钢瓶成本', label: '钢瓶成本', type: 'money' },
      { key: '成本金额', label: '成本金额', type: 'money', readonly: true }
    ],
    lineSum: []
  },
  stocktake: {
    kind: 'stocktake',
    title: '每日盘点',
    // v3：每日盘点 → 库存盘点单（盘点单由仓库作业产生，本页只做查询与状态流转）
    dateField: '盘点日期',
    noField: '单据号码',
    keyField: '盘点单编号',
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '盘点日期', label: '盘点日期', type: 'date', required: true, width: 120 },
      { key: '仓库编号', label: '盘点仓库', type: 'select', lookup: '仓库', required: true, width: 140 },
      // 库里有 CK_库存盘点单_范围：全仓 / 品类 / 商品 / 抽盘。自由文本会撞约束（409），改成下拉
      {
        key: '盘点范围', label: '盘点范围', type: 'select', width: 120,
        options: [
          { label: '全仓', value: '全仓' },
          { label: '品类', value: '品类' },
          { label: '商品', value: '商品' },
          { label: '抽盘', value: '抽盘' }
        ]
      },
      { key: '盘点人编号', label: '盘点人', type: 'select', lookup: '员工', width: 110 },
      { key: '是否盲盘', label: '盲盘', type: 'switch', hideInTable: true },
      { key: '差异行数', label: '差异行数', type: 'number', readonly: true, width: 100 },
      { key: '差异数量', label: '差异数量', type: 'number', readonly: true, width: 100 },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '备注', ...备注字段 } as 字段
    ],
    lines: [
      { key: '商品编号', label: '商品', type: 'select', lookup: '商品', required: true },
      {
        key: '库存状态代码', label: '库存状态', type: 'select', width: 110,
        options: [{ label: '可用', value: 'AVAIL' }, { label: '报废', value: 'SCRAP' }, { label: '在途', value: 'TRANSIT' }]
      },
      {
        key: '权属代码', label: '权属', type: 'select', width: 100,
        options: [{ label: '自有', value: 'OWN' }, { label: '客户', value: 'CUSTOMER' }]
      },
      // 账面数量留空时，后端按「仓 × 商品 × 批次 × 状态 × 权属」从 库存结存 自动取
      { key: '账面数量', label: '账面数量', type: 'number' },
      { key: '实盘数量', label: '实盘数量', type: 'number', quantityOf: true },
      { key: '差异数量', label: '差异数量', type: 'number', readonly: true },
      { key: '差异原因', label: '差异原因', type: 'text' }
    ],
    lineSum: []
  },

  /* ------------------------------------------------------------------ 资金 */
  /**
   * 批次 15：收款单 / 付款单 / 费用单换成 **v3 真实列名**，并打通写入口。
   *
   * ⚠ 三张单都是「录入即记账」：后端一次调用过程就完成 建单 → 核销 → 生成凭证，
   *   落库后 单据状态 = 3（已过账），没有草稿/提交/审核三态。
   *   核销**不需要前端再点一次**：`usp_财务_记账_收款` 内部按「最早到期」FIFO 自动写
   *   `收款核销明细`，触发器再把 `收款单.已核销金额` / `应收单.已收金额` 回写，
   *   所以「未核销金额 = 收款金额 − 已核销金额」是**库算的**，页面只读展示。
   */
  receipt: {
    kind: 'receipt',
    title: '收款单',
    dateField: '收款日期',
    noField: '单据号码',
    keyField: '收款单编号',
    amountField: '收款金额',
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '收款日期', label: '收款日期', type: 'date', required: true, width: 120 },
      { key: '客户编号', label: '客户', type: 'select', lookup: '客户', required: true, width: 160 },
      { key: '收款金额', label: '收款金额', type: 'money', required: true, width: 120 },
      // CK_收款单_方式 只认这几个值，自由文本会撞约束（后端也再挡一次）
      {
        key: '收款方式', label: '收款方式', type: 'select', width: 110,
        options: [
          { label: '现金', value: '现金' },
          { label: '银行转账', value: '银行转账' },
          { label: '微信', value: '微信' },
          { label: '支付宝', value: '支付宝' },
          { label: '支票', value: '支票' },
          { label: '承兑', value: '承兑' },
          { label: '押金抵扣', value: '押金抵扣' },
          { label: '其他', value: '其他' }
        ]
      },
      { key: '已核销金额', label: '已核销', type: 'money', readonly: true, width: 110 },
      { key: '未核销金额', label: '未核销(预收)', type: 'money', readonly: true, width: 120, hideInTable: true },
      { key: '认领状态', label: '认领', readonly: true, width: 90, hideInTable: true },
      { key: '凭证编号', label: '凭证', type: 'number', readonly: true, width: 90 },
      { key: '经办人编号', label: '经办人', type: 'select', lookup: '员工', width: 110 },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '是否已记账', label: '已记账', type: 'switch', readonly: true, hideInTable: true },
      { key: '备注', ...备注字段 } as 字段
    ]
  },
  payment: {
    kind: 'payment',
    title: '付款单',
    dateField: '付款日期',
    noField: '单据号码',
    keyField: '付款单编号',
    amountField: '付款金额',
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '付款日期', label: '付款日期', type: 'date', required: true, width: 120 },
      { key: '供应商编号', label: '供应商', type: 'select', lookup: '供应商', required: true, width: 160 },
      { key: '付款金额', label: '付款金额', type: 'money', required: true, width: 120 },
      // CK_付款单_方式（付款没有"押金抵扣"这一项）
      {
        key: '付款方式', label: '付款方式', type: 'select', width: 110,
        options: [
          { label: '现金', value: '现金' },
          { label: '银行转账', value: '银行转账' },
          { label: '微信', value: '微信' },
          { label: '支付宝', value: '支付宝' },
          { label: '支票', value: '支票' },
          { label: '承兑', value: '承兑' },
          { label: '其他', value: '其他' }
        ]
      },
      { key: '已核销金额', label: '已核销', type: 'money', readonly: true, width: 110 },
      { key: '未核销金额', label: '未核销(预付)', type: 'money', readonly: true, width: 120, hideInTable: true },
      { key: '凭证编号', label: '凭证', type: 'number', readonly: true, width: 90 },
      { key: '经办人编号', label: '经办人', type: 'select', lookup: '员工', width: 110 },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '是否已记账', label: '已记账', type: 'switch', readonly: true, hideInTable: true },
      { key: '备注', ...备注字段 } as 字段
    ]
  },
  expense: {
    kind: 'expense',
    title: '费用单',
    dateField: '费用日期',
    noField: '单据号码',
    keyField: '费用单编号',
    amountField: '费用金额',
    // 费用单没有 单据状态 列：状态看 审批状态（过程建单时直接置 2=已批准、是否已付=1）
    statusField: '审批状态',
    statusLabels: { 0: '草稿', 1: '待审批', 2: '已批准', 3: '已驳回', 9: '已作废' },
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '费用日期', label: '费用日期', type: 'date', required: true, width: 120 },
      // value 必须是 **类型编码**（FREIGHT/OFFICE…）：usp_财务_费用 的入参就是编码，不是编号
      {
        key: '费用类型编号', label: '费用类型', type: 'select', lookup: '费用类型',
        lookupValue: '类型编码', required: true, width: 150
      },
      { key: '费用金额', label: '费用金额(含税)', type: 'money', required: true, width: 130 },
      { key: '税额', label: '其中税额', type: 'money', width: 110 },
      { key: '不含税金额', label: '不含税', type: 'money', readonly: true, width: 110, hideInForm: true },
      {
        key: '付款方式', label: '付款方式', type: 'select', width: 110,
        options: [
          { label: '现金', value: '现金' },
          { label: '银行转账', value: '银行转账' },
          { label: '微信', value: '微信' },
          { label: '支付宝', value: '支付宝' },
          { label: '支票', value: '支票' },
          { label: '承兑', value: '承兑' },
          { label: '其他', value: '其他' }
        ]
      },
      // 公司编号留空时后端取「默认有效公司」（集团总部优先），所以不设必填
      { key: '公司编号', label: '公司', type: 'select', lookup: '公司', hideInTable: true },
      { key: '报销人编号', label: '报销人', type: 'select', lookup: '员工', width: 110 },
      { key: '供应商编号', label: '供应商', type: 'select', lookup: '供应商', hideInTable: true },
      { key: '部门编号', ...部门字段 } as 字段,
      { key: '凭证编号', label: '凭证', type: 'number', readonly: true, width: 90 },
      {
        key: '审批状态', label: '审批状态', type: 'select', readonly: true, width: 100,
        options: [
          { label: '草稿', value: 0 },
          { label: '待审批', value: 1 },
          { label: '已批准', value: 2 },
          { label: '已驳回', value: 3 },
          { label: '已作废', value: 9 }
        ]
      },
      { key: '是否已付', label: '已付', type: 'switch', readonly: true, hideInTable: true },
      { key: '备注', ...备注字段 } as 字段
    ]
  },
  commission: {
    kind: 'commission',
    title: '提成结算',
    dateField: '结算期间止',
    noField: '单据号码',
    keyField: '提成结算编号',
    amountField: '提成合计',
    header: [
      { key: '单据号码', label: '单据号码', readonly: true, width: 150 },
      { key: '员工编号', label: '送瓶员', type: 'select', lookup: '员工', required: true, width: 140 },
      { key: '结算期间起', label: '结算期间起', type: 'date', required: true, width: 120 },
      { key: '结算期间止', label: '结算期间止', type: 'date', required: true, width: 120 },
      { key: '数量合计', label: '数量合计', type: 'number', readonly: true, width: 100 },
      { key: '提成合计', label: '提成合计', type: 'money', readonly: true, width: 120 },
      { key: '已发金额', label: '已发金额', type: 'money' },
      { key: '单据状态', ...状态字段 } as 字段,
      { key: '备注', ...备注字段 } as 字段
    ],
    lines: [
      { key: '送气单编号', label: '送气单编号', type: 'number' },
      { key: '送气明细编号', label: '送气明细编号', type: 'number' },
      { key: '规格编号', label: '钢瓶规格', type: 'select', lookup: '钢瓶规格', required: true },
      { key: '数量', label: '数量', type: 'number', quantityOf: true },
      { key: '单位提成', label: '单位提成', type: 'money', priceOf: true },
      { key: '提成金额', label: '提成金额', type: 'money', readonly: true }
    ],
    lineSum: [{ key: '提成金额', label: '合计提成' }]
  }
}

/** 单据代码 → 中文名（菜单、标题、面包屑都用它） */
export function 单据标题(kind: string): string {
  return 单据表[kind]?.title ?? kind
}

export interface 档案配置 {
  table: string
  title: string
  fields: 字段[]
  keywordHint?: string
}

/** 档案 / 字典：走 /api/records/{table} 通用接口 */
export const 档案表: Record<string, 档案配置> = {
  客户: {
    table: '客户',
    title: '客户管理',
    keywordHint: '编码 / 简称 / 电话',
    fields: [
      { key: '客户编码', label: '客户编码', required: true },
      { key: '客户简称', label: '客户简称', required: true },
      { key: '客户全称', label: '客户全称', required: true },
      { key: '客户类型', label: '客户类型', type: 'number' },
      { key: '联系人', label: '联系人' },
      { key: '联系电话', label: '联系电话' },
      { key: '客户地址', label: '地址' },
      { key: '业务员编号', label: '业务员', type: 'select', lookup: '员工' },
      { key: '客户分类编号', label: '客户分类', type: 'select', lookup: '客户分类' },
      { key: '客户区域编号', label: '客户区域', type: 'select', lookup: '客户区域' },
      { key: '客户状态编号', label: '客户状态', type: 'select', lookup: '客户状态' },
      { key: '计提方案编号', label: '计提方案', type: 'select', lookup: '计提方案' },
      { key: '结算方式编号', label: '结算方式', type: 'select', lookup: '结算方式' },
      { key: '付款方式编号', label: '付款方式', type: 'select', lookup: '付款方式' },
      { key: '结算账期编号', label: '结算账期', type: 'select', lookup: '结算账期' },
      { key: '价格等级', label: '价格等级', type: 'number' },
      { key: '信用额度', label: '信用额度', type: 'money' },
      { key: '是否超额预警', label: '超额预警', type: 'switch' },
      { key: '结算备注', label: '结算备注', type: 'textarea' },
      { key: '是否有效', label: '有效', type: 'switch' }
    ]
  },
  员工: {
    table: '员工',
    title: '员工管理',
    keywordHint: '工号 / 姓名',
    fields: [
      { key: '工号', label: '工号', required: true },
      { key: '姓名', label: '姓名', required: true },
      { key: '部门编号', label: '部门', type: 'select', lookup: '部门' },
      { key: '主岗位编号', label: '主岗位', type: 'select', lookup: '岗位' },
      { key: '联系电话', label: '联系电话' },
      { key: '身份证号', label: '身份证号' },
      { key: '入职日期', label: '入职日期', type: 'date' },
      { key: '离职日期', label: '离职日期', type: 'date' },
      { key: '是否在职', label: '在职', type: 'switch' }
    ]
  },
  岗位: {
    table: '岗位',
    title: '岗位管理',
    fields: [
      { key: '岗位代码', label: '岗位代码', required: true },
      { key: '岗位名称', label: '岗位名称', required: true },
      { key: '岗位描述', label: '岗位描述' },
      { key: '是否超级管理员', label: '超级管理员', type: 'switch' },
      { key: '状态', label: '启用', type: 'switch' },
      { key: '排序号', label: '排序号', type: 'number' }
    ]
  },
  部门: {
    table: '部门',
    title: '部门',
    fields: [
      { key: '部门名称', label: '部门名称', required: true },
      { key: '上级部门编号', label: '上级部门', type: 'number' },
      { key: '负责人员工编号', label: '负责人', type: 'select', lookup: '员工' },
      { key: '排序号', label: '排序号', type: 'number' },
      { key: '是否有效', label: '有效', type: 'switch' },
      { key: '备注', label: '备注', type: 'textarea' }
    ]
  },
  商品: {
    table: '商品',
    title: '商品档案',
    keywordHint: '编码 / 名称',
    fields: [
      { key: '商品编码', label: '商品编码', required: true },
      { key: '商品名称', label: '商品名称', required: true },
      { key: '品类编号', label: '品类', type: 'select', lookup: '商品品类' },
      { key: '规格型号', label: '规格型号' },
      { key: '计量单位', label: '计量单位', required: true },
      { key: '是否气品', label: '气品', type: 'switch' },
      { key: '是否销售', label: '可销售', type: 'switch' },
      { key: '是否采购', label: '可采购', type: 'switch' },
      { key: '是否钢瓶资产', label: '钢瓶资产', type: 'switch' },
      { key: '默认售价', label: '默认售价', type: 'money' },
      { key: '标准成本', label: '标准成本', type: 'money' },
      { key: '最低售价', label: '最低售价', type: 'money' },
      { key: '默认税率', label: '默认税率', type: 'number' },
      { key: '安全库存量', label: '安全库存', type: 'number' },
      { key: '排序号', label: '排序号', type: 'number' },
      { key: '是否有效', label: '有效', type: 'switch' },
      { key: '备注', label: '备注', type: 'textarea' }
    ]
  },
  商品品类: {
    table: '商品品类',
    title: '商品品类',
    fields: [
      { key: '品类编码', label: '品类编码', required: true },
      { key: '品类名称', label: '品类名称', required: true },
      { key: '上级品类编号', label: '上级品类', type: 'number' },
      { key: '是否气品', label: '气品', type: 'switch' },
      { key: '排序号', label: '排序号', type: 'number' },
      { key: '是否有效', label: '有效', type: 'switch' }
    ]
  },
  供应商: {
    table: '供应商',
    title: '供应商',
    keywordHint: '编码 / 简称 / 电话',
    fields: [
      { key: '供应商编码', label: '供应商编码', required: true },
      { key: '供应商简称', label: '简称', required: true },
      { key: '供应商全称', label: '全称', required: true },
      { key: '供应商类型', label: '类型' },
      { key: '统一社会信用代码', label: '统一社会信用代码' },
      { key: '联系人', label: '联系人' },
      { key: '联系电话', label: '联系电话' },
      { key: '地址', label: '地址' },
      { key: '结算方式', label: '结算方式' },
      { key: '结算天数', label: '结算天数', type: 'number' },
      { key: '是否月结', label: '月结', type: 'switch' },
      { key: '开户银行', label: '开户银行' },
      { key: '银行账号', label: '银行账号' },
      { key: '是否有效', label: '有效', type: 'switch' }
    ]
  },
  车辆: {
    table: '车辆',
    title: '车辆',
    fields: [
      { key: '车牌', label: '车牌', required: true },
      { key: '别名', label: '别名' },
      { key: '车辆类型', label: '车辆类型' },
      { key: '品牌型号', label: '品牌型号' },
      { key: '车仓仓库编号', label: '车仓仓库', type: 'select', lookup: '仓库' },
      { key: '默认司机员工编号', label: '默认司机', type: 'select', lookup: '员工' },
      { key: '默认跟车员工编号', label: '默认跟车', type: 'select', lookup: '员工' },
      { key: '核载瓶数', label: '核载瓶数', type: 'number' },
      { key: '核载公斤', label: '核载(kg)', type: 'number' },
      { key: '年检到期日', label: '年检到期', type: 'date' },
      { key: '保险到期日', label: '保险到期', type: 'date' },
      { key: '是否有效', label: '有效', type: 'switch' }
    ]
  },
  渠道: {
    table: '渠道',
    title: '渠道',
    fields: [
      { key: '渠道代码', label: '渠道代码', required: true },
      { key: '渠道名称', label: '渠道名称', required: true },
      { key: '渠道类型', label: '渠道类型' },
      { key: '排序号', label: '排序号', type: 'number' },
      { key: '是否有效', label: '有效', type: 'switch' }
    ]
  },
  客户分类: {
    table: '客户分类',
    title: '客户分类',
    fields: [
      { key: '分类编码', label: '分类编码', required: true },
      { key: '分类名称', label: '分类名称', required: true },
      { key: '排序号', label: '排序号', type: 'number' },
      { key: '是否有效', label: '有效', type: 'switch' }
    ]
  },
  客户区域: {
    table: '客户区域',
    title: '客户区域',
    fields: [
      { key: '区域编码', label: '区域编码', required: true },
      { key: '区域名称', label: '区域名称', required: true },
      { key: '所属网点编号', label: '所属网点', type: 'select', lookup: '经营网点' },
      { key: '排序号', label: '排序号', type: 'number' },
      { key: '是否有效', label: '有效', type: 'switch' }
    ]
  },
  客户协议价: {
    table: '客户协议价',
    title: '客户协议价',
    fields: [
      { key: '客户编号', label: '客户', type: 'select', lookup: '客户', required: true },
      { key: '商品编号', label: '商品', type: 'select', lookup: '商品', required: true },
      { key: '协议单价', label: '协议单价', type: 'money' },
      { key: '最低售价', label: '最低售价', type: 'money' },
      { key: '起订数量', label: '起订数量', type: 'number' },
      { key: '生效日期', label: '生效日期', type: 'date' },
      { key: '失效日期', label: '失效日期', type: 'date' },
      { key: '合同编号', label: '合同编号' },
      { key: '是否有效', label: '有效', type: 'switch' },
      { key: '备注', label: '备注', type: 'textarea' }
    ]
  },
  价格方案: {
    table: '价格方案',
    title: '价格方案',
    fields: [
      { key: '方案编码', label: '方案编码', required: true },
      { key: '方案名称', label: '方案名称', required: true },
      { key: '方案类型', label: '方案类型' },
      { key: '适用客户等级', label: '适用客户等级' },
      { key: '生效日期', label: '生效日期', type: 'date' },
      { key: '失效日期', label: '失效日期', type: 'date' },
      { key: '优先级', label: '优先级', type: 'number' },
      { key: '是否默认', label: '默认方案', type: 'switch' },
      { key: '是否有效', label: '有效', type: 'switch' },
      { key: '备注', label: '备注', type: 'textarea' }
    ]
  },
  字典类型: {
    table: '字典类型',
    title: '字典类型',
    fields: [
      { key: '字典类型代码', label: '字典类型代码', required: true },
      { key: '字典类型名称', label: '字典类型名称', required: true },
      { key: '说明', label: '说明' },
      { key: '是否系统', label: '系统内置', type: 'switch' },
      { key: '排序号', label: '排序号', type: 'number' }
    ]
  },
  字典项: {
    table: '字典项',
    title: '字典项',
    keywordHint: '项代码 / 项名称',
    fields: [
      { key: '字典类型编号', label: '字典类型编号', type: 'number', required: true },
      { key: '项代码', label: '项代码', required: true },
      { key: '项名称', label: '项名称', required: true },
      { key: '项值', label: '项值' },
      { key: '排序号', label: '排序号', type: 'number' },
      { key: '是否默认', label: '默认项', type: 'switch' },
      { key: '是否有效', label: '有效', type: 'switch' }
    ]
  },
  字典类型: {
    table: '字典类型',
    title: '字典类型',
    fields: [
      { key: '字典类型代码', label: '字典类型代码', required: true },
      { key: '字典类型名称', label: '字典类型名称', required: true },
      { key: '说明', label: '说明' },
      { key: '是否系统', label: '系统内置', type: 'switch' },
      { key: '排序号', label: '排序号', type: 'number' }
    ]
  }
}

/* 菜单：资源名与后端 权限资源.对象名 一致，登录后按 /api/auth/menus 过滤        */
/* ========================================================================== */

export interface 菜单项 {
  path: string
  title: string
  /** 权限资源对象名，与后端 权限资源.对象名 一致；不填表示所有登录用户可见 */
  资源?: string
  icon?: string
}

export interface 菜单组 {
  title: string
  icon: string
  /** 分组自身也占一个资源时填这里（例如"钢瓶库存"整体一个资源） */
  资源?: string
  children: 菜单项[]
}

export type 菜单节点 = 菜单项 | 菜单组

export const 菜单: 菜单节点[] = [
  { path: '/dashboard', title: '首页看板', icon: 'DataBoard', 资源: 'Rpt.Dashboard' },
  {
    title: '销售',
    icon: 'ShoppingCart',
    children: [
      { path: '/orders/delivery', title: '送气单', 资源: 'Sale.Delivery' },
      // 批次 7：押金收据（/orders/deposit）在 v3 已并入 客户押金账户，菜单改指押金台账。
      // 走通用报表页直读 vw_客户押瓶对比（押金余额 / 借出瓶数 / 档案在外瓶数 / 差异），
      // 只读、不提供"直接改余额"的入口 —— 余额由押金明细驱动，手改会与台账脱节。
      { path: '/reports/view/vw_客户押瓶对比', title: '客户押金账户', 资源: 'Fin.Receivable' }
    ]
  },
  {
    title: '配送调度',
    icon: 'Van',
    children: [
      { path: '/dispatch/tasks', title: '配送任务', 资源: 'Dist.Task' }
    ]
  },
  {
    title: '采购',
    icon: 'Van',
    children: [
      // 收款单 / 付款单在 v3 已并入应收单、应付单，入口在「采购与财务」组的应收台账 / 应付台账
      { path: '/orders/intake', title: '进气单', 资源: 'Pur.Receipt' }
    ]
  },
  {
    title: '钢瓶流转',
    icon: 'Coin',
    children: [
      // 借瓶单 / 还瓶单 / 入仓单在 v3 已并入钢瓶资产与库存单据，
      // 借还瓶看「钢瓶台账」的流转明细，入仓看到货单（采购台账）/ 盘点（库存盘点台账）
      { path: '/orders/stocktake', title: '每日盘点', 资源: 'Inv.Stocktake' },
      { path: '/ledger/cylinder', title: '钢瓶台账', 资源: 'Cyl.Archive' }
    ]
  },
  {
    title: '钢瓶库存',
    icon: 'Box',
    children: [
      { path: '/inventory/stock', title: '库存查询', 资源: 'Inv.Stock' },
      // 批次 19：库存作业（调拨 / 调整 / 冻结）。整页挂在 Inv.Transfer 资源下，
      // 调整与冻结的接口自己挂 Inv.Adjust —— 页内按钮按有权限('Inv.Adjust', …) 收窄。
      { path: '/inventory/ops', title: '库存作业', 资源: 'Inv.Transfer' },
      { path: '/inventory/ledger', title: '出入库流水', 资源: 'Inv.Ledger' },
      { path: '/inventory/reconcile', title: '库存对账', 资源: 'Inv.Stock' }
    ]
  },
  {
    title: '往来资金',
    icon: 'Wallet',
    children: [
      { path: '/partners/customers', title: '客户欠款', 资源: 'Fin.Receivable' },
      { path: '/partners/ageing', title: '账龄分析', 资源: 'Fin.Receivable' },
      { path: '/partners/suppliers', title: '供应商应付', 资源: 'Fin.Payable' }
    ]
  },
  {
    title: '基础档案',
    icon: 'Files',
    children: [
      // ⚠ 资源名必须是 v3 库里的《权限资源.对象名》点分口径（Md.* / Org.*）。
      //   v2 的中文名（base_customer 这类）在 v3 里已不存在 —— 写错 → 资源清单里查不到 → 菜单直接不显示。
      { path: '/records/客户', title: '客户管理', 资源: 'Md.Customer' },
      { path: '/records/员工', title: '员工管理', 资源: 'Md.Employee' },
      { path: '/records/商品', title: '商品档案', 资源: 'Md.Product' },
      { path: '/records/商品品类', title: '商品品类', 资源: 'Md.Category' },
      { path: '/records/供应商', title: '供应商', 资源: 'Md.Supplier' },
      { path: '/records/车辆', title: '车辆', 资源: 'Md.Vehicle' },
      { path: '/records/客户协议价', title: '客户协议价', 资源: 'Md.Price' },
      { path: '/records/价格方案', title: '价格方案', 资源: 'Md.Price' },
      { path: '/records/岗位', title: '岗位管理', 资源: 'Org.Dict' },
      { path: '/records/部门', title: '部门', 资源: 'Org.Dict' },
      { path: '/records/客户分类', title: '客户分类', 资源: 'Org.Dict' },
      { path: '/records/客户区域', title: '客户区域', 资源: 'Org.Dict' },
      { path: '/records/渠道', title: '渠道', 资源: 'Org.Dict' },
      { path: '/records/字典类型', title: '字典类型', 资源: 'Org.Dict' },
      { path: '/records/字典项', title: '字典项', 资源: 'Org.Dict' }
    ]
  },
  {
    title: '计提与价格',
    icon: 'Money',
    children: [
      // 计提：v2 的「计提方案 / 客户提成标准」在 v3 已由 提成规则 + 提成规则阶梯 取代；
      //       那两张表 v3 里没有，放进通用档案页只会报「表不存在」，所以先只留报表入口。
      { path: '/reports/commission', title: '计提汇总', 资源: 'Rpt.Commission' }
    ]
  },
  {
    title: '采购与财务',
    icon: 'Money',
    children: [
      { path: '/ledger/purchase', title: '采购台账', 资源: 'Pur.Receipt' },
      // 批次 15：收付款与费用三张单的录入口（录入即核销 + 生成凭证，没有草稿态）
      { path: '/orders/receipt', title: '收款单', 资源: 'Fin.Receivable' },
      { path: '/orders/payment', title: '付款单', 资源: 'Fin.Payable' },
      { path: '/orders/expense', title: '费用单', 资源: 'Fin.Expense' },
      { path: '/ledger/receivable', title: '应收台账', 资源: 'Fin.Receivable' },
      { path: '/ledger/payable', title: '应付台账', 资源: 'Fin.Payable' },
      { path: '/ledger/voucher', title: '凭证台账', 资源: 'Fin.Voucher' },
      { path: '/ledger/subject', title: '科目余额', 资源: 'Fin.Period' }    ]
  },
  {
    title: '经营台账',
    icon: 'Tickets',
    children: [
      // 钢瓶台账已上移到「钢瓶流转」组（那里才是它的语义位置），此处不再重复放一份
      { path: '/ledger/delivery', title: '配送台账', 资源: 'Dist.Task' },
      { path: '/ledger/stocktake', title: '库存盘点台账', 资源: 'Inv.Ledger' },
      { path: '/ledger/commission', title: '提成台账', 资源: 'Hr.Commission' },
      { path: '/ledger/payroll', title: '薪资台账', 资源: 'Hr.Payroll' }
    ]
  },
  {
    title: '报表',
    icon: 'Document',
    children: [
      { path: '/reports/center', title: '报表中心', 资源: 'Rpt.Report' },
      { path: '/reports/statement', title: '客户对账单', 资源: 'Sale.Statement' },
      { path: '/reports/supplier', title: '供气商对账', 资源: 'Pur.Order' },
      { path: '/reports/delivery-list', title: '配送清单', 资源: 'Dist.Task' },
      { path: '/reports/alerts', title: '客户预警', 资源: 'Md.Customer' },
      { path: '/reports/stock-diff', title: '盘点差异', 资源: 'Inv.Stocktake' }
    ]
  },
  {
    title: '系统',
    icon: 'Setting',
    children: [
      { path: '/system/permissions', title: '权限矩阵', 资源: 'Sys.User' },
      // 期间管理（批次 16）：月结 / 反月结 / 结转损益 / 成本结转，资源名沿用 v3 的 Fin.Period
      { path: '/system/periods', title: '期间管理', 资源: 'Fin.Period' },
      { path: '/system/logs', title: '登录日志', 资源: 'Sys.Log' },
      { path: '/system/oplogs', title: '操作日志', 资源: 'Sys.Audit' },
      { path: '/system/params', title: '系统参数', 资源: 'Org.Param' },
      { path: '/system/password', title: '修改密码' }
    ]
  }
]

/** 展平菜单项（路由守卫、按资源过滤都用它） */
export function 展平菜单(节点: 菜单节点[], 前缀 = ''): (菜单项 & { 分组?: string })[] {
  const 结果: (菜单项 & { 分组?: string })[] = []
  节点.forEach((n) => {
    if ('children' in n && n.children) {
      结果.push(...展平菜单(n.children, n.title))
    } else if ('path' in n) {
      结果.push(前缀 ? { ...n, 分组: 前缀 } : { ...n })
    }
  })
  return 结果
}

/** 路径 → 菜单项 */
export const 菜单索引: Record<string, 菜单项 & { 分组?: string }> = Object.fromEntries(
  展平菜单(菜单).map((m) => [m.path, m])
)

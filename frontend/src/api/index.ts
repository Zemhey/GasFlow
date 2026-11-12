import http from './http'

/**
 * 后端契约：成功直接返回业务对象；失败返回 HTTP 4xx/5xx + { message }。
 * 统一响应体的解包已在 api/http.ts 的拦截器里完成，这里只声明类型与方法。
 */

export interface 分页<T> {
  total: number
  items: T[]
}

export type 行数据 = Record<string, any>

/** 单据动作：提交 / 审核 / 过账 / 反过账 */
export type 单据动作 = 'submit' | 'approve' | 'post' | 'unpost'

/* ------------------------------------------------------------------ 认证 */
export interface 登录结果 {
  员工编号: number
  工号: string
  姓名: string
  部门名称?: string
  岗位名称?: string
  必须改密?: boolean
  资源: string[]
  /** 可选：后端若返回则形如 { 资源对象名: ['view','edit',...] } */
  权限?: Record<string, string[]>
}

export interface 菜单资源行 {
  资源: string
  名称: string
  路由: string
  模块: string
  排序号: number
}

/* ------------------------------------------------------------------ 单据 */
export interface 单据主键结果 {
  id: number
  单据号码: string
}

/* ------------------------------------------------------------------ 库存 */
export interface 库存行 {
  规格编号: number
  规格名称: string
  仓库: string
  实瓶数量: number
  空瓶数量: number
  在借瓶数: number
  报废数量: number
  库存金额: number
}

export interface 流水行 {
  业务日期: string
  规格名称: string
  业务类型名称: string
  实瓶变动: number
  空瓶变动: number
  单据类型: string
  单据号码: string
  客户: string
  供气商: string
  备注: string
}

export interface 对账明细行 {
  规格名称: string
  期初: number
  入库: number
  出库: number
  借出: number
  归还: number
  期末: number
  账面: number
  盘点实盘: number
  差异: number
}

/* ---------------------------------------------------------- 库存作业（批次 19） */
/**
 * 五维结存一行（仓库 × 商品 × 批次 × 权属）：**库存状态转成列**，
 * 所以「可用 / 冻结 / 在途 / 合计」是同一行的四个数，合计 = 四个状态之和。
 * 可用 = 该五维可用状态的数量 − 锁定量（锁定量由订单/配送占用，不在这里改）。
 */
export interface 结存行 {
  仓库编号: number
  仓库名称: string
  仓库类型: string
  是否车仓: boolean
  网点名称: string
  商品编号: number
  商品编码: string
  商品名称: string
  计量单位: string
  批次编号: number
  批次号: string
  到期日期: string | null
  剩余数量: number
  权属代码: string
  权属名称: string
  可用结存: number
  锁定量: number
  可用: number
  冻结: number
  在途: number
  待检: number
  报废: number
  合计: number
  成本单价: number
  成本金额: number
}

export interface 结存结果 {
  total: number
  items: 结存行[]
  合计行: 行数据
}

/* ---------------------------------------------------------------- 钢瓶 */
export interface 钢瓶统计 {
  总数: number
  在库: number
  在途: number
  在用: number
  借出: number
  报检: number
  报废: number
  遗失: number
  逾期未检: number
}

export interface 钢瓶详情 {
  档案: 行数据
  检验记录: 行数据[]
  轨迹: 行数据[]
}

/* ---------------------------------------------------------------- 往来 */
export interface 客户欠款行 {
  客户编号: number
  客户编码: string
  客户简称: string
  联系电话: string
  业务员: string
  区域: string
  应收金额: number
  押金金额: number
  在借瓶数: number
  信用额度: number
  最后交易日期: string
}

export interface 账龄行 {
  单据号码: string
  送气日期: string
  金额合计: number
  已收金额: number
  未收金额: number
  账龄天数: number
}

/* ---------------------------------------------------------------- 报表 */
export interface 客户对账单 {
  客户: 行数据
  起: string
  止: string
  期初应收: number
  明细: {
    日期: string
    单据号码: string
    规格: string
    数量: number
    单位: string
    单价: number
    金额: number
    空瓶数量: number
  }[]
  合计: { 单数: number; 金额: number; 销量: number }
  期末应收: number
}

export interface 盘点差异行 {
  盘点日期: string
  盘点区域: string
  规格: string
  账面: number
  实盘: number
  差异: number
  差异金额: number
  差异原因: string
}

/* ---------------------------------------------------------------- 看板 */
export interface 看板数据 {
  截止: string
  今日: { 单数: number; 金额: number; 销量: number }
  本月: { 单数: number; 金额: number; 销量: number }
  进气: { 单数: number; 金额: number }
  库存: { 实瓶: number; 空瓶: number; 在借: number; 库存金额: number }
  客户: { 总数: number; 活跃数: number }
  应收: { 总额: number; 逾期90天: number }
  趋势: { 日期: string; 单数: number; 金额: number }[]
  规格占比: { 规格: string; 数量: number }[]
}

/* ---------------------------------------------------------------- 结账 */
/** 凭证台账一行（凭证主表口径）。可红冲/可过账 由后端按库规则算好 */
export interface 凭证行 {
  凭证编号: number
  凭证字号: string
  凭证日期: string
  期间编号: number
  期间名称: string
  /** 0 未结账 / 1 已结账 —— 决定本期还能不能动凭证 */
  期间状态: number
  业务类型代码: string
  摘要: string
  借方合计: number
  贷方合计: number
  分录行数: number
  /** 0 草稿 / 2 已记账 / 9 已作废 */
  单据状态: number
  是否冲销: boolean
  原凭证编号: number | null
  冲销凭证编号: number | null
  制单人: string
  过账人: string
  制单时间: string | null
  过账时间: string | null
  可红冲: number
  可过账: number
  /** 不能红冲的具体原因（可红冲=0 时有值） */
  红冲受阻原因: string
}

/** 凭证分录（读 vw_凭证分录，名称类列已在视图里解析好） */
export interface 凭证分录行 {
  行号: number
  摘要: string
  科目编码: string
  科目名称: string
  科目类别: string
  借方金额: number
  贷方金额: number
  客户: string
  供应商: string
  员工: string
  商品: string
  数量: number | null
  单价: number | null
}

/** 录入页提交的一行分录 */
export interface 凭证分录入参 {
  科目编码: string
  借贷方向: '借' | '贷'
  金额: number
  摘要?: string
}

export interface 科目行 {
  科目编号: number
  科目编码: string
  科目名称: string
  科目类别: string
  是否损益科目: boolean
  借贷方向: string
}

/** 会计期间 + 四个动作的合法性（后端按过程/触发器的规则算好） */
export interface 期间行 {
  期间编号: number
  年度: number
  月份: number
  期间名称: string
  开始日期: string
  结束日期: string
  期间状态: number
  是否结转损益: boolean
  结账人: string
  结账时间: string | null
  台账张数: number
  已记账张数: number
  草稿张数: number
  已记账借方: number
  已记账贷方: number
  借贷差: number
  成本结转行数: number
  成本结转已结账行数: number
  后续已结账数: number
  可月结: number
  可反月结: number
  可结转: number
  月结受阻原因: string
}

/* ---------------------------------------------------------------- 系统 */
export interface 权限资源行 {
  资源编号: number
  资源名称: string
  资源类型: string
  对象名: string
  路由: string
  模块: string
  排序号: number
}

export interface 权限项行 {
  权限编号: number
  权限代码: string
  权限名称: string
}

export interface 权限矩阵 {
  岗位: { 岗位编号: number; 岗位名称: string }[]
  资源: 权限资源行[]
  权限: 权限项行[]
  授权: { 岗位编号: number; 资源编号: number; 权限编号: number }[]
}

export interface 系统参数行 {
  参数名: string
  参数值: string
  参数类型: string
  参数分组: string
  参数说明: string
}

/* ===================================================================== */
/* 接口                                                                */
/* ===================================================================== */

export const api = {
  /* ---------------------------------------------------------------- 认证 */
  auth: {
    登录: (工号: string, 密码: string) => http.post<never, 登录结果>('/auth/login', { 工号, 密码 }),
    退出: () => http.post<never, void>('/auth/logout'),
    当前用户: () => http.get<never, 登录结果>('/auth/me'),
    改密码: (旧密码: string, 新密码: string) =>
      http.post<never, void>('/auth/password', { 旧密码, 新密码 }),
    菜单: () =>
      http.get<never, { 菜单: 菜单资源行[]; 路由?: Record<string, string> }>('/auth/menus')
  },

  /* ------------------------------------------------------- 下拉数据源 */
  /** 一次性取全：键是数据源名（客户 / 员工 / 商品 / 供应商 ...），值是行数组 */
  lookups: () => http.get<never, Record<string, 行数据[]>>('/lookups'),

  /* ---------------------------------------------------------------- 档案 */
  /**
   * 档案通用 CRUD。**批次 3 时缺的就是这个命名空间** —— 档案页 14 个菜单点开只显示
   * "档案列表加载失败"，浏览器却根本没有发出 /api/records 请求（api.records 是 undefined，
   * 异常被 try/catch 吞成错误面板，所以 console 里连报错都看不到）。
   * 后端 RecordsController 一直是完整的，缺的只是前端这一层封装。
   */
  records: {
    列表: (table: string, params: 行数据) =>
      http.get<never, 分页<行数据>>(`/records/${table}`, { params }),
    详情: (table: string, id: number | string) =>
      http.get<never, 行数据>(`/records/${table}/${id}`),
    新增: (table: string, body: 行数据) =>
      http.post<never, { id: number }>(`/records/${table}`, body),
    修改: (table: string, id: number | string, body: 行数据) =>
      http.put<never, { ok: true }>(`/records/${table}/${id}`, body),
    删除: (table: string, id: number | string) =>
      http.delete<never, void>(`/records/${table}/${id}`)
  },

  /* ---------------------------------------------------------------- 单据 */
  orders: {
    列表: (kind: string, params: 行数据) =>
      http.get<never, 分页<行数据>>(`/orders/${kind}`, { params }),
    详情: (kind: string, id: number) =>
      http.get<never, { 主表: 行数据; 明细: 行数据[] }>(`/orders/${kind}/${id}`),
    下一个单号: (kind: string) => http.get<never, { no: string }>(`/orders/${kind}/next-no`),
    /** 自动带价：返回单价；silent=true 时失败不弹提示（录单页要求"失败留空不报错"） */
    价格: (kind: string, 客户编号: number | null | undefined, 规格编号: number | null | undefined) =>
      http.get<never, 行数据>(`/orders/${kind}/price`, {
        params: { 客户编号, 规格编号 },
        silent: true
      } as 行数据),
    保存: (kind: string, body: { 主表: 行数据; 明细: 行数据[] }) =>
      http.post<never, 单据主键结果>(`/orders/${kind}`, body),
    /** 提交 / 审核 / 过账 / 反过账 */
    操作: (kind: string, id: number, 动作: 单据动作) =>
      http.post<never, { ok: true }>(`/orders/${kind}/${id}/${动作}`),
    /** 整单冲销：生成红字单 */
    冲销: (kind: string, id: number, 原因: string) =>
      http.post<never, 单据主键结果>(`/orders/${kind}/${id}/reverse`, { 原因 }),
    删除: (kind: string, id: number) => http.delete<never, void>(`/orders/${kind}/${id}`)
  },

  /* ---------------------------------------------------------------- 配送 */
  /**
   * 配送调度（批次 17）：一行 = 一趟配送（计划 + 可选任务）。
   * 动作全部走后端 DispatchController → usp_配送_*，前端不直接拼单据。
   */
  dispatch: {
    列表: (params: 行数据) =>
      http.get<never, 分页<行数据>>('/orders/dispatch', { params }),
    选项: () => http.get<never, { 路线: 行数据[] }>('/orders/dispatch/options'),
    计划详情: (id: number) => http.get<never, 行数据>(`/orders/dispatch/plans/${id}`),
    任务详情: (id: number) => http.get<never, 行数据>(`/orders/dispatch/tasks/${id}`),
    生成计划: (body: 行数据) => http.post<never, 行数据>('/orders/dispatch/plan', body),
    转任务: (计划编号: number) =>
      http.post<never, 行数据>(`/orders/dispatch/plans/${计划编号}/convert`),
    装车: (任务编号: number) =>
      http.post<never, 行数据>(`/orders/dispatch/tasks/${任务编号}/load`),
    出车: (任务编号: number) =>
      http.post<never, 行数据>(`/orders/dispatch/tasks/${任务编号}/out`),
    回场: (任务编号: number) =>
      http.post<never, 行数据>(`/orders/dispatch/tasks/${任务编号}/return`),
    /** 探针安全门：确认后端连的是哪个库（只读） */
    库信息: () => http.get<never, { 库: string; 服务器: string; 任务数: number }>('/orders/dispatch/health')
  },

  /* --------------------------------------------------------------- 结账 */
  /**
   * 财务结账（批次 16）：手工凭证 / 过账 / 红冲 / 期间管理。
   *
   * 约定：**合法性以后端返回的 可红冲 / 可过账 / 可月结 / 可反月结 为准**，
   * 前端不自己按状态码推断 —— 那几个标志是照着库里的过程和触发器算出来的
   * （期间锁定、已红冲、借贷平衡），前端再猜一遍必然与库漂移。
   */
  finance: {
    凭证列表: (params: 行数据) => http.get<never, 分页<凭证行>>('/finance/vouchers', { params }),
    凭证详情: (id: number) =>
      http.get<never, { 主表: 凭证行; 分录: 凭证分录行[] }>(`/finance/vouchers/${id}`),
    新建凭证: (body: { 凭证日期: string; 摘要?: string; 分录: 凭证分录入参[] }) =>
      http.post<never, { 凭证编号: number; 凭证字号: string; 借方合计: number; 贷方合计: number }>(
        '/finance/vouchers',
        body
      ),
    凭证过账: (id: number) => http.post<never, { ok: true; 凭证: 行数据 }>(`/finance/vouchers/${id}/post`),
    凭证红冲: (id: number, 原因: string) =>
      http.post<never, { ok: true; 新凭证编号: number; 新凭证字号: string; 摘要: string }>(
        `/finance/vouchers/${id}/reverse`,
        { 原因 }
      ),
    科目: () => http.get<never, 科目行[]>('/finance/subjects'),
    期间: () =>
      http.get<never, { 合计: number; 已结账数: number; 未结账数: number; items: 期间行[] }>(
        '/finance/periods'
      ),
    /** 月结内部要跑成本结转 + 结转损益 + 试算，别用默认 30s 超时把它掐断 */
    月结: (id: number) =>
      http.post<never, 行数据>(`/finance/periods/${id}/close`, null, { timeout: 180000 }),
    反月结: (id: number) => http.post<never, 行数据>(`/finance/periods/${id}/reopen`),
    结转损益: (id: number) =>
      http.post<never, 行数据>(`/finance/periods/${id}/close-pl`, null, { timeout: 120000 }),
    成本结转: (id: number) =>
      http.post<never, 行数据>(`/finance/periods/${id}/close-cost`, null, { timeout: 180000 })
  },

  /* ---------------------------------------------------------------- 库存 */  inventory: {
    库存: () => http.get<never, 库存行[]>('/inventory/stock'),
    流水: (params: 行数据) =>
      http.get<never, 分页<流水行>>('/inventory/ledger', { params }),
    对账: (日期: string) =>
      http.get<never, { 明细: 对账明细行[]; 合计: 行数据 }>('/inventory/reconcile', {
        params: { 日期 }
      }),
    /**
     * 库存作业（批次 19）：数量账只能由 usp_库存_* 过程改，所以这里**只有"建单 + 动作"**，
     * 没有任何"改结存"的接口。调拨=出调出仓+入调入仓；调整=正数入/负数出并生成凭证；
     * 冻结=同一五维的状态迁移（可用→冻结，合计不变）。
     */
    结存: (params: 行数据) => http.get<never, 结存结果>('/inventory/ops/balance', { params }),
    调拨列表: (params: 行数据) =>
      http.get<never, 分页<行数据>>('/inventory/ops/transfers', { params }),
    调拨详情: (id: number) =>
      http.get<never, { 主表: 行数据; 明细: 行数据[]; 在途: 行数据[] }>(
        `/inventory/ops/transfers/${id}`
      ),
    建调拨单: (body: 行数据) => http.post<never, 行数据>('/inventory/ops/transfer', body),
    过账调拨: (id: number) => http.post<never, 行数据>(`/inventory/ops/transfers/${id}/post`),
    到达调拨: (id: number) => http.post<never, 行数据>(`/inventory/ops/transfers/${id}/arrive`),
    调整列表: (params: 行数据) =>
      http.get<never, 分页<行数据>>('/inventory/ops/adjusts', { params }),
    调整详情: (id: number) =>
      http.get<never, { 主表: 行数据; 明细: 行数据[]; 凭证: 行数据[] }>(
        `/inventory/ops/adjusts/${id}`
      ),
    建调整单: (body: 行数据) => http.post<never, 行数据>('/inventory/ops/adjust', body),
    过账调整: (id: number) => http.post<never, 行数据>(`/inventory/ops/adjusts/${id}/post`),
    冻结列表: (params: 行数据) =>
      http.get<never, 分页<行数据>>('/inventory/ops/freezes', { params }),
    建冻结单: (body: 行数据) => http.post<never, 行数据>('/inventory/ops/freeze', body),
    解冻: (id: number) => http.post<never, 行数据>(`/inventory/ops/freezes/${id}/unfreeze`),
    /** 探针自检用：这个端口连的是哪个库 */
    作业库名: () => http.get<never, 行数据>('/inventory/ops/db')
  },

  /* ---------------------------------------------------------------- 钢瓶 */
  /**
   * 钢瓶一物一码（批次 18）。建档 / 检验 / 报废 / 遗失 / 移动全部走后端过程；
   * 本页只负责采集与展示，不直插档案、不改历史检验记录。
   */
  cylinders: {
    列表: (params: 行数据) =>
      http.get<never, { total: number; items: 行数据[]; 统计: 钢瓶统计 }>('/cylinders', { params }),
    详情: (id: number) => http.get<never, 钢瓶详情>(`/cylinders/${id}`),
    批量建档: (body: 行数据) =>
      http.post<never, { ok: true; 数量: number; 钢瓶: 行数据[] }>('/cylinders/batch', body),
    检验: (id: number, body: 行数据) =>
      http.post<never, 行数据>(`/cylinders/${id}/inspect`, body),
    报废: (id: number, body: 行数据) =>
      http.post<never, 行数据>(`/cylinders/${id}/scrap`, body),
    遗失: (id: number, body: 行数据) =>
      http.post<never, 行数据>(`/cylinders/${id}/lost`, body),
    移动: (id: number, body: 行数据) =>
      http.post<never, 行数据>(`/cylinders/${id}/move`, body)
  },

  /* ---------------------------------------------------------------- 往来 */
  partners: {
    客户: (params: 行数据) =>
      http.get<never, 分页<客户欠款行>>('/partners/customers', { params }),
    账龄: (客户编号: number) =>
      http.get<never, { 明细: 账龄行[]; 合计: 行数据 }>('/partners/ageing', {
        params: { 客户编号 }
      }),
    供应商: () => http.get<never, 行数据[]>('/partners/suppliers')
  },

  /* ---------------------------------------------------------------- 报表 */
  reports: {
    客户对账单: (params: { 客户编号: number; from: string; to: string }) =>
      http.get<never, 客户对账单>('/reports/customer-statement', { params }),
    供气商对账单: (params: { 供气商编号: number; from: string; to: string }) =>
      http.get<never, 行数据>('/reports/supplier-statement', { params }),
    计提: (params: { from: string; to: string; 员工编号?: number }) =>
      http.get<never, { 汇总: 行数据[]; 明细: 行数据[] }>('/reports/commission', { params }),
    配送清单: (params: { 日期: string; 员工编号?: number }) =>
      http.get<never, { 日期: string; rows: 行数据[] }>('/reports/delivery-list', { params }),
    预警: (天数: number) =>
      http.get<never, { 全部: 行数据[]; 风险: 行数据[]; 阈值: number }>('/reports/alerts', {
        params: { 天数 }
      }),
    盘点差异: (params: { from: string; to: string }) =>
      http.get<never, { rows: 盘点差异行[] }>('/reports/stocktake-diff', { params })
  },

  /* ---------------------------------------------------- 报表中心（v3 视图） */
  /**
   * 报表中心：直接读库里 120 个 `vw_*` 视图。
   * 后端只做"列目录 + 分页取数"，口径永远在库里 —— 前端不写死任何字段。
   * 返回的 `列[{列,型}]` / `日期列` / `排序列` 是渲染表格与筛选的依据。
   */
  views: {
    清单: () =>
      http.get<never, { 合计: number; 分组: { 域: string; 项: { 视图: string; 域: string; 列数: number }[] }[] }>(
        '/views'
      ),
    数据: (view: string, params: 行数据) =>
      http.get<never, { view: string; total: number; items: 行数据[]; 列: { 列: string; 型: string }[]; 排序列: string; 日期列: string }>(
        `/views/${encodeURIComponent(view)}`,
        { params }
      )
  },

  /* ---------------------------------------------------------------- 系统 */
  system: {
    权限矩阵: () => http.get<never, 权限矩阵>('/system/permissions/matrix'),
    保存权限: (
      岗位编号: number,
      授权: { 资源编号: number; 权限编号: number }[]
    ) => http.post<never, void>(`/system/permissions/${岗位编号}`, 授权),
    登录日志: (params: { page: number; size: number }) =>
      http.get<never, 分页<行数据>>('/system/login-logs', { params }),
    操作日志: (params: { page: number; size: number; 操作类型?: string }) =>
      http.get<never, 分页<行数据>>('/system/oplogs', { params }),
    参数: () => http.get<never, 系统参数行[]>('/system/params'),
    保存参数: (body: 系统参数行[]) => http.post<never, void>('/system/params', body),
    健康检查: () => http.get<never, 行数据>('/system/health')
  },

  /* ---------------------------------------------------------------- 看板 */
  dashboard: () => http.get<never, 看板数据>('/dashboard')
}

export default api

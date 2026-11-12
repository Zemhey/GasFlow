import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { 菜单索引, 单据资源 } from '@/config/modules'

const routes = [
  { path: '/login', component: () => import('@/views/Login.vue'), meta: { public: true, title: '登录' } },
  /**
   * 单据打印（批次 14）：**放在 MainLayout 之外** —— 打印页不该带侧栏和顶栏，
   * 否则 @media print 总要跟外壳斗智斗勇。数据仍走 `/api/orders/{kind}/{id}`。
   */
  {
    path: '/print/orders/:kind/:id',
    component: () => import('@/views/OrderPrint.vue'),
    meta: { title: '单据打印' }
  },
  {
    path: '/',
    component: () => import('@/layouts/MainLayout.vue'),
    children: [
      { path: '', redirect: '/dashboard' },
      { path: 'dashboard', component: () => import('@/views/Dashboard.vue'), meta: { title: '首页看板' } },

      /* 单据：10 种 kind，同一个通用页面 */
      { path: 'orders/:kind', component: () => import('@/views/Orders.vue'), meta: { title: '单据列表' } },
      { path: 'orders/:kind/:id', component: () => import('@/views/OrderEdit.vue'), meta: { title: '单据编辑' } },

      /* 配送调度（批次 17）：列表 + 计划/任务详情 */
      { path: 'dispatch/tasks', component: () => import('@/views/dispatch/DispatchTasks.vue'), meta: { title: '配送任务' } },
      { path: 'dispatch/tasks/:id', component: () => import('@/views/dispatch/DispatchDetail.vue'), meta: { title: '配送任务详情' } },
      { path: 'dispatch/plans/:id', component: () => import('@/views/dispatch/DispatchDetail.vue'), meta: { title: '配送计划详情' } },

      /* 档案 / 字典 */
      { path: 'records/:table', component: () => import('@/views/Records.vue'), meta: { title: '档案维护' } },

      /* 钢瓶库存 */
      { path: 'inventory/stock', component: () => import('@/views/inventory/Stock.vue'), meta: { title: '库存查询' } },
      { path: 'inventory/ledger', component: () => import('@/views/inventory/Ledger.vue'), meta: { title: '出入库流水' } },
      { path: 'inventory/reconcile', component: () => import('@/views/inventory/Reconcile.vue'), meta: { title: '库存对账' } },
      /* 批次 19：库存作业（调拨/调整/冻结）—— 数量账只能通过这三类单据 + 库过程改 */
      { path: 'inventory/ops', component: () => import('@/views/inventory/Ops.vue'), meta: { title: '库存作业' } },

      /* 往来资金 */
      { path: 'partners/customers', component: () => import('@/views/partners/Customers.vue'), meta: { title: '客户欠款' } },
      { path: 'partners/ageing', component: () => import('@/views/partners/Ageing.vue'), meta: { title: '账龄分析' } },
      { path: 'partners/suppliers', component: () => import('@/views/partners/Suppliers.vue'), meta: { title: '供应商应付' } },

      /* 业务台账（批次 5）：每域一个只读台账页，数据源是 v3 的 vw_* 视图 */
      { path: 'ledger/purchase', component: () => import('@/views/ledger/Purchase.vue'), meta: { title: '采购台账' } },
      { path: 'ledger/receivable', component: () => import('@/views/ledger/Receivable.vue'), meta: { title: '应收台账' } },
      { path: 'ledger/payable', component: () => import('@/views/ledger/Payable.vue'), meta: { title: '应付台账' } },
      { path: 'ledger/voucher', component: () => import('@/views/ledger/Voucher.vue'), meta: { title: '凭证台账' } },
      /* 批次 16：凭证录入（手工凭证）。挂在台账子路径下，从台账右上角「录入凭证」进入 */
      { path: 'ledger/voucher/new', component: () => import('@/views/ledger/VoucherEdit.vue'), meta: { title: '录入凭证' } },
      { path: 'ledger/subject', component: () => import('@/views/ledger/Subject.vue'), meta: { title: '科目余额' } },
      { path: 'ledger/delivery', component: () => import('@/views/ledger/Delivery.vue'), meta: { title: '配送台账' } },
      { path: 'ledger/cylinder', component: () => import('@/views/ledger/Cylinder.vue'), meta: { title: '钢瓶台账' } },
      { path: 'ledger/commission', component: () => import('@/views/ledger/Commission.vue'), meta: { title: '提成台账' } },
      { path: 'ledger/payroll', component: () => import('@/views/ledger/Payroll.vue'), meta: { title: '薪资台账' } },
      { path: 'ledger/stocktake', component: () => import('@/views/ledger/Stocktake.vue'), meta: { title: '库存盘点台账' } },

      /* 报表 */
      { path: 'reports/center', component: () => import('@/views/reports/ReportCenter.vue'), meta: { title: '报表中心' } },
      { path: 'reports/view/:view', component: () => import('@/views/reports/ReportView.vue'), meta: { title: '报表' } },
      { path: 'reports/statement', component: () => import('@/views/reports/Statement.vue'), meta: { title: '客户对账单' } },
      { path: 'reports/supplier', component: () => import('@/views/reports/Supplier.vue'), meta: { title: '供气商对账' } },
      { path: 'reports/commission', component: () => import('@/views/reports/Commission.vue'), meta: { title: '计提汇总' } },
      { path: 'reports/delivery-list', component: () => import('@/views/reports/DeliveryList.vue'), meta: { title: '配送清单' } },
      { path: 'reports/alerts', component: () => import('@/views/reports/Alerts.vue'), meta: { title: '客户预警' } },
      { path: 'reports/stock-diff', component: () => import('@/views/reports/StockDiff.vue'), meta: { title: '盘点差异' } },

      /* 系统 */
      { path: 'system/permissions', component: () => import('@/views/system/Permissions.vue'), meta: { title: '权限矩阵' } },
      /* 批次 16：期间管理（月结 / 反月结 / 结转损益 / 成本结转） */
      { path: 'system/periods', component: () => import('@/views/system/Periods.vue'), meta: { title: '期间管理' } },
      { path: 'system/logs', component: () => import('@/views/system/Logs.vue'), meta: { title: '登录日志' } },
      { path: 'system/oplogs', component: () => import('@/views/system/Oplogs.vue'), meta: { title: '操作日志' } },
      { path: 'system/params', component: () => import('@/views/system/Params.vue'), meta: { title: '系统参数' } },
      { path: 'system/password', component: () => import('@/views/system/Password.vue'), meta: { title: '修改密码' } }
    ]
  },
  { path: '/:pathMatch(.*)*', redirect: '/dashboard' }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

/**
 * 单据 / 档案页的资源从路径里的 kind / table 推出来。
 *
 * ⚠ 批次 13 修：原来直接把 kind 当资源名（`return 单据[1]`），而 v3 的资源对象名是
 * `Sale.Delivery` 这类点分代码 —— 于是 `/orders/delivery/0`（新建）和 `/orders/{kind}/{id}`
 * （详情/编辑）在菜单表里查不到静态资源，就退化成"资源 = delivery"，`有资源('delivery')` 恒为 false，
 * **整个录单页被守卫踢回首页**（列表页因为有静态菜单项才没事）。
 */
function 推导资源(path: string): string | undefined {
  const 静态 = 菜单索引[path]?.资源
  if (静态) return 静态
  // `/orders/{kind}/{id}` 与打印页 `/print/orders/{kind}/{id}` 用同一套资源
  const 单据 = path.match(/^\/(?:print\/)?orders\/([^/]+)/)
  if (单据) return 单据资源[单据[1]]      // 未知 kind → undefined（放行，由后端回 400 说明）
  // 配送详情没有静态菜单项，但列表与详情共用 Dist.Task
  if (/^\/dispatch\/(?:tasks|plans)(?:\/|$)/.test(path)) return 'Dist.Task'
  return undefined
}

router.beforeEach(async (to) => {
  if (to.meta.public) return true
  const auth = useAuthStore()
  const me = await auth.拉取()
  if (!me) return { path: '/login', query: { redirect: to.fullPath } }

  // 首次登录（初始密码未改）只做"提示不拦截"：
  // 顶栏显示"初始密码未修改"，点一下去改密页。全局拦截会把人锁死在改密页，
  // 而改密成功后 store 里仍是旧对象（必须改密 依旧为 true），等于永远出不去。
  // 真正需要收紧时，应改为"改密成功后重新拉 /api/auth/me"，而不是无条件拦路由。

  // 资源级守卫：优先用后端下发的"路由 → 资源对象名"登记表（没有就退回本地配置）。
  // 注意：登记表随 /api/auth/menus 一起下发，是异步的 —— 这里必须先等它到位，
  // 否则首屏会按"空表"放行，等于没校验。
  let 资源 = 推导资源(to.path)
  const 后端表 = await auth.拉取菜单().then(() => auth.路由表)
  const 后端资源 = 后端表?.[to.path.replace(/^\//, '')]
  if (后端资源 !== undefined) 资源 = 后端资源 || undefined
  if (资源 && !auth.有资源(资源)) {
    return { path: '/dashboard' }
  }
  return true
})

router.afterEach((to) => {
  document.title = `${to.meta.title ?? ''} · 瓶装气进销存`
})

export default router

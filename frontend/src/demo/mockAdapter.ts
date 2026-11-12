import type { AxiosAdapter, AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { ElMessage } from 'element-plus'
import http from '@/api/http'

interface 清单项 {
  key: string
  file: string
  status: number
}

const 演示资源 = [
  'Md.Product', 'Org.Company', 'Pur.Request', 'Sale.Order', 'Dist.Task', 'Cyl.Archive', 'Inv.Stock',
  'Fin.Receivable', 'Rpt.Dashboard', 'Sys.User', 'Hr.Commission', 'Mob.Device', 'Mob.Queue', 'Hr.Payroll',
  'Sys.Log', 'Rpt.Report', 'Fin.Payable', 'Inv.Ledger', 'Cyl.Inspect', 'Dist.Route', 'Sale.Delivery',
  'Pur.Order', 'Org.Office', 'Md.Category', 'Md.Barcode', 'Org.Warehouse', 'Pur.Receipt', 'Sale.Return',
  'Dist.Gps', 'Cyl.Scrap', 'Inv.Transfer', 'Fin.Voucher', 'Rpt.Commission', 'Sys.Integration',
  'Hr.Attendance', 'Rpt.ReportDef', 'Intf.Config', 'Fin.Period', 'Inv.Stocktake', 'Sale.Statement',
  'Pur.Qc', 'Org.Param', 'Md.Customer', 'Md.Supplier', 'Org.Dict', 'Pur.Return', 'Inv.Adjust',
  'Fin.Expense', 'Intf.Log', 'Rpt.Subscribe', 'Intf.Platform', 'Sys.Audit', 'Org.Attach', 'Md.Employee',
  'Md.Vehicle', 'Sys.Session', 'Intf.Payment', 'Sys.Permit', 'Md.Price', 'Md.Promotion', 'Sys.Export'
]

const 演示用户 = {
  员工编号: 1,
  工号: 'E001',
  姓名: '演示管理员',
  部门名称: '总部',
  岗位名称: '负责人',
  必须改密: false,
  资源: 演示资源
}

const 基础路径 = import.meta.env.BASE_URL || '/'
let 清单: Promise<Map<string, 清单项>> | null = null
const 数据缓存 = new Map<string, any>()

/** 巡检用诊断：每条路由实际命中/未命中的接口，随页面加载累积 */
const 诊断 = { 命中: [] as string[], 未命中: [] as string[], 复用: [] as string[], 写操作: [] as string[] }
export function 重置诊断() {
  诊断.命中.length = 0
  诊断.未命中.length = 0
  诊断.复用.length = 0
  诊断.写操作.length = 0
}

function 读清单() {
  if (!清单) {
    清单 = fetch(`${基础路径}mock/manifest.json`)
      .then((r) => r.json())
      .then((m: { entries: 清单项[] }) => new Map(m.entries.map((e) => [e.key, e])))
      .catch(() => new Map<string, 清单项>())
  }
  return 清单
}

function 路径(cfg: InternalAxiosRequestConfig) {
  let u = cfg.url || ''
  if (/^https?:\/\//.test(u)) u = new URL(u).pathname
  else if (!u.startsWith('/api')) u = (cfg.baseURL || '') + u
  return decodeURIComponent(u.split('?')[0]).replace(/\/+$/, '')
}

function 取模式(p: string) {
  return p.replace(/\/\d+(?=\/|$)/g, '/{id}')
}

async function 读数据(path: string) {
  const 表 = await 读清单()
  const 命中 = 表.get(`GET ${path}`) ?? 表.get(`GET ${取模式(path)}`)
  if (!命中) {
    诊断.未命中.push(path)
    return undefined
  }
  if (!数据缓存.has(命中.file)) {
    const r = await fetch(`${基础路径}mock/${命中.file}`)
    if (!r.ok) {
      诊断.未命中.push(path)
      return undefined
    }
    数据缓存.set(命中.file, await r.json())
    诊断.命中.push(path)
  } else {
    诊断.复用.push(path)
  }
  return 数据缓存.get(命中.file)
}

function 列表兜底(p: string) {
  if (/^\/api\/(orders|records)\/[^/]+$/.test(p)) return { total: 0, items: [] }
  if (/^\/api\/inventory\/(ledger|ops\/(transfers|adjusts|freezes))$/.test(p)) return { total: 0, items: [] }
  if (/^\/api\/(partners\/customers|finance\/vouchers|cylinders|system\/(login-logs|oplogs))$/.test(p))
    return { total: 0, items: [] }
  if (/^\/api\/(inventory\/stock|partners\/suppliers|finance\/subjects|system\/params)$/.test(p)) return []
  return {}
}

function 响应(cfg: InternalAxiosRequestConfig, data: any, status = 200): AxiosResponse {
  return { data, status, statusText: status === 200 ? 'OK' : '', headers: {}, config: cfg, request: null }
}

function 提示不保存() {
  ElMessage({ type: 'info', message: '静态演示，不保存', grouping: true })
}

function 写操作(path: string, method: string, body: any) {
  诊断.写操作.push(`${method.toUpperCase()} ${path}`)
  提示不保存()
  if (/^\/api\/auth\/logout$/.test(path)) return null
  if (/^\/api\/auth\/password$/.test(path)) return null
  if (/^\/api\/orders\/[^/]+\/reverse$/.test(path)) return { id: 90002, 单据号码: '演示-红字-001' }
  if (/^\/api\/orders\/[^/]+\/[^/]+\/(submit|approve|post|unpost)$/.test(path)) return { ok: true }
  if (/^\/api\/orders\/[^/]+$/.test(path) && method === 'post')
    return { id: 90001, 单据号码: '演示-新建-001' }
  if (/^\/api\/finance\/vouchers$/.test(path) && method === 'post') {
    const 分录 = Array.isArray(body?.分录) ? body.分录 : []
    const 借 = 分录.filter((x: any) => x.借贷方向 === '借').reduce((s: number, x: any) => s + Number(x.金额 || 0), 0)
    return { 凭证编号: 90001, 凭证字号: '演示-001', 借方合计: 借, 贷方合计: 借 }
  }
  if (/^\/api\/finance\/vouchers\/\d+\/reverse$/.test(path)) return { ok: true, 新凭证编号: 90002, 新凭证字号: '演示-002', 摘要: '演示红冲' }
  if (/^\/api\/cylinders\/batch$/.test(path)) return { ok: true, 数量: Number(body?.数量 ?? 0), 钢瓶: [] }
  if (/^\/api\/records\/[^/]+$/.test(path) && method === 'post') return { id: 90001 }
  if (/^\/api\/records\/[^/]+\/\d+$/.test(path) && method === 'put') return { ok: true }
  return { ok: true }
}

const 适配器: AxiosAdapter = async (cfg) => {
  const method = (cfg.method || 'get').toLowerCase()
  const path = 路径(cfg as InternalAxiosRequestConfig)

  if (path === '/api/auth/me' || path === '/api/auth/login') return 响应(cfg as InternalAxiosRequestConfig, 演示用户)
  if (path === '/api/auth/menus') {
    const 数据 = await 读数据(path)
    return 响应(cfg as InternalAxiosRequestConfig, 数据 ?? { 菜单: [], 路由: {} })
  }
  if (method === 'get') {
    if (/^\/api\/orders\/[^/]+\/0$/.test(path))
      return 响应(cfg as InternalAxiosRequestConfig, { 主表: { 单据号码: '演示-新建' }, 明细: [] })
    const 数据 = await 读数据(path)
    return 响应(cfg as InternalAxiosRequestConfig, 数据 === undefined ? 列表兜底(path) : 数据)
  }
  const body = typeof cfg.data === 'string' ? (() => { try { return JSON.parse(cfg.data) } catch { return null } })() : cfg.data
  return 响应(cfg as InternalAxiosRequestConfig, 写操作(path, method, body))
}

function 安装外观() {
  document.documentElement.classList.add('demo-mode')
  const meta = document.createElement('meta')
  meta.name = 'robots'
  meta.content = 'noindex, nofollow'
  document.head.appendChild(meta)

  const style = document.createElement('style')
  style.textContent = `
    html.demo-mode body { padding-top: 30px; padding-bottom: 24px; box-sizing: border-box; }
    html.demo-mode .app-shell, html.demo-mode .login-page {
      height: calc(100vh - 54px); height: calc(100dvh - 54px);
    }
    #demo-banner { position: fixed; inset: 0 0 auto 0; height: 30px; z-index: 4000;
      display: flex; align-items: center; justify-content: center; gap: 14px;
      background: #1f2d3d; color: #ffe9b8; font: 12px/1 -apple-system, "Microsoft YaHei", sans-serif; letter-spacing: .02em; }
    #demo-banner b { color: #ffd166; font-weight: 600; }
    #demo-banner a { color: #9fd0ff; text-decoration: none; }
    #demo-banner a:hover { text-decoration: underline; }
    #demo-footer { position: fixed; inset: auto 0 0 0; height: 24px; z-index: 4000;
      display: flex; align-items: center; justify-content: center; gap: 16px;
      background: #f4f6f9; color: #6b7785; border-top: 1px solid #e4e9f0;
      font: 11px/1 -apple-system, "Microsoft YaHei", sans-serif; }
    #demo-footer a { color: #4a6fa5; text-decoration: none; }
    #demo-footer a:hover { text-decoration: underline; }
    @media (max-width: 640px) {
      #demo-banner { font-size: 11px; gap: 8px; }
      #demo-footer { font-size: 10px; gap: 8px; }
    }
  `
  document.head.appendChild(style)

  const 横幅 = document.createElement('div')
  横幅.id = 'demo-banner'
  横幅.innerHTML =
    '<span><b>静态演示</b> · 虚构数据</span><span>写操作仅提示，不保存</span>' +
    `<a href="${基础路径}tour/">截图导览</a><a href="${基础路径}dashboard">进入系统</a>`
  document.body.appendChild(横幅)

  const 页脚 = document.createElement('div')
  页脚.id = 'demo-footer'
  页脚.innerHTML =
    '<span>ICP 备案号：鄂ICP备00000000号（占位）</span><span>演示环境 · 非生产系统</span>' +
    '<a href="https://zemworld.cn/" target="_blank" rel="noopener">返回作品集</a>'
  document.body.appendChild(页脚)
}

export function installDemo() {
  http.defaults.adapter = 适配器
  ;(window as any).__DEMO__ = 诊断
  ;(window as any).__DEMO_RESET = 重置诊断
  安装外观()
}

export default installDemo

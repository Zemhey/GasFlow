namespace GasInventory.Api.Services;

/// <summary>
/// 前端路由 → 权限资源对象名 的登记表（后端下发，前端据此做菜单与页面的资源级校验）。
///
/// 为什么要后端维护：
///   前端 router 的资源守卫要判断"这个页面该看哪个资源"，但对象名是权限资源表里的口径
///   （如 Inv.Stock / Sale.Delivery）。把映射放在前端配置里，迟早与库里对不上；
///   放在后端，启动时可与库内《权限资源.对象名》逐一比对（见 Program.cs 的资源自检），错一个就报。
///
/// 规则：一条路由只登记"进入该页面需要哪个资源"。同一资源下多条子路由按最长前缀匹配。
/// </summary>
public static class 路由资源表
{
    /// <summary>
    /// 后端登记的路由 → 资源对象名（前端 router/index.ts 的 path 去掉前导 /）。
    /// `Program.cs` 启动时把这些对象名与 `权限资源.对象名` 逐一比对（和 `动态资源表`、控制器上的
    /// `[资源]` 一起进同一道自检），登记了库里没有的名字 = 该页整族 403，所以错一个就要在启动日志里喊出来。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> 全部 = new Dictionary<string, string>
    {
        ["dashboard"] = "Rpt.Dashboard",

        /* 钢瓶 / 库存 */
        ["inventory/stock"] = "Inv.Stock",
        ["inventory/ledger"] = "Inv.Ledger",
        ["inventory/reconcile"] = "Inv.Stock",
        /* 库存作业（批次 19）：调拨/调整/冻结。整页进得来要求有调拨资源；
           调整与冻结的接口自己挂 Inv.Adjust，页内按钮再按 有权限('Inv.Adjust', …) 收窄。 */
        ["inventory/ops"] = "Inv.Transfer",

        /* 往来资金：客户欠款页读的是 PartnersReceivableController（[资源("Fin.Receivable")]），
           原登记成 Sale.Statement 会让「界面允许进、接口 403」——批次 6 第 6 步核对时改正。 */
        ["partners/customers"] = "Fin.Receivable",
        ["partners/ageing"] = "Fin.Receivable",
        ["partners/suppliers"] = "Fin.Payable",

        /* 业务台账（批次 5）：每域一个只读台账页，数据源是 vw_* 视图 */
        ["ledger/purchase"] = "Pur.Receipt",
        ["ledger/receivable"] = "Fin.Receivable",
        ["ledger/payable"] = "Fin.Payable",
        ["ledger/voucher"] = "Fin.Voucher",
        /* 批次 16：凭证录入页（/ledger/voucher/new）不是独立菜单项，路由表必须整条登记，
           否则前端按路径查不到资源 → 退化成"无资源"放行。 */
        ["ledger/voucher/new"] = "Fin.Voucher",
        ["ledger/subject"] = "Fin.Period",
        ["ledger/delivery"] = "Dist.Task",
        ["ledger/cylinder"] = "Cyl.Archive",
        ["ledger/stocktake"] = "Inv.Ledger",
        ["ledger/commission"] = "Hr.Commission",
        ["ledger/payroll"] = "Hr.Payroll",
        /* 配送调度（批次 17）：列表与计划/任务详情共用同一个资源 */
        ["dispatch/tasks"] = "Dist.Task",
        ["dispatch/plans"] = "Dist.Task",
        /* 报表 */
        ["reports/center"] = "Rpt.Report",
        ["reports/view"] = "Rpt.Report",
        ["reports/statement"] = "Sale.Statement",
        ["reports/supplier"] = "Pur.Order",
        ["reports/commission"] = "Rpt.Commission",
        ["reports/delivery-list"] = "Dist.Task",
        ["reports/alerts"] = "Md.Customer",
        ["reports/stock-diff"] = "Inv.Stocktake",
        /* 批次 7：押金收据菜单已改为「客户押金账户」，走通用报表页直读 vw_客户押瓶对比。
           路径里带视图名，所以是**整条**登记（不是前缀）—— 换视图就要同步改这里。 */
        ["reports/view/vw_客户押瓶对比"] = "Fin.Receivable",

        /* 系统 */
        ["system/permissions"] = "Sys.User",
        ["system/periods"] = "Fin.Period",
        ["system/logs"] = "Sys.Log",
        ["system/oplogs"] = "Sys.Audit",
        ["system/params"] = "Org.Param",
        ["system/password"] = ""            // 人人可改自己的密码，不挂资源

        /* 单据（/orders/{kind}）与档案（/records/{table}）由前端按 kind/table 自己推导，见 router/index.ts；
           后端侧的 kind/table → 资源 映射在 动态资源表，两处必须同源（批次 20 统一到过滤器）。 */
    };
}

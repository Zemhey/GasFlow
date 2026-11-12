namespace GasInventory.Api.Services;

/// <summary>
/// 「路由参数 → 权限资源对象名 / 权限代码」的动态映射表（批次 20）。
///
/// 为什么需要它：`/api/orders/{kind}` 和 `/api/records/{table}` 是"一个方法管一族单据/一张族档案"，
/// 静态 `[资源("…")]` 特性写不出"资源取决于路由里的 kind/table"。批次 20 之前，这两处各自在控制器里
/// 手写一份字典 + 校验（OrdersController.资源名 / RecordsController.资源映射），
/// 于是出现两类后果：① 读接口（列表/详情/取价/下一个单号）根本没判权限，任何登录用户都能翻出全部单据；
/// ② 权限口径散落在两个控制器里，改一处忘一处。
/// 现在统一到 `权限过滤器` 一条通路：特性声明"按哪个路由参数取资源"，过滤器负责解析与拦截。
///
/// 两条硬约束：
///   · 值必须是 `权限资源.对象名` 点分口径，写错 = 全员 403；`Program.cs` 启动时会与库内对象名比对（同 路由资源表）。
///   · 映射里没有的 kind/table **不在这里判权限**（放行给控制器回 400「不支持的单据类型/表」），
///     否则未知 kind 会从 400 变成 403，把"参数写错"伪装成"没权限"。
/// </summary>
public static class 动态资源表
{
    /// <summary>单据 kind → 资源对象名（与 OrdersController.定义表 的六个 kind 一一对应）</summary>
    public static readonly IReadOnlyDictionary<string, string> 单据 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["delivery"] = "Sale.Delivery",
        ["intake"] = "Pur.Receipt",
        ["stocktake"] = "Inv.Stocktake",
        // 批次 15：财务作业三张单（录入即核销 + 生成凭证）
        ["receipt"] = "Fin.Receivable",
        ["payment"] = "Fin.Payable",
        ["expense"] = "Fin.Expense",
    };

    /// <summary>
    /// 档案表名 → 资源对象名。未列出的表落 <see cref="档案兜底"/>（字典/基础资料维护）。
    /// ⚠ 批次 20 收紧：`权限资源` / `权限项` 从 Org.Dict 挪到 Sys.User ——
    ///   它们定义了"谁能干什么"，让"字典维护岗"能改资源字典等于留出提权路径。
    ///   （真正的授权关系在 `岗位权限`，它是复合主键，本来就不在这个通用 CRUD 白名单里。）
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> 档案 = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["客户"] = "Md.Customer", ["客户联系人"] = "Md.Customer",
        ["客户收货地址"] = "Md.Customer", ["客户协议价"] = "Md.Price",
        ["商品"] = "Md.Product", ["商品品类"] = "Md.Category",
        ["商品条码"] = "Md.Barcode", ["商品气瓶属性"] = "Md.Product",
        ["供应商"] = "Md.Supplier", ["供应商联系人"] = "Md.Supplier",
        ["车辆"] = "Md.Vehicle", ["员工"] = "Md.Employee",
        ["价格方案"] = "Md.Price", ["价格方案明细"] = "Md.Price",
        ["公司"] = "Org.Company", ["经营网点"] = "Org.Office",
        ["仓库"] = "Org.Warehouse", ["仓位"] = "Org.Warehouse",
        ["系统参数"] = "Org.Param",
        ["字典类型"] = "Org.Dict", ["字典项"] = "Org.Dict",
        ["权限资源"] = "Sys.User", ["权限项"] = "Sys.User",
    };

    /// <summary>未登记档案表的兜底资源（与批次 20 之前的 RecordsController.默认资源 同口径）</summary>
    public const string 档案兜底 = "Org.Dict";

    /// <summary>
    /// 通用档案 CRUD 的**白名单**（单据主表/明细表一律排除，防止绕过业务校验；复合主键表不在内）。
    /// 批次 20 把它从 RecordsController 挪到这里，是为了让"要不要判权限"和"这张表允不允许维护"
    /// 用同一个集合：<b>不在白名单里的表 → 不判资源，直接由控制器回 400</b>
    /// （否则 `/api/records/送气单` 会先吃一个 403，把"这张表不走通用接口"说成"你没权限"）。
    /// </summary>
    public static readonly IReadOnlySet<string> 档案白名单 = new HashSet<string>(StringComparer.Ordinal)
    {
        // 主数据
        "客户", "客户联系人", "客户收货地址", "客户协议价",
        "商品", "商品品类", "商品条码", "商品气瓶属性",
        "供应商", "供应商联系人", "车辆",
        "客户分类", "客户区域", "渠道",
        "价格方案", "价格方案明细",
        // 组织 / 人物
        "公司", "经营网点", "仓库", "仓位", "部门", "岗位", "员工",
        // 字典 / 参数 / 权限
        "字典类型", "字典项", "计量单位", "费用类型",
        "系统参数", "权限资源", "权限项",
    };

    /// <summary>
    /// 单据动作 → 权限代码。与库里的《权限项》一字不差（view/add/edit/del/audit/post/print/export）：
    /// 提交=改（EDIT）、审核=AUDIT、过账/质检/冲销=POST。
    /// 未登记的动作为空串 → 控制器自己回 400（未知动作）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> 单据动作 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["submit"] = "edit",
        ["approve"] = "audit",
        ["post"] = "post",
        // v3 已没有反过账过程（更正走红冲），但动作名仍在前端契约里，权限代码照旧按 POST 收
        ["unpost"] = "post",
        ["qc"] = "post",
        ["reverse"] = "post",
    };

    /// <summary>按映射集名取资源对象名；未知 kind / 不在白名单的表返回 null（交给控制器判 400）</summary>
    public static string? 资源(string 映射集, string? 键) => 映射集 switch
    {
        // 档案表名是中文，必须**逐字**匹配（Ordinal）；白名单内但未单独登记的表落兜底 Org.Dict
        "档案" when 键 is null or { Length: 0 } => null,
        "档案" => 档案.TryGetValue(键, out var 表) ? 表 : 档案白名单.Contains(键) ? 档案兜底 : null,
        "单据" when 键 is null or { Length: 0 } => null,
        "单据" => 单据.TryGetValue(键, out var 型) ? 型 : null,
        _ => null
    };

    /// <summary>按映射集名取权限代码；未知动作返回 null（过滤器退回方法上的 `[需要权限]`）</summary>
    public static string? 权限代码(string 映射集, string? 键) =>
        映射集 == "单据动作" && 键 is not null && 单据动作.TryGetValue(键, out var 码) ? 码 : null;

    /// <summary>全部出现过的资源对象名（启动自检用：登记了库里不存在的名字 = 全员 403）</summary>
    public static IEnumerable<string> 全部资源名() => 单据.Values.Concat(档案.Values);
}

using System.Text.Json;
using GasInventory.Api.Auth;
using GasInventory.Api.Services;
using GasInventory.Infrastructure.Data;
using GasInventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GasInventory.Api.Controllers;

/// <summary>
/// 单据统一入口。六类单据（送气/进气/借瓶/还瓶/盘点/押金/入仓）结构相同：
/// 主表 + 明细表，整单提交，服务端事务保存并重算合计。
/// 路由：/api/orders/{kind}
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly GasInventoryDbContext _db;
    private readonly 数据服务 _data;
    private readonly 单号服务 _no;
    private readonly 权限服务 _perm;
    private readonly 当前登录 _me;

    public OrdersController(GasInventoryDbContext db, 数据服务 data, 单号服务 no, 权限服务 perm, 当前登录 me)
    {
        _db = db;
        _data = data;
        _no = no;
        _perm = perm;
        _me = me;
    }

    /// <summary>
    /// 按岗位授权校验（批次 20 收敛：kind → 资源的映射表移到 <see cref="动态资源表"/>，
    /// 与 `权限过滤器` 用同一份口径）。这里只保留**过滤器判不出来的细粒度**：
    /// 「保存」按请求体里的 id 区分新增（ADD）/改单（EDIT）——路由上没有这个信息。
    /// 未登记的 kind 不在这里拦（控制器前面已有 400「未知单据类型」）。
    /// </summary>
    private async Task<IActionResult?> 校验权限(string kind, string 代码)
    {
        var 资源 = 动态资源表.资源("单据", kind);
        if (资源 is null) return null;
        if (await _perm.有权限Async(_me.员工编号, 资源, 代码)) return null;
        return StatusCode(403, new { message = $"没有权限：{资源} / {代码}" });
    }

    /// <summary>单据类型定义</summary>
    private sealed record 定义(
        string Kind, string 主表, string? 明细表, string? 明细外键,
        string 前缀, string? 单号列, string 日期列, string? 金额列, string? 销量列, string? 明细金额列);

    /// <summary>
    /// **批次 7**：只剩 3 个 kind 在 v3 有同名单据。
    /// 列名全部换成 v3 真实列（v2 的 送气日期/金额合计/销量合计/送气员工编号 已不存在）——
    /// 之前 日期列 写的是 v2 列名，`查询Async` 认不出列就**静默丢掉日期筛选与排序**，
    /// 接口看着 200，其实日期区间完全没生效。
    /// </summary>
    private static readonly Dictionary<string, 定义> 定义表 = new()
    {
        ["delivery"] = new("delivery", "送气单", "送气单明细", "送气单编号", "SH", "单据号码", "送货日期", "销售金额", "销售数量", "行金额"),
        ["intake"] = new("intake", "到货单", "到货单明细", "到货单编号", "DHD", "单据号码", "到货日期", null, null, null),
        ["stocktake"] = new("stocktake", "库存盘点单", "库存盘点单明细", "盘点单编号", "盘点", null, "盘点日期", null, null, null),
        /* 批次 15：财务三张单。列名按 v3 真实表（收款单/付款单/费用单），
           明细表只用于「详情」把核销/费用明细带出来 —— 写入一律走过程，**不直插这两张明细表**。 */
        ["receipt"] = new("receipt", "收款单", "收款核销明细", "收款单编号", "SK", "单据号码", "收款日期", "收款金额", null, null),
        ["payment"] = new("payment", "付款单", "付款核销明细", "付款单编号", "FK", "单据号码", "付款日期", "付款金额", null, null),
        ["expense"] = new("expense", "费用单", "费用单明细", "费用单编号", "FY", "单据号码", "费用日期", "费用金额", null, null),
    };

    /// <summary>
    /// v3 已无同名单据的 kind：给出**明确的替代入口**，不再硬造表。
    /// 说明文字直接回给前端展示（400），而不是抛异常变 500。
    ///
    /// ⚠ 批次 15：receipt / payment 已从这张表**移除** —— v3 里 收款单 / 付款单 就是真表
    /// （dbo.收款单 / dbo.付款单），写入走 usp_财务_收款 / usp_财务_付款，不再是"已并入"。
    /// </summary>
    private static readonly Dictionary<string, string> 已停用 = new()
    {
        ["borrow"] = "借瓶单在 v3 已并入钢瓶资产：请用「钢瓶台账」看 钢瓶流转明细，押金与借出瓶数看「客户押金账户」",
        ["return"] = "还瓶单在 v3 已并入钢瓶资产：请用「钢瓶台账」看 钢瓶流转明细（回收/报废），押金余额看「客户押金账户」",
        ["warehouse"] = "入仓单在 v3 已由 装车单 / 库存调拨单 / 到货单 取代：请用「采购台账」（到货）或「库存盘点台账」",
        ["deposit"] = "押金收据在 v3 已并入 客户押金账户（200 行）：请用「客户押金账户」查看押金余额与借出瓶数",
        ["commission"] = "提成结算在 v3 改为 提成结算单 + 提成明细：请用「提成台账」或「计提汇总」报表",
    };

    /// <summary>
    /// 批次 15：**录入即记账**的单据（没有草稿态，也就没有 提交/审核/过账 三个动作）。
    /// 三类过程都是「建单 + 核销 + 建凭证」一个事务做完，单据状态直接落 3（已过账）。
    /// 因此这里的动作一律 409 + 说明，而不是让它静默返回 ok 骗前端。
    /// </summary>
    private static readonly Dictionary<string, string> 录入即记账 = new()
    {
        ["receipt"] = "收款单录入即核销并生成凭证（一次完成），没有 提交/审核/过账 动作；录错请用财务红冲",
        ["payment"] = "付款单录入即核销并生成凭证（一次完成），没有 提交/审核/过账 动作；录错请用财务红冲",
        ["expense"] = "费用单录入即审批通过并生成凭证（一次完成），没有 提交/审核/过账 动作；录错请用财务红冲",
    };

    /// <summary>未知 kind 的统一说明（400，不是 500）</summary>
    private static string 未知说明(string kind) =>
        $"未知单据类型 {kind}。可用类型：delivery（送气单）/ intake（到货单）/ stocktake（库存盘点单）"
        + " / receipt（收款单）/ payment（付款单）/ expense（费用单）";

    /// <summary>取定义；取不到时返回 null（调用方回 400 + 说明）</summary>
    private static 定义? 取定义(string kind) => 定义表.TryGetValue(kind, out var d) ? d : null;

    /// <summary>停用 kind → 说明；未知 kind → 未知说明</summary>
    private static string 不可用说明(string kind) =>
        已停用.TryGetValue(kind, out var 说明) ? 说明 : 未知说明(kind);

    // ---------------- 查询 ----------------

    /// <summary>分页列表：支持关键词、日期区间和任意列等值过滤</summary>
    [HttpGet("{kind}")]
    [资源按路由("kind", "单据")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 列表(
        string kind, int page = 1, int size = 20, string? keyword = null, string? orderBy = null,
        bool desc = true, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        if (取定义(kind) is not { } d) return BadRequest(new { message = 不可用说明(kind) });
        var filters = new Dictionary<string, string?>();
        foreach (var q in Request.Query)
        {
            if (q.Key is "page" or "size" or "keyword" or "orderBy" or "desc" or "from" or "to") continue;
            filters[q.Key] = q.Value.ToString();
        }

        var r = await _data.查询Async(d.主表, filters, keyword, page, size,
            orderBy ?? d.日期列, desc, (d.日期列, from, to), ct);
        return Ok(new { total = r.总数, items = r.数据 });
    }

    /// <summary>整单详情：主表 + 明细</summary>
    [HttpGet("{kind}/{id:int}")]
    [资源按路由("kind", "单据")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 详情(string kind, int id, CancellationToken ct)
    {
        if (取定义(kind) is not { } d) return BadRequest(new { message = 不可用说明(kind) });
        var header = await _data.按主键Async(d.主表, id, ct);
        if (header is null) return NotFound(new { message = "单据不存在" });

        object? details = null;
        if (d.明细表 is not null)
        {
            details = await _data.查询Async(d.明细表, new Dictionary<string, string?> { [d.明细外键!] = id.ToString() },
                null, 1, 500, null, false, null, ct);
        }
        // 费用单主表**不存费用类型**（只在 费用单明细 的类型列上），这里把首行回填进主表，
        // 否则编辑页打开一张已记账的费用单，"费用类型"永远是空的。
        if (kind == "expense" && (details as 分页结果)?.数据.FirstOrDefault() is { } 首行)
            header["费用类型编号"] = 首行.GetValueOrDefault("费用类型编号");
        return Ok(new { 主表 = header, 明细 = (details as 分页结果)?.数据 ?? new List<Dictionary<string, object?>>() });
    }

    /// <summary>预览下一个单据号</summary>
    [HttpGet("{kind}/next-no")]
    [资源按路由("kind", "单据")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 下一个单号(string kind)
    {
        if (取定义(kind) is not { } d) return BadRequest(new { message = 不可用说明(kind) });
        if (d.单号列 is null) return Ok(new { no = "" });
        var no = await _no.生成Async(d.主表, d.前缀, DateTime.Today, d.单号列);
        return Ok(new { no });
    }

    // ---------------- 保存 / 删除 ----------------

    public sealed class 单据入参
    {
        public JsonElement 主表 { get; set; }
        public List<JsonElement> 明细 { get; set; } = new();
    }

    [HttpPost("{kind}")]
    [资源按路由("kind", "单据")]
    [需要权限("VIEW")]          // 资源级地板；新增(ADD)/改单(EDIT)由下面的 校验权限 按请求体里的 id 判
    public async Task<IActionResult> 保存(string kind, [FromBody] 单据入参 body, CancellationToken ct)
    {
        if (取定义(kind) is not { } d) return BadRequest(new { message = 不可用说明(kind) });
        var 主键 = 元数据.取(d.主表).主键;
        var 是否编辑 = body.主表.ValueKind == JsonValueKind.Object
                       && body.主表.TryGetProperty(主键, out var pkEl)
                       && pkEl.TryGetInt32(out var pkValue) && pkValue > 0;
        if (await 校验权限(kind, 是否编辑 ? "EDIT" : "ADD") is { } 拒绝) return 拒绝;
        return kind switch
        {
            "delivery" => await 保存送气(body, ct),
            "intake" => await 保存到货(body, ct),
            // v3 只有 usp_库存_盘点过账，没有"新建盘点单"的过程；盘点单由仓库作业/导入产生
            "stocktake" => await 保存盘点(body, ct),
            // 批次 15：财务作业三张单，一律走 usp_财务_收款 / 付款 / 费用
            "receipt" => await 保存收款(body, ct),
            "payment" => await 保存付款(body, ct),
            "expense" => await 保存费用(body, ct),
            _ => BadRequest(new { message = 未知说明(kind) })
        };
    }

    // ---------------- 送气单录单：走 v3 过程（批次 8） ----------------

    /// <summary>
    /// 送气单录单。**v3 里一张送气单不是一个"可直接 INSERT 的表单"**，而是一条链：
    ///   usp_销售_下单 → usp_销售_加明细 × N → usp_销售_订单审核(提交) → usp_销售_订单审核(审核)
    ///   → usp_销售_下单送气单
    /// 关键原因（坑 L）：FIFO **跨批次**分配写在 usp_销售_下单送气单 里。绕开它自己插
    /// 送气单 + 明细，会让明细数量压到单一批次上，usp_销售_出库 随即以 53133 失败，
    /// 留下"已审核未过账"的孤单送气单 —— 这正是全年 146 张孤单单的成因。
    ///
    /// 生成的送气单是**草稿（单据状态 0）**：提交/审核/过账仍走本控制器的状态机，
    /// 过账调 usp_销售_出库，不在这里顺手过账。
    /// </summary>
    private async Task<IActionResult> 保存送气(单据入参 body, CancellationToken ct)
    {
        if (取整(body.主表, "送气单编号") is > 0)
            return Conflict(new { message = "v3 的送气单由销售订单生成，不支持直接改单：要改数量/价格请先作废后重新下单" });

        var 客户 = 取整(body.主表, "客户编号");
        if (客户 is null or <= 0) return BadRequest(new { message = "请选择客户" });

        var 行 = body.明细
            .Select(x => new
            {
                商品 = 取整(x, "商品编号") ?? 0,
                数量 = 取整(x, "数量") ?? 0,
                单价 = 取数(x, "单价"),
                备注 = 取文(x, "备注")
            })
            .Where(x => x.商品 > 0 && x.数量 > 0)
            .ToList();
        if (行.Count == 0) return BadRequest(new { message = "至少录入一行明细（商品 + 数量）" });

        var 送货日期 = 取日(body.主表, "送货日期") ?? DateTime.Today;
        var 业务员 = 取整(body.主表, "业务员编号") ?? _me.员工编号;
        var 仓库 = 取整(body.主表, "出库仓库编号") ?? await 默认出库仓(客户.Value, ct);
        if (仓库 is null)
            return Conflict(new { message = "该客户所属网点没有可销售出库的实体仓（需 是否允许销售=1 且非车仓），请先在仓库档案维护" });

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var o1 = await _data.查询SqlAsync("CALL public.\"usp_销售_下单\"(@c,'电话',NULL,@e,NULL,NULL,@m,@o)", ct,
                ("@c", 客户.Value), ("@e", 业务员), ("@m", 取文(body.主表, "备注")), ("@o", null));
            var _o1 = o1.FirstOrDefault();
            var 订单编号 = _o1 is not null && _o1.TryGetValue("p_订单编号", out var _ov) && _ov is not null ? Convert.ToInt32(_ov) : 0;

            foreach (var l in 行)
            {
                await _data.查询SqlAsync("CALL public.\"usp_销售_加明细\"(@o,@p,@q,@price,NULL,@note,@d)", ct,
                    ("@o", 订单编号), ("@p", l.商品), ("@q", l.数量), ("@price", l.单价), ("@note", l.备注), ("@d", null));
            }

            await _data.查询SqlAsync("CALL public.\"usp_销售_订单审核\"(@o,@e,true,true)", ct, ("@o", 订单编号), ("@e", 业务员));
            await _data.查询SqlAsync("CALL public.\"usp_销售_订单审核\"(@o,@e,true,false)", ct, ("@o", 订单编号), ("@e", 业务员));

            var callSingle = await _data.查询SqlAsync("CALL public.\"usp_销售_下单送气单\"(@o,@w,@d,@e,false,@s,NULL)", ct,
                ("@o", 订单编号), ("@w", 仓库.Value), ("@d", 送货日期), ("@e", 业务员), ("@s", null));
            var _sr0 = callSingle.FirstOrDefault();
            var 送气单编号 = _sr0 is not null && _sr0.TryGetValue("p_送气单编号", out var _sv) && _sv is not null ? Convert.ToInt32(_sv) : 0;
            var 单据号码 = Convert.ToString((await _data.查询SqlAsync("SELECT \"单据号码\" FROM \"送气单\" WHERE \"送气单编号\" = @s", ct, ("@s", 送气单编号))).FirstOrDefault()?["单据号码"]);
            await tx.CommitAsync(ct);
            return Ok(new { id = 送气单编号, 单据号码, 订单编号, 出库仓库编号 = 仓库.Value });
        }
        catch (Npgsql.PostgresException ex)
        {
            // 过程用 THROW 给出的业务提示（客户停用 / 超信用额度 / 仓库无库存 …）原样回给前端
            await tx.RollbackAsync(ct);
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 盘点单录入（批次 10）。v3 **没有建盘点单的过程**（只有 `usp_库存_盘点过账`），
    /// 所以主表与明细由 `数据服务.新增Async` 直插 —— 这是 v3 唯一可行的建单方式。
    ///
    /// 关键约束：
    ///   · `库存盘点单明细.差异数量 / 差异金额` 是**计算列**，写会报错（只写账面/实盘）；
    ///   · 明细的 `批次编号 / 库存状态代码 / 权属代码` 是 NOT NULL —— 结存是五维的，
    ///     前端不传批次时，取该商品在该仓「AVAIL + OWN + 数量最大」的那个批次；
    ///   · `账面数量` 不传就按同一维度从 `库存结存` 取（取不到记 0，并在备注里留痕）。
    /// 生成的是**草稿（状态 0）**：提交/审核走状态机，过账走 `usp_库存_盘点过账`。
    /// </summary>
    private async Task<IActionResult> 保存盘点(单据入参 body, CancellationToken ct)
    {
        if (取整(body.主表, "盘点单编号") is > 0)
            return Conflict(new { message = "v3 的盘点单不支持直接改单：请作废后重开" });

        var 仓库 = 取整(body.主表, "仓库编号");
        if (仓库 is null or <= 0) return BadRequest(new { message = "请选择盘点仓库" });
        var 盘点日期 = 取日(body.主表, "盘点日期") ?? DateTime.Today;

        var 行 = body.明细
            .Select(x => new
            {
                商品 = 取整(x, "商品编号") ?? 0,
                批次 = 取整(x, "批次编号"),
                状态 = 取文(x, "库存状态代码"),
                权属 = 取文(x, "权属代码"),
                账面 = 取整(x, "账面数量"),
                实盘 = 取整(x, "实盘数量"),
                原因 = 取文(x, "差异原因")
            })
            .Where(x => x.商品 > 0 && x.实盘 is not null)
            .ToList();
        if (行.Count == 0) return BadRequest(new { message = "至少录入一行明细（商品 + 实盘数量）" });

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var 号 = await _data.查询SqlAsync("CALL public.\"usp_取单据编号\"('stocktake',@no,@d)", ct, ("@no", null), ("@d", 盘点日期));
            var _nh = 号.FirstOrDefault();
            var 单据号码 = _nh is not null && _nh.TryGetValue("p_单据编号", out var _nv) && _nv is not null ? Convert.ToString(_nv) : "";

            var 盘点单编号 = await _data.新增Async("库存盘点单", new Dictionary<string, object?>
            {
                ["单据号码"] = 单据号码,
                ["仓库编号"] = 仓库.Value,
                ["盘点日期"] = 盘点日期,
                ["盘点范围"] = 取文(body.主表, "盘点范围") ?? "全仓",
                ["是否盲盘"] = false,
                ["单据状态"] = 0,
                ["盘点人编号"] = _me.员工编号,
                ["差异行数"] = 0,
                ["差异数量"] = 0,
                ["备注"] = 取文(body.主表, "备注"),
                ["创建人"] = _me.员工编号
            }, ct);

            var 行号 = 1;
            foreach (var l in 行)
            {
                var 状态 = string.IsNullOrWhiteSpace(l.状态) ? "AVAIL" : l.状态!.Trim();
                var 权属 = string.IsNullOrWhiteSpace(l.权属) ? "OWN" : l.权属!.Trim();

                // 批次：优先用传入的；否则取该商品在该仓「AVAIL/OWN 数量最大」的批次
                var 批次 = l.批次 ?? await _data.标量Async<int?>(
                    """
                    SELECT TOP 1 批次编号 FROM dbo.库存结存
                     WHERE 仓库编号 = @w AND 商品编号 = @p AND 库存状态代码 = @s AND 权属代码 = @o
                     ORDER BY 数量 DESC, 批次编号
                    """, ct, ("@w", 仓库.Value), ("@p", l.商品), ("@s", 状态), ("@o", 权属));
                if (批次 is null or <= 0)
                    return BadRequest(new { message = $"商品 {l.商品} 在仓库 {仓库.Value} 没有 {状态}/{权属} 结存，无法盘点" });

                // 账面数量与成本单价：同一维度（仓 × 商品 × 批次 × 状态 × 权属）从结存取
                var 结存 = await _data.查询SqlAsync("""
                    SELECT ISNULL(SUM(数量), 0) AS 数量, ISNULL(MAX(成本单价), 0) AS 成本单价
                    FROM dbo.库存结存
                    WHERE 仓库编号 = @w AND 商品编号 = @p AND 批次编号 = @b
                      AND 库存状态代码 = @s AND 权属代码 = @o
                    """, ct, ("@w", 仓库.Value), ("@p", l.商品), ("@b", 批次.Value),
                         ("@s", 状态), ("@o", 权属));
                var 账面 = l.账面 ?? Convert.ToInt32(结存[0]["数量"] ?? 0);
                var 成本单价 = Convert.ToDecimal(结存[0]["成本单价"] ?? 0m);

                await _data.新增Async("库存盘点单明细", new Dictionary<string, object?>
                {
                    ["盘点单编号"] = 盘点单编号,
                    ["行号"] = 行号++,
                    ["商品编号"] = l.商品,
                    ["批次编号"] = 批次.Value,
                    ["库存状态代码"] = 状态,
                    ["权属代码"] = 权属,
                    ["账面数量"] = 账面,
                    ["实盘数量"] = l.实盘,
                    ["成本单价"] = 成本单价,
                    ["差异原因"] = l.原因,
                    ["是否已调整"] = false
                }, ct);
            }

            // 主表差异汇总（差异数量是明细的计算列，主表这两个是普通列，需要回填）
            var 汇 = await _data.查询SqlAsync("""
                UPDATE public."库存盘点单" AS h
                   SET "差异行数" = x."行数", "差异数量" = x."数量"
                  FROM (SELECT COUNT(1) AS "行数", COALESCE(SUM("差异数量"), 0) AS "数量"
                          FROM public."库存盘点单明细"
                         WHERE "盘点单编号" = @id AND "差异数量" <> 0) AS x
                 WHERE h."盘点单编号" = @id;
                SELECT "差异行数", "差异数量" FROM public."库存盘点单" WHERE "盘点单编号" = @id;
                """, ct, ("@id", 盘点单编号));

            // 把"后端实际选中的批次 / 自动取的账面"回给前端 —— 盘点按批次（五维结存），
            // 用户填的实盘必须与这条批次对上，不回显就很容易把整仓合计当成单批次数（库存会翻倍）
            var 明细 = await _data.查询SqlAsync("""
                SELECT 行号, 商品编号, 批次编号, 库存状态代码, 权属代码,
                       账面数量, 实盘数量, 差异数量
                FROM dbo.库存盘点单明细 WHERE 盘点单编号 = @id ORDER BY 行号
                """, ct, ("@id", 盘点单编号));

            await tx.CommitAsync(ct);
            return Ok(new
            {
                id = 盘点单编号,
                单据号码,
                明细行数 = 行.Count,
                差异行数 = 汇.FirstOrDefault()?["差异行数"] ?? 0,
                差异数量 = 汇.FirstOrDefault()?["差异数量"] ?? 0,
                明细
            });
        }
        catch (Npgsql.PostgresException ex)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 到货单录入（批次 9）。v3 的采购链：`usp_采购_到货登记` → （填明细）→ `usp_采购_质检` → `usp_采购_到货过账`。
    ///
    /// 关键：参数 `biz.采购可无订单直采 = 1`（实测库内就是 1），所以 **@订单编号 可以传 NULL**，
    /// 不必先建采购订单。登记过程只建主表；有订单时才按订单明细预填"应收数量"，
    /// 无订单直采时明细由本方法写入（实收/拒收/单价由录单人填）。
    ///
    /// 生成的到货单是**草稿（单据状态 0）**：质检走 `qc` 动作、入库走 `post` 动作（→ usp_采购_到货过账）。
    /// 注意 `biz.采购入库先质检 = 1`：没过账的到货单必须先质检，否则过账过程 THROW 54283。
    /// </summary>
    private async Task<IActionResult> 保存到货(单据入参 body, CancellationToken ct)
    {
        if (取整(body.主表, "到货单编号") is > 0)
            return Conflict(new { message = "v3 的到货单不支持直接改单：请在「采购台账」查看，或作废后重开" });

        var 供应商 = 取整(body.主表, "供应商编号");
        if (供应商 is null or <= 0) return BadRequest(new { message = "请选择供应商" });
        var 仓库 = 取整(body.主表, "收货仓库编号");
        if (仓库 is null or <= 0) return BadRequest(new { message = "请选择收货仓库（必须是实体仓）" });

        var 行 = body.明细
            .Select(x => new
            {
                商品 = 取整(x, "商品编号") ?? 0,
                实收 = 取整(x, "实收数量") ?? 0,
                拒收 = 取整(x, "拒收数量") ?? 0,
                含税单价 = 取数(x, "含税单价"),
                税率 = 取数(x, "税率"),
                备注 = 取文(x, "备注")
            })
            .Where(x => x.商品 > 0 && x.实收 > 0)
            .ToList();
        if (行.Count == 0) return BadRequest(new { message = "至少录入一行明细（商品 + 实收数量）" });

        var 到货日期 = 取日(body.主表, "到货日期") ?? DateTime.Today;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var 单 = await _data.查询SqlAsync("CALL public.\"usp_采购_到货登记\"(NULL,@s,@w,@d,NULL,@plate,@drv,@e,@r)", ct,
                ("@s", 供应商.Value), ("@w", 仓库.Value), ("@d", 到货日期),
                ("@plate", 取文(body.主表, "车牌")), ("@drv", 取文(body.主表, "司机姓名")),
                ("@e", _me.员工编号), ("@r", null));
            var _hh = 单.FirstOrDefault();
            var 到货单编号 = _hh is not null && _hh.TryGetValue("p_到货单编号", out var _rv) && _rv is not null ? Convert.ToInt32(_rv) : 0;
            var 单据号码 = Convert.ToString((await _data.查询SqlAsync("SELECT \"单据号码\" FROM \"到货单\" WHERE \"到货单编号\"=@r", ct, ("@r", 到货单编号))).FirstOrDefault()?["单据号码"]);

            var 行号 = 1;
            foreach (var l in 行)
            {
                var 商品 = await _data.查询SqlAsync(
                    "SELECT 计量单位, 默认税率 FROM dbo.商品 WHERE 商品编号 = @p", ct, ("@p", l.商品));
                if (商品.Count == 0) return BadRequest(new { message = $"商品 {l.商品} 不存在" });

                var 单位 = Convert.ToString(商品[0]["计量单位"]) ?? "";
                var 税率 = l.税率 ?? (商品[0]["默认税率"] is { } t ? Convert.ToDecimal(t) : 0m);
                var 含税单价 = l.含税单价 ?? 0m;
                // 不含税单价 = 含税 / (1 + 税率)；金额与税额是计算列，不要写
                var 不含税单价 = 税率 > 0 ? Math.Round(含税单价 / (1 + 税率), 4) : 含税单价;

                await _data.新增Async("到货单明细", new Dictionary<string, object?>
                {
                    ["到货单编号"] = 到货单编号,
                    ["行号"] = 行号++,
                    ["商品编号"] = l.商品,
                    ["计量单位"] = 单位,
                    ["应收数量"] = l.实收,
                    ["实收数量"] = l.实收,
                    ["拒收数量"] = l.拒收,
                    ["含税单价"] = 含税单价,
                    ["税率"] = 税率,
                    ["不含税单价"] = 不含税单价,
                    ["合格数量"] = 0,
                    ["不合格数量"] = l.拒收,
                    ["入库数量"] = 0,
                    ["备注"] = l.备注 ?? "无订单直采"
                }, ct);
            }

            await tx.CommitAsync(ct);
            return Ok(new { id = 到货单编号, 单据号码, 明细行数 = 行.Count });
        }
        catch (Npgsql.PostgresException ex)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { message = ex.Message });
        }
    }

    // ---------------- 财务作业三张单：收款 / 付款 / 费用（批次 15） ----------------

    /// <summary>
    /// 收付款方式：**库里有 CHECK 约束**（CK_收款单_方式 / CK_付款单_方式），
    /// 只有这几个值能落库。前端下拉也照这份清单出，但后端再挡一次 ——
    /// 否则自由文本会以 547「与 CHECK 约束冲突」的形式回到用户脸上，完全看不懂。
    /// </summary>
    private static readonly string[] 收款方式集 = { "现金", "银行转账", "微信", "支付宝", "支票", "承兑", "押金抵扣", "其他" };
    private static readonly string[] 付款方式集 = { "现金", "银行转账", "微信", "支付宝", "支票", "承兑", "其他" };

    /// <summary>规范方式：空 → 银行转账；不在清单内 → null（调用方回 400）</summary>
    private static string? 规范方式(string? 值, string[] 清单)
    {
        var v = string.IsNullOrWhiteSpace(值) ? "银行转账" : 值.Trim();
        return 清单.Contains(v) ? v : null;
    }

    /// <summary>备注：过程参数是 nvarchar(300)，超长会以 8152 报出来，这里先截断</summary>
    private static string? 截(string? s, int n) =>
        string.IsNullOrEmpty(s) ? null : (s.Length <= n ? s : s[..n]);

    /// <summary>三张财务单共用的调用壳：过程内部自带事务，这里不再套外层事务</summary>
    private async Task<IActionResult> 执行财务过程(
        string sql, CancellationToken ct, params (string 名, object? 值)[] ps)
    {
        try
        {
            var r = await _data.查询SqlAsync(sql, ct, ps);
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (Npgsql.PostgresException ex)
        {
            // 过程用 THROW 给的业务提示（金额必须大于 0 / 客户不存在 / 期间已结账 …）原样回前端
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>财务单不允许"改单"：过程只有"建单"入口，改单会变成再记一笔</summary>
    private static bool 是改单(单据入参 body, string 主键) => 取整(body.主表, 主键) is > 0;

    /// <summary>
    /// 收款单（批次 15）。`POST /api/orders/receipt` → `usp_财务_收款`。
    ///
    /// **核销结论（读源码核过，不是猜的）**：核销是**过程内自动分配**，前端不需要"录完再核销"。
    ///   · `usp_财务_收款` 只插收款单主表，然后把活交给 `usp_财务_记账_收款`；
    ///   · `usp_财务_记账_收款` 用游标按「最早到期」FIFO 逐张应收单插 `收款核销明细`
    ///     （备注固定为「收款自动核销（先进先出）」），未核销完的余额留作预收；
    ///   · `收款核销明细` 上有两支触发器：
    ///       - `trg_收款核销_校验`：客户必须一致、核销额不得超应收未收额、不得超收款单金额；
    ///       - `trg_收款核销_回写应收`：把 `收款单.已核销金额` 与 `应收单.已收金额` 回写，
    ///         于是 `应收单.未收金额`（计算列）自然下降。
    ///   所以 **本方法绝不直插 `收款核销明细`** —— 直插会绕过 FIFO 口径，且必然与回写触发器打架。
    ///
    /// 返回值里的 `已核销金额` 是过程出参（= 实际冲掉的应收），其余部分就是预收。
    /// </summary>
    private async Task<IActionResult> 保存收款(单据入参 body, CancellationToken ct)
    {
        if (是改单(body, "收款单编号"))
            return Conflict(new { message = "收款单录入即记账、不可改单：录错请用财务红冲，或另录一张" });

        var 客户 = 取整(body.主表, "客户编号");
        if (客户 is null or <= 0) return BadRequest(new { message = "请选择客户" });
        var 金额 = 取数(body.主表, "收款金额") ?? 0m;
        if (金额 <= 0) return BadRequest(new { message = "收款金额必须大于 0" });
        var 方式 = 规范方式(取文(body.主表, "收款方式"), 收款方式集);
        if (方式 is null)
            return BadRequest(new { message = $"收款方式只能是：{string.Join(" / ", 收款方式集)}" });

        try
        {
            var call = await _data.查询SqlAsync("CALL public.\"usp_财务_收款\"(@cust,@amt,@way,@day,@acc,@emp,@note,@r,@v,@a)", ct,
                ("@cust", 客户.Value), ("@amt", 金额), ("@way", 方式),
                ("@day", 取日(body.主表, "收款日期") ?? DateTime.Today),
                ("@acc", 取整(body.主表, "账户编号")),
                ("@emp", 取整(body.主表, "经办人编号") ?? _me.员工编号),
                ("@note", 截(取文(body.主表, "备注"), 300)),
                ("@r", null), ("@v", null), ("@a", null));
            var _c = call.FirstOrDefault();
            var rId = _c is not null && _c.TryGetValue("p_收款单编号", out var _r1) && _r1 is not null ? Convert.ToInt32(_r1) : 0;
            var rV = _c is not null && _c.TryGetValue("p_凭证编号", out var _r2) ? _r2 : null;
            var rA = _c is not null && _c.TryGetValue("p_已核销金额", out var _r3) ? _r3 : null;
            var r = await _data.查询SqlAsync("""
                SELECT @r::integer AS "id",
                       (SELECT "单据号码" FROM "收款单" WHERE "收款单编号" = @r::integer) AS "单据号码",
                       @v::integer AS "凭证编号",
                       @a AS "已核销金额",
                       (SELECT "未核销金额" FROM "收款单" WHERE "收款单编号" = @r::integer) AS "未核销金额",
                       (SELECT "单据状态" FROM "收款单" WHERE "收款单编号" = @r::integer) AS "单据状态";
                """, ct, ("@r", rId), ("@v", rV), ("@a", rA));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (Npgsql.PostgresException ex) { return Conflict(new { message = ex.Message }); }
    }

    /// <summary>
    /// 付款单（批次 15）。`POST /api/orders/payment` → `usp_财务_付款`。
    /// 与收款完全对称：`usp_财务_付款` → `usp_财务_记账_付款`（FIFO 核销应付单，写 `付款核销明细`），
    /// 明细表上的 `trg_付款核销_回写应付` 负责把 `付款单.已核销金额` / `应付单.已付金额` 回写。
    /// </summary>
    private async Task<IActionResult> 保存付款(单据入参 body, CancellationToken ct)
    {
        if (是改单(body, "付款单编号"))
            return Conflict(new { message = "付款单录入即记账、不可改单：录错请用财务红冲，或另录一张" });

        var 供应商 = 取整(body.主表, "供应商编号");
        if (供应商 is null or <= 0) return BadRequest(new { message = "请选择供应商" });
        var 金额 = 取数(body.主表, "付款金额") ?? 0m;
        if (金额 <= 0) return BadRequest(new { message = "付款金额必须大于 0" });
        var 方式 = 规范方式(取文(body.主表, "付款方式"), 付款方式集);
        if (方式 is null)
            return BadRequest(new { message = $"付款方式只能是：{string.Join(" / ", 付款方式集)}" });

        try
        {
            var call = await _data.查询SqlAsync("CALL public.\"usp_财务_付款\"(@sup,@amt,@way,@day,@acc,@emp,@note,@p,@v,@a)", ct,
                ("@sup", 供应商.Value), ("@amt", 金额), ("@way", 方式),
                ("@day", 取日(body.主表, "付款日期") ?? DateTime.Today),
                ("@acc", 取整(body.主表, "账户编号")),
                ("@emp", 取整(body.主表, "经办人编号") ?? _me.员工编号),
                ("@note", 截(取文(body.主表, "备注"), 300)),
                ("@p", null), ("@v", null), ("@a", null));
            var _c = call.FirstOrDefault();
            var pId = _c is not null && _c.TryGetValue("p_付款单编号", out var _p1) && _p1 is not null ? Convert.ToInt32(_p1) : 0;
            var pV = _c is not null && _c.TryGetValue("p_凭证编号", out var _p2) ? _p2 : null;
            var pA = _c is not null && _c.TryGetValue("p_已核销金额", out var _p3) ? _p3 : null;
            var r = await _data.查询SqlAsync("""
                SELECT @p::integer AS "id",
                       (SELECT "单据号码" FROM "付款单" WHERE "付款单编号" = @p::integer) AS "单据号码",
                       @v::integer AS "凭证编号",
                       @a AS "已核销金额",
                       (SELECT "未核销金额" FROM "付款单" WHERE "付款单编号" = @p::integer) AS "未核销金额",
                       (SELECT "单据状态" FROM "付款单" WHERE "付款单编号" = @p::integer) AS "单据状态";
                """, ct, ("@p", pId), ("@v", pV), ("@a", pA));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (Npgsql.PostgresException ex) { return Conflict(new { message = ex.Message }); }
    }

    /// <summary>
    /// 费用单（批次 15）。`POST /api/orders/expense` → `usp_财务_费用`（先读参数再写，不猜）。
    ///
    /// 过程要的是 **@费用类型编码（varchar(20)，如 FREIGHT/OFFICE）**，不是 `费用类型编号`：
    /// 前端下拉的 value 是编号（`费用类型` 表的主键），所以这里做一次编号 → 编码的翻译。
    /// 过程内部会：建主表 + 建一行 `费用单明细`（科目取 `费用类型.默认科目编号`）+ 直接置
    /// `审批状态 = 2 / 是否已付 = 1` + 调 `usp_财务_记账_费用` 生成凭证。
    /// `不含税金额` 是计算列，**不传**。
    /// </summary>
    private async Task<IActionResult> 保存费用(单据入参 body, CancellationToken ct)
    {
        if (是改单(body, "费用单编号"))
            return Conflict(new { message = "费用单录入即记账、不可改单：录错请用财务红冲，或另录一张" });

        var 金额 = 取数(body.主表, "费用金额") ?? 0m;
        if (金额 <= 0) return BadRequest(new { message = "费用金额必须大于 0" });
        var 税额 = 取数(body.主表, "税额") ?? 0m;
        if (税额 < 0 || 税额 > 金额) return BadRequest(new { message = "税额不能为负，也不能大于费用金额（含税）" });

        var 编码 = 取文(body.主表, "费用类型编码")?.Trim();
        if (string.IsNullOrEmpty(编码))
        {
            var 类型编号 = 取整(body.主表, "费用类型编号");
            if (类型编号 is > 0)
                编码 = await _data.标量Async<string>(
                    "SELECT 类型编码 FROM dbo.费用类型 WHERE 费用类型编号 = @id", ct, ("@id", 类型编号.Value));
        }
        if (string.IsNullOrEmpty(编码)) return BadRequest(new { message = "请选择费用类型" });

        var 方式 = 规范方式(取文(body.主表, "付款方式"), 付款方式集);
        if (方式 is null)
            return BadRequest(new { message = $"付款方式只能是：{string.Join(" / ", 付款方式集)}" });

        // 公司编号：费用单是 NOT NULL，前端没选就取默认有效公司（集团总部优先）
        var 公司 = 取整(body.主表, "公司编号");
        if (公司 is null or <= 0)
            公司 = await _data.标量Async<int?>(
                "SELECT TOP 1 公司编号 FROM dbo.公司 WHERE 是否有效 = 1 ORDER BY 是否集团总部 DESC, 公司编号", ct);
        if (公司 is null or <= 0) return BadRequest(new { message = "没有可用的公司档案，无法录费用单" });

        try
        {
            var call = await _data.查询SqlAsync("CALL public.\"usp_财务_费用\"(@comp,@code,@amt,@tax,@way,@day,@site,@dept,@sup,@emp,@note,@e,@v)", ct,
                ("@comp", 公司.Value), ("@code", 编码), ("@amt", 金额), ("@tax", 税额), ("@way", 方式),
                ("@day", 取日(body.主表, "费用日期") ?? DateTime.Today),
                ("@site", 取整(body.主表, "网点编号")),
                ("@dept", 取整(body.主表, "部门编号")),
                ("@sup", 取整(body.主表, "供应商编号")),
                ("@emp", 取整(body.主表, "报销人编号") ?? _me.员工编号),
                ("@note", 截(取文(body.主表, "备注"), 300)),
                ("@e", null), ("@v", null));
            var _c = call.FirstOrDefault();
            var eId = _c is not null && _c.TryGetValue("p_费用单编号", out var _e1) && _e1 is not null ? Convert.ToInt32(_e1) : 0;
            var eV = _c is not null && _c.TryGetValue("p_凭证编号", out var _e2) ? _e2 : null;
            var r = await _data.查询SqlAsync("""
                SELECT @e::integer AS "id",
                       (SELECT "单据号码" FROM "费用单" WHERE "费用单编号" = @e::integer) AS "单据号码",
                       @v::integer AS "凭证编号",
                       (SELECT "费用金额" FROM "费用单" WHERE "费用单编号" = @e::integer) AS "费用金额",
                       (SELECT "审批状态" FROM "费用单" WHERE "费用单编号" = @e::integer) AS "审批状态";
                """, ct, ("@e", eId), ("@v", eV));
            return Ok(r.FirstOrDefault() ?? new Dictionary<string, object?>());
        }
        catch (Npgsql.PostgresException ex) { return Conflict(new { message = ex.Message }); }
    }

    /// <summary>
    /// 默认出库仓：客户所属网点下「可销售 + 非车仓 + 有效」的实体仓，按可用库存降序取第一个。
    /// 车仓不能直接销售出库（CK_仓库_车仓不销售），所以这里排除掉。
    /// </summary>
    private async Task<int?> 默认出库仓(int 客户编号, CancellationToken ct)
    {
        var r = await _data.查询SqlAsync("""
            SELECT TOP 1 w.仓库编号
            FROM dbo.仓库 w
            LEFT JOIN (SELECT 仓库编号, SUM(可用数量) AS 可用 FROM dbo.库存结存
                        WHERE 库存状态代码 = 'AVAIL' AND 权属代码 = 'OWN'
                        GROUP BY 仓库编号) b ON b.仓库编号 = w.仓库编号
            WHERE w.是否有效 = 1 AND w.是否车仓 = 0 AND w.是否允许销售 = 1
              AND w.所属网点编号 = (SELECT 所属网点编号 FROM dbo.客户 WHERE 客户编号 = @c)
            ORDER BY ISNULL(b.可用, 0) DESC, w.仓库编号
            """, ct, ("@c", 客户编号));
        return r.Count == 0 ? null : Convert.ToInt32(r[0]["仓库编号"]);
    }

    // ---------------- JSON 取值（主表/明细都是 JsonElement，列名按 v3） ----------------

    private static int? 取整(JsonElement e, string 名) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(名, out var v)
        && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static decimal? 取数(JsonElement e, string 名) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(名, out var v)
        && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? n : null;

    private static string? 取文(JsonElement e, string 名) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(名, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? 取日(JsonElement e, string 名) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(名, out var v)
        && v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var d) ? d.Date : null;

    [HttpDelete("{kind}/{id:int}")]
    [资源按路由("kind", "单据")]
    [需要权限("DEL")]
    public async Task<IActionResult> 删除(string kind, int id, CancellationToken ct)
    {
        if (取定义(kind) is not { } d) return BadRequest(new { message = 不可用说明(kind) });
        if (await 校验权限(kind, "DEL") is { } 拒绝) return 拒绝;
        // 批次 15：收付款/费用录入即生成凭证，删单会留下孤立凭证（凭证只能红冲，不能删），
        // 所以整类单据一律拒绝删除，把"更正走红冲"这条正路说明白。
        if (录入即记账.TryGetValue(kind, out var 不删说明))
            return Conflict(new { message = $"{d.主表}不能删除：{不删说明}" });
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (d.明细表 is not null)
        {
            await _data.查询SqlAsync($"DELETE FROM \"{d.明细表}\" WHERE \"{d.明细外键}\" = @id", ct, ("@id", id));
        }
        var n = (await _data.查询SqlAsync($"DELETE FROM \"{d.主表}\" WHERE \"{元数据.取(d.主表).主键}\" = @id RETURNING 1", ct, ("@id", id))).Count;
        await tx.CommitAsync(ct);
        return n > 0 ? Ok(new { ok = true }) : NotFound(new { message = "单据不存在" });
    }

    // ---------------- 状态机：submit / approve / post / unpost / reverse ----------------

    /// <summary>
    /// 前端这 10 个 kind 里，只有 3 个能一一对应到 v3 的真实单据：
    /// delivery→送气单、intake→到货单、stocktake→库存盘点单。
    /// 其余（借瓶/还瓶/入仓/押金/收款/付款/提成）在 v3 已换模型，没有同名单据。
    /// </summary>
    private static (string 表名, string 主键列)? 状态表(string kind) => kind switch
    {
        "delivery" => ("送气单", "送气单编号"),
        "intake" => ("到货单", "到货单编号"),
        "stocktake" => ("库存盘点单", "盘点单编号"),
        _ => null
    };

    /// <summary>
    /// 单据动作。过账一律调用库里的过程（usp_销售_出库 / usp_采购_到货过账 /
    /// usp_库存_盘点过账），绝不直接写表 —— 否则库存流水、结存、凭证会对不上。
    /// v3 没有「反过账 / 冲销」过程，这两类动作明确拒绝，避免前端误以为成功。
    ///
    /// 权限代码由**路由里的动作**决定（批次 20 收敛到过滤器 + 动态资源表）：
    /// submit→edit、approve→audit、post/unpost/qc/reverse→post；未知动作落地板 VIEW，
    /// 由下面的 400「未知动作」说明白（不要把"参数写错"伪装成"没权限"）。
    /// </summary>
    [HttpPost("{kind}/{id:int}/{动作}")]
    [资源按路由("kind", "单据")]
    [需要权限按路由("动作", "单据动作")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 状态动作(string kind, int id, string 动作, CancellationToken ct)
    {
        // 未知 kind → 400（本批验收项：不能因为抛异常变成 500）；
        // v3 已停用的 kind → 409 + 替代入口（状态机确实没有对应单据，属"当前状态不允许"）
        if (取定义(kind) is null && !已停用.ContainsKey(kind))
            return BadRequest(new { message = 未知说明(kind) });

        var 表 = 状态表(kind);
        if (表 is null)
        {
            // 录入即记账的三张财务单：动作本身不存在（不是"当前状态不允许"），单独说清
            if (录入即记账.TryGetValue(kind, out var 无动作))
                return Conflict(new { message = 无动作 });
            return Conflict(new { message = $"单据类型 {kind} 暂不支持单据动作：{不可用说明(kind)}" });
        }

        // qc = 到货单质检（批次 9）：v3 只有到货单需要质检，其它单据没有这个动作
        if (动作 is not ("submit" or "approve" or "post" or "unpost" or "reverse" or "qc"))
            return BadRequest(new { message = $"未知动作 {动作}" });

        var (表名, 主键列) = 表.Value;

        try
        {
            // 质检：usp_采购_质检 按明细的「不合格数量」推导合格数量，并把到货单置为已审核（状态 2）
            if (动作 == "qc")
            {
                if (kind != "intake")
                    return BadRequest(new { message = "只有到货单需要质检" });
                // @质检单编号 是 OUTPUT 且无默认值 —— 必须显式给变量，否则报 201「未提供该参数」
                await _data.查询SqlAsync("CALL public.\"usp_采购_质检\"(@id,NULL,@emp,NULL,NULL,@q)", ct,
                    ("@id", id), ("@emp", _me.员工编号), ("@q", null));
                return Ok(new { ok = true });
            }

            if (动作 == "post")
            {
                var 员工 = _me.员工编号;
                switch (kind)
                {
                    case "delivery":
                        await _data.查询SqlAsync("CALL public.\"usp_销售_出库\"(@id,@emp,false)", ct, ("@id", id), ("@emp", 员工));
                        break;
                    case "intake":
                        var 已质检 = await _data.标量Async<int>(
                            "SELECT CASE WHEN 质检单编号 IS NULL THEN 0 ELSE 1 END FROM dbo.到货单 WHERE 到货单编号 = @id",
                            ct, ("@id", id));
                        if (已质检 == 0)
                            await _data.查询SqlAsync("CALL public.\"usp_采购_质检\"(@id,NULL,@emp,NULL,NULL,@q)", ct,
                                ("@id", id), ("@emp", 员工), ("@q", null));
                        await _data.查询SqlAsync("CALL public.\"usp_采购_到货过账\"(@id,@emp)", ct, ("@id", id), ("@emp", 员工));
                        break;
                    default: // stocktake
                        await _data.查询SqlAsync("CALL public.\"usp_库存_盘点过账\"(@id)", ct, ("@id", id));
                        break;
                }
                return Ok(new { ok = true });
            }

            if (动作 == "reverse")
            {
                // 红冲 = 生成一张「销售退货单」（v3 的红字单据）并过账：
                //   usp_销售_退货 会把货退回仓库（SALES_RET 入库流水）、写应收冲减、生成红字凭证
                if (kind != "delivery")
                    return Conflict(new { message = $"{表名} 在 v3 没有冲销过程：到货/盘点如需回退，请用对应的红字单据" });
                return await 红冲送气单(id, await 读原因(), ct);
            }

            if (动作 == "unpost")
                return Conflict(new { message = $"{表名} 在 v3 没有反过账过程；已生成的凭证请用财务红冲（usp_财务_红冲）处理" });

            // 提交 0→1 / 审核 1→2：v3 没有通用过程，按状态机更新（过账仍走上面的过程）
            var 审核列 = 动作 == "submit" ? "" : (元数据.取(表名).有列("审核人编号")
                    ? ", \"审核人编号\" = @emp, \"审核时间\" = now()"
                    : "");
            var st = 动作 == "submit" ? 1 : 2;
            var sql = $"UPDATE \"{表名}\" SET \"单据状态\" = {st}{审核列} WHERE \"{主键列}\" = @id AND \"单据状态\" = {(动作 == "submit" ? 0 : 1)} RETURNING 1";
            var n = 审核列.Contains("@emp")
                ? (await _data.查询SqlAsync(sql, ct, ("@id", id), ("@emp", _me.员工编号))).Count
                : (await _data.查询SqlAsync(sql, ct, ("@id", id))).Count;
            if (n == 0)
                return Conflict(new { message = "当前状态不允许该操作（可能已被他人处理），请刷新后重试" });
            return Ok(new { ok = true });
        }
        catch (Npgsql.PostgresException ex)
        {
            // 过程用 THROW 给出的业务提示（已挂账 / 无明细 / 未质检 …）原样回给前端
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>读请求体里的 原因（只有 reverse 用；不挂 [FromBody]，避免给别的动作加必填参数）</summary>
    private async Task<string?> 读原因()
    {
        if (Request.ContentLength is null or 0) return null;
        using var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("原因", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 送气单红冲（批次 12）。v3 没有「反过账」，但有**销售退货单**这条正路：
    ///   建 销售退货单（原送气单编号 + 逐行 商品/批次/数量/单价/成本单价）
    ///   → `usp_销售_退货`（退回入库流水 SALES_RET、冲减应收、生成红字凭证）
    ///   → 原送气单置为「已冲销」（状态 9，与前端 9=已冲销 一致）。
    ///
    /// 约束（实测 CHECK）：退货原因只能是 其他/气量不足/送错货/质量问题/客户退单，
    /// 所以红冲用「其他」+ 退货原因说明；处理方式只能是 冲减应收/换货/退款，红冲用「冲减应收」。
    /// </summary>
    private async Task<IActionResult> 红冲送气单(int 送气单编号, string? 原因, CancellationToken ct)
    {
        var 原 = await _data.查询SqlAsync("""
            SELECT 单据号码, 客户编号, 出库仓库编号, 回收空瓶数, 单据状态, 是否已过账
            FROM dbo.送气单 WHERE 送气单编号 = @id
            """, ct, ("@id", 送气单编号));
        if (原.Count == 0) return NotFound(new { message = "送气单不存在" });

        var 已过账 = Convert.ToBoolean(原[0]["是否已过账"]);
        var 状态 = Convert.ToInt32(原[0]["单据状态"]);
        if (状态 == 9) return Conflict(new { message = "该送气单已冲销，不能重复冲销" });
        if (!已过账) return Conflict(new { message = "未过账的送气单直接删除即可，不需要冲销（没有库存与凭证要回退）" });

        var 已有 = await _data.标量Async<int>(
            "SELECT COUNT(1) FROM dbo.销售退货单 WHERE 原送气单编号 = @id AND 单据状态 <> 9",
            ct, ("@id", 送气单编号));
        if (已有 > 0) return Conflict(new { message = "该送气单已有退货单，不能重复冲销" });

        var 行 = await _data.查询SqlAsync("""
            SELECT 商品编号, 批次编号, 数量, 单价, 成本单价
            FROM dbo.送气单明细 WHERE 送气单编号 = @id AND 数量 > 0 ORDER BY 行号
            """, ct, ("@id", 送气单编号));
        if (行.Count == 0) return Conflict(new { message = "原单没有可退的明细，无法冲销" });

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var 号 = await _data.查询SqlAsync("CALL public.\"usp_取单据编号\"('sales_return',@no,@d)", ct, ("@no", null), ("@d", DateTime.Today));
            var _nh = 号.FirstOrDefault();
            var 单据号码 = _nh is not null && _nh.TryGetValue("p_单据编号", out var _nv) && _nv is not null ? Convert.ToString(_nv) : "";

            var 退货单编号 = await _data.新增Async("销售退货单", new Dictionary<string, object?>
            {
                ["单据号码"] = 单据号码,
                ["客户编号"] = 原[0]["客户编号"],
                ["原送气单编号"] = 送气单编号,
                ["入库仓库编号"] = 原[0]["出库仓库编号"],
                ["退货日期"] = DateTime.Today,
                ["退货原因"] = "其他",                      // CHECK 只允许 其他/气量不足/送错货/质量问题/客户退单
                ["退货原因说明"] = string.IsNullOrWhiteSpace(原因) ? "整单红冲" : 原因,
                ["处理方式"] = "冲减应收",
                ["退货数量"] = 0, ["退货金额"] = 0, ["退款金额"] = 0, ["冲减应收金额"] = 0,
                ["回收空瓶数"] = 原[0]["回收空瓶数"] ?? 0,
                ["是否已过账"] = false,
                ["单据状态"] = 0,
                ["经办人编号"] = _me.员工编号,
                ["创建人"] = _me.员工编号
            }, ct);

            var 行号 = 1;
            foreach (var l in 行)
            {
                await _data.新增Async("销售退货单明细", new Dictionary<string, object?>
                {
                    ["退货单编号"] = 退货单编号,
                    ["行号"] = 行号++,
                    ["商品编号"] = l["商品编号"],
                    ["批次编号"] = l["批次编号"],
                    ["退货数量"] = l["数量"],
                    ["单价"] = l["单价"],
                    ["成本单价"] = l["成本单价"],
                    ["回库状态"] = "AVAIL",
                    ["权属代码"] = "OWN"
                }, ct);
            }

            // 过账退货单：退回入库流水 + 应收冲减 + 红字凭证（过程内部带事务）
            await _data.查询SqlAsync("CALL public.\"usp_销售_退货\"(@id,@emp)", ct, ("@id", 退货单编号), ("@emp", _me.员工编号));

            // 原单置「已冲销」，避免重复冲销与报表重复统计
            await _data.查询SqlAsync("UPDATE \"送气单\" SET \"单据状态\" = 9 WHERE \"送气单编号\" = @id", ct, ("@id", 送气单编号));

            var 结果 = await _data.查询SqlAsync(
                "SELECT 退货数量, 退货金额, 凭证编号 FROM dbo.销售退货单 WHERE 退货单编号 = @id",
                ct, ("@id", 退货单编号));

            await tx.CommitAsync(ct);
            return Ok(new
            {
                ok = true,
                id = 退货单编号,
                单据号码,
                退货数量 = 结果[0]["退货数量"],
                退货金额 = 结果[0]["退货金额"],
                凭证编号 = 结果[0]["凭证编号"]
            });
        }
        catch (Npgsql.PostgresException ex)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { message = ex.Message });
        }
    }

    // ---------------- 自动带价 ----------------

    /// <summary>
    /// 取价：销售类走 usp_取商品价格（客户协议价 → 价格方案 → 标准售价），
    /// 采购（intake）走 usp_取采购价。前端参数名是 v2 的「规格编号」，v3 对应「商品编号」。
    /// 取不到价返回 单价=null（前端 silent 留空，不报错）。
    /// </summary>
    [HttpGet("{kind}/price")]
    [资源按路由("kind", "单据")]
    [需要权限("VIEW")]
    public async Task<IActionResult> 取价(
        string kind, CancellationToken ct,
        [FromQuery(Name = "客户编号")] int? 客户编号 = null,
        [FromQuery(Name = "供应商编号")] int? 供应商编号 = null,
        [FromQuery(Name = "规格编号")] int? 规格编号 = null,
        [FromQuery(Name = "商品编号")] int? 商品编号 = null,
        [FromQuery(Name = "数量")] int 数量 = 1)
    {
        var 商品 = 商品编号 ?? 规格编号;
        if (商品 is null or <= 0)
            return Ok(new { 单价 = (decimal?)null, 取价来源 = "", 是否命中 = false });

        if (kind == "intake")
        {
            var 供应商 = 供应商编号 ?? 客户编号;
            if (供应商 is null or <= 0)
                return Ok(new { 单价 = (decimal?)null, 取价来源 = "", 是否命中 = false });

            var call = await _data.查询SqlAsync("CALL public.\"usp_取采购价\"(@sid,@pid,@qty,@day,@p,@tax,@src)", ct,
                ("@sid", 供应商.Value), ("@pid", 商品.Value), ("@qty", Math.Max(1, 数量)), ("@day", DateTime.Today),
                ("@p", null), ("@tax", null), ("@src", null));
            var r = call.FirstOrDefault() ?? new Dictionary<string, object?>();
            object? P = r.TryGetValue("p_含税单价", out var a1) ? a1 : null;
            object? tax = r.TryGetValue("p_税率", out var a2) ? a2 : null;
            object? src = r.TryGetValue("p_取价来源", out var a3) ? a3 : null;
            return Ok(new { 单价 = P, 税率 = tax, 取价来源 = src, 是否命中 = P is not null });
        }

        var call2 = await _data.查询SqlAsync("CALL public.\"usp_取商品价格\"(@cid,@pid,@qty,@day,@p,@src,@srcId,@hit,@low)", ct,
            ("@cid", 客户编号 ?? 0), ("@pid", 商品.Value), ("@qty", Math.Max(1, 数量)), ("@day", DateTime.Today),
            ("@p", null), ("@src", null), ("@srcId", null), ("@hit", null), ("@low", null));
        var rr = call2.FirstOrDefault() ?? new Dictionary<string, object?>();
        object? PP = rr.TryGetValue("p_单价", out var b1) ? b1 : null;
        object? SS = rr.TryGetValue("p_取价来源", out var b2) ? b2 : null;
        object? SI = rr.TryGetValue("p_来源编号", out var b3) ? b3 : null;
        object? HH = rr.TryGetValue("p_是否命中", out var b4) ? b4 : null;
        object? LL = rr.TryGetValue("p_最低售价", out var b5) ? b5 : null;
        return Ok(new { 单价 = PP, 取价来源 = SS, 来源编号 = SI, 是否命中 = HH, 最低售价 = LL });
    }
}

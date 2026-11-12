using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using GasInventory.Infrastructure.Persistence;

namespace GasInventory.Infrastructure.Data;

/// <summary>通用查询结果</summary>
public sealed record 分页结果(long 总数, List<Dictionary<string, object?>> 数据);

/// <summary>
/// 元数据驱动的通用数据访问（PostgreSQL / Npgsql 版）。
/// 表名与列名只允许来自《元数据.全部》白名单；值全部走参数；自增主键自动忽略。
/// </summary>
public class 数据服务
{
    private readonly GasInventoryDbContext _db;
    // bit/boolean 列名（用于把 T-SQL 的 [col]=1/0 改写成 = true/false）
    private static readonly string[] BitCols = new[] { "enabled", "finitiator", "has_restricted_text", "is_damaged", "is_forced_plan", "is_internal_query", "is_online_index_plan", "is_parallel_plan", "is_part_of_encrypted_module", "is_replication_specific", "is_trivial_plan", "notify_level_eventlog", "保质期管理", "仓库有效", "初始密码是否已改", "含税", "核算部门", "核算供应商", "核算客户", "核算商品", "核算员工", "是否必填", "是否采购", "是否超额预警", "是否超级管理员", "是否车仓", "是否成功", "是否冲销", "是否存在自有瓶", "是否单据", "是否导出", "是否叠加", "是否多选", "是否分组", "是否钢瓶资产", "是否含税", "是否合计", "是否黑名单", "是否红冲", "是否基本单位", "是否集团总部", "是否计入可用", "是否计入应发", "是否计入资产", "是否计税", "是否阶梯", "是否结转损益", "是否经过在途", "是否可出库", "是否可导出", "是否可维护", "是否可钻取", "是否跨区", "是否跨网点", "是否来自提成", "是否临时改价", "是否盲盘", "是否敏感", "是否明细", "是否默认", "是否默认收付", "是否排序", "是否启用", "是否气品", "是否入库", "是否沙箱", "是否税前扣除", "是否司机", "是否送瓶员", "是否损益科目", "是否停用", "是否同意", "是否退货单", "是否退款", "是否往来", "是否系统", "是否显示", "是否现金科目", "是否销售", "是否需发票", "是否需审批", "是否需要钢瓶编号", "是否需要人工介入", "是否一物一码", "是否已处理", "是否已导入", "是否已登记销毁", "是否已调整", "是否已付", "是否已过账", "是否已记账", "是否已结账", "是否已开单", "是否已收赔", "是否已授权", "是否已映射", "是否已用", "是否银行科目", "是否影响送达", "是否有效", "是否逾期未检", "是否月结", "是否允许导出", "是否允许负库存", "是否允许离线", "是否允许销售", "是否允许钻取", "是否在用", "是否在职", "是否制造盘亏", "是否主条码", "是否主要联系人", "是否准时", "是否自产", "是否走配送", "是否作废", "验证码是否通过", "已开通登录", "已完成", "已转订单", "状态" };
    private static readonly System.Text.RegularExpressions.Regex _bitEq = new(
        "(\")?\\b(" + string.Join("|", BitCols.Select(System.Text.RegularExpressions.Regex.Escape)) + ")\\b(\")?\\s*=\\s*([01])(?![\\d.])",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    // 含 ASCII 大写的列/表名（PG 未加引号会折叠成小写导致不匹配）→ 自动加双引号
    private static readonly string[] CaseIdents = new[] { "AppId","AppKey","AppKey状态","AppSecret","App版本","GPS设备号","GPS里程公里","IP归属地","IP白名单","MIME类型","Secret前缀","Secret哈希","UserAgent","会话ID","同IP近1小时失败次数","客户端IP","GPS轨迹" };
    private static readonly System.Text.RegularExpressions.Regex _caseIdent = new("(?<![\\w\"])(" + string.Join("|", CaseIdents.Select(System.Text.RegularExpressions.Regex.Escape)) + ")(?![\\w\"])", System.Text.RegularExpressions.RegexOptions.Compiled);

    public 数据服务(GasInventoryDbContext db) => _db = db;

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        return conn;
    }

    private NpgsqlCommand 建命令(string sql, NpgsqlConnection conn)
    {
        var cmd = new NpgsqlCommand(sql, conn);
        var tx = _db.Database.CurrentTransaction;
        if (tx is not null) cmd.Transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        return cmd;
    }

    private static readonly Regex _中文方括号 = new(@"\[([^\[\]]*[\u4e00-\u9fff][^\[\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex _dbo点 = new(@"\bdbo\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _N前缀 = new(@"\bN'", RegexOptions.Compiled);
    private static readonly Regex _isnull = new(@"\bISNULL\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _len = new(@"\bLEN\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _getdate = new(@"\bGETDATE\s*\(\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _sysutc = new(@"\bSYSUTCDATETIME\s*\(\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _sysdt = new(@"\bSYSDATETIME\s*\(\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _dbname = new(@"\bDB_NAME\s*\(\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>把控制器里沿用的 T-SQL 方言在运行时归一化为 PG 方言（只作用于 SQL 字符串，不动 C#）。</summary>
    public static string 转Sql(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return sql;
        var s = _中文方括号.Replace(sql, "\"$1\"");
        s = _dbo点.Replace(s, "");
        s = _N前缀.Replace(s, "'");
        s = _isnull.Replace(s, "COALESCE(");
        s = _len.Replace(s, "length(");
        s = _getdate.Replace(s, "now()");
        s = _sysutc.Replace(s, "now()");
        s = _sysdt.Replace(s, "now()");
        s = _dbname.Replace(s, "current_database()");
        s = s.Replace("@@SERVICENAME", "current_database()");
        s = s.Replace("@@SERVERNAME", "current_database()");
        s = Regex.Replace(s, @"\bnvarchar\b", "varchar", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bnchar\b", "char", RegexOptions.IgnoreCase);
        s = _bitEq.Replace(s, m => "\"" + m.Groups[2].Value + "\" = " + (m.Groups[4].Value == "1" ? "true" : "false"));
        s = Regex.Replace(s, @"\bDATEDIFF\s*\(\s*(year|quarter|month|week|day|hour|minute|second|yy|yyyy|qq|mm|wk|dd|hh|mi|ss)\s*,", m => "public.datediff('" + m.Groups[1].Value.ToLower() + "',", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bDATEADD\s*\(\s*(year|quarter|month|week|day|hour|minute|second|yy|yyyy|qq|mm|wk|dd|hh|mi|ss)\s*,", m => "public.dateadd('" + m.Groups[1].Value.ToLower() + "',", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?i)\bCONVERT\s*\(\s*(?:nvarchar|varchar|nchar|char)\s*\(\s*(\d+)\s*\)\s*,\s*([^,()]+?)\s*(?:,\s*(\d+)\s*)?\)", m =>
        {
            var len = m.Groups[1].Value; var expr = m.Groups[2].Value.Trim();
            if (m.Groups[3].Success)
            {
                var fmt = m.Groups[3].Value switch { "23" => "YYYY-MM-DD", "120" => "YYYY-MM-DD HH24:MI:SS", "112" => "YYYYMMDD", "108" => "HH24:MI:SS", "101" => "MM/DD/YYYY", "111" => "YYYY/MM/DD", _ => "YYYY-MM-DD" };
                return "to_char(" + expr + ", '" + fmt + "')";
            }
            return "CAST(" + expr + " AS varchar(" + len + "))";
        });
        s = Regex.Replace(s, @"(?i)\bCONVERT\s*\(\s*(int|bigint|smallint|bit|date|datetime2?|decimal\s*\(\s*[\d,]+\s*\)|numeric\s*\(\s*[\d,]+\s*\))\s*,\s*([^,()]+?)\s*\)", m =>
        {
            var ty = m.Groups[1].Value.ToLower().Replace(" ", "");
            if (ty.StartsWith("decimal") || ty.StartsWith("numeric")) { }
            else if (ty == "int") ty = "integer";
            else if (ty == "bit") ty = "boolean";
            else if (ty.StartsWith("datetime")) ty = "timestamp";
            return "CAST(" + m.Groups[2].Value.Trim() + " AS " + ty + ")";
        });
        s = QuoteCaseIdents(s);
        s = ConvertTop(s);
        return s;
    }
    private static string QuoteCaseIdents(string sql)
    {
        var sb = new System.Text.StringBuilder(sql.Length + 16);
        int i = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '\'')
            {
                int j = i + 1;
                while (j < sql.Length) { if (sql[j] == '\'') { if (j + 1 < sql.Length && sql[j + 1] == '\'') { j += 2; continue; } else { j++; break; } } else j++; }
                sb.Append(sql, i, j - i); i = j;
            }
            else
            {
                int j = sql.IndexOf('\'', i); if (j < 0) j = sql.Length;
                sb.Append(_caseIdent.Replace(sql.Substring(i, j - i), m => "\"" + m.Groups[1].Value + "\""));
                i = j;
            }
        }
        return sb.ToString();
    }
    private static string ConvertTop(string sql)
    {
        while (true)
        {
            var m = Regex.Match(sql, @"(?i)\bSELECT\s+TOP\s*\(?\s*(\d+)\s*\)?\s+");
            if (!m.Success) break;
            int n = int.Parse(m.Groups[1].Value);
            int afterStart = m.Index + m.Length;
            int depth = 0; int insertAt = -1;
            for (int k = afterStart; k < sql.Length; k++)
            {
                char c = sql[k];
                if (c == '\'') { k++; while (k < sql.Length) { if (sql[k] == '\'') { if (k + 1 < sql.Length && sql[k + 1] == '\'') { k += 2; continue; } else { break; } } k++; } }
                else if (c == '(') depth++;
                else if (c == ')') { if (depth == 0) { insertAt = k; break; } else depth--; }
                else if (c == ';' && depth == 0) { insertAt = k; break; }
            }
            if (insertAt < 0) insertAt = sql.Length;
            string mid = sql.Substring(afterStart, insertAt - afterStart);
            string tail = sql.Substring(insertAt);
            sql = sql.Substring(0, m.Index) + "SELECT " + mid + " LIMIT " + n + tail;
        }
        return sql;
    }
    private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> _callTypes = new();
    private static async System.Threading.Tasks.Task<System.Collections.Generic.List<string>?> CallArgTypesAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        var mm = Regex.Match(sql, @"(?is)^\s*CALL\s+(?:public\.)?[""']?([^""'\s(]+)[""']?\s*\(");
        if (!mm.Success) return null;
        var name = mm.Groups[1].Value;
        if (_callTypes.TryGetValue(name, out var cached)) return cached;
        await using var c = new NpgsqlCommand("SELECT pg_get_function_identity_arguments(p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname=@pn LIMIT 1", conn);
        c.Parameters.Add(new NpgsqlParameter("pn", name));
        string? sig = null;
        await using (var rd = await c.ExecuteReaderAsync(ct)) { if (await rd.ReadAsync(ct)) sig = rd.IsDBNull(0) ? null : rd.GetString(0); }
        var types = new System.Collections.Generic.List<string>();
        if (sig != null)
        {
            var parts = new System.Collections.Generic.List<string>(); int depth = 0; var cur = new System.Text.StringBuilder();
            foreach (var ch in sig) { if (ch == '(') depth++; else if (ch == ')') depth--; if (ch == ',' && depth == 0) { parts.Add(cur.ToString()); cur.Clear(); } else cur.Append(ch); }
            if (cur.Length > 0) parts.Add(cur.ToString());
            foreach (var part in parts)
            {
                var m2 = Regex.Match(part, @"\b(integer|bigint|smallint|numeric|decimal|character varying|varchar|text|boolean|bool|date|timestamp without time zone|timestamp with time zone|timestamp|uuid|bytea|double precision|real)\b");
                types.Add(m2.Success ? m2.Value : "");
            }
        }
        _callTypes[name] = types;
        return types;
    }
    private static NpgsqlTypes.NpgsqlDbType? MapPgType(string t)
    {
        switch (t)
        {
            case "integer": return NpgsqlTypes.NpgsqlDbType.Integer;
            case "bigint": return NpgsqlTypes.NpgsqlDbType.Bigint;
            case "smallint": return NpgsqlTypes.NpgsqlDbType.Smallint;
            case "numeric": case "decimal": return NpgsqlTypes.NpgsqlDbType.Numeric;
            case "character varying": case "varchar": return NpgsqlTypes.NpgsqlDbType.Varchar;
            case "text": return NpgsqlTypes.NpgsqlDbType.Text;
            case "boolean": case "bool": return NpgsqlTypes.NpgsqlDbType.Boolean;
            case "date": return NpgsqlTypes.NpgsqlDbType.Date;
            case "timestamp": case "timestamp without time zone": return NpgsqlTypes.NpgsqlDbType.Timestamp;
            case "timestamp with time zone": return NpgsqlTypes.NpgsqlDbType.TimestampTz;
            case "uuid": return NpgsqlTypes.NpgsqlDbType.Uuid;
            case "bytea": return NpgsqlTypes.NpgsqlDbType.Bytea;
            case "double precision": return NpgsqlTypes.NpgsqlDbType.Double;
            case "real": return NpgsqlTypes.NpgsqlDbType.Real;
        }
        return null;
    }
    private static async System.Threading.Tasks.Task AddParamsAsync(NpgsqlCommand cmd, string sql, NpgsqlConnection conn, (string 名, object? 值)[] ps, CancellationToken ct)
    {
        System.Collections.Generic.List<string>? types = null;
        var posByName = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        if (Regex.IsMatch(sql, @"(?is)^\s*CALL\b"))
        {
            types = await CallArgTypesAsync(conn, sql, ct);
            if (types != null)
            {
                var mm = Regex.Match(sql, @"(?is)^\s*CALL\s+(?:public\.)?[""']?[^""'\s(]+[""']?\s*\(");
                if (mm.Success)
                {
                    var args = SplitCallArgs(sql, mm.Index + mm.Length, out _);
                    for (int i2 = 0; i2 < args.Count; i2++)
                    {
                        var am = Regex.Match(args[i2].Trim(), @"^@([A-Za-z_][A-Za-z0-9_]*)$");
                        if (am.Success) posByName[am.Groups[1].Value] = i2;
                    }
                }
            }
        }
        foreach (var (n, v) in ps)
        {
            var name = (n ?? "").TrimStart('@');
            object? val = v ?? DBNull.Value;
            var prm = new NpgsqlParameter(name, val);
            if (types != null && posByName.TryGetValue(name, out var idx) && idx < types.Count)
            {
                var mt = MapPgType(types[idx]);
                if (mt.HasValue)
                {
                    prm.NpgsqlDbType = mt.Value;
                    if (mt.Value == NpgsqlTypes.NpgsqlDbType.Smallint && val is int iv) prm.Value = (short)iv;
                    else if (mt.Value == NpgsqlTypes.NpgsqlDbType.Integer && val is short sv) prm.Value = (int)sv;
                }
            }
            cmd.Parameters.Add(prm);
        }
    }

    private static System.Collections.Generic.List<string> SplitCallArgs(string s, int start, out int end)
    {
        var list = new System.Collections.Generic.List<string>();
        int depth = 0; var cur = new System.Text.StringBuilder(); int i = start;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\'')
            {
                cur.Append(c); i++;
                while (i < s.Length)
                {
                    cur.Append(s[i]);
                    if (s[i] == '\'') { if (i + 1 < s.Length && s[i + 1] == '\'') { cur.Append(s[i + 1]); i += 2; continue; } break; }
                    i++;
                }
                continue;
            }
            if (c == '(') { depth++; cur.Append(c); }
            else if (c == ')') { if (depth == 0) { list.Add(cur.ToString()); end = i + 1; return list; } depth--; cur.Append(c); }
            else if (c == ',' && depth == 0) { list.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        list.Add(cur.ToString()); end = i; return list;
    }

    /// <summary>分页查询。filters 为"列名=值"的等值条件（值支持逗号分隔的多值 IN）。</summary>
    public async Task<分页结果> 查询Async(
        string 表名,
        IDictionary<string, string?> filters,
        string? keyword,
        int page,
        int size,
        string? orderBy,
        bool desc,
        (string 列, DateTime? 起, DateTime? 止)? 日期区间,
        CancellationToken ct = default)
    {
        var m = 元数据.取(表名);
        var where = new List<string>();
        var ps = new List<NpgsqlParameter>();
        var p = 0;

        foreach (var (k, v) in filters)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            if (!m.有列(k)) continue;
            var vals = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (vals.Length == 1)
            {
                where.Add($"\"{k}\" = @p{p}");
                ps.Add(new NpgsqlParameter($"p{p}", 转换值(m.取列(k)!, vals[0]) ?? DBNull.Value));
            }
            else
            {
                var names = new List<string>();
                foreach (var one in vals)
                {
                    names.Add($"@p{p}");
                    ps.Add(new NpgsqlParameter($"p{p}", 转换值(m.取列(k)!, one) ?? DBNull.Value));
                    p++;
                }
                where.Add($"\"{k}\" IN ({string.Join(",", names)})");
            }
            p++;
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var textCols = m.列.Where(c => c.类型 is "VARCHAR" or "LONGCHAR").Select(c => c.名称).ToList();
            if (textCols.Count > 0)
            {
                var or = textCols.Select(c => $"\"{c}\" LIKE @kw");
                where.Add("(" + string.Join(" OR ", or) + ")");
                ps.Add(new NpgsqlParameter("kw", $"%{keyword.Trim()}%"));
            }
        }

        if (日期区间 is { } d && m.有列(d.列))
        {
            if (d.起 is not null) { where.Add($"\"{d.列}\" >= @d1"); ps.Add(new NpgsqlParameter("d1", d.起)); }
            if (d.止 is not null) { where.Add($"\"{d.列}\" < @d2"); ps.Add(new NpgsqlParameter("d2", d.止.Value.Date.AddDays(1))); }
        }

        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var sortCol = !string.IsNullOrWhiteSpace(orderBy) && m.有列(orderBy) ? orderBy : m.主键;
        var direction = desc ? "DESC" : "ASC";

        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);

        var sql = $"""
            SELECT COUNT(1) FROM "{m.名称}" {whereSql};
            SELECT * FROM "{m.名称}" {whereSql}
              ORDER BY "{sortCol}" {direction}
              OFFSET {(page - 1) * size} ROWS FETCH NEXT {size} ROWS ONLY;
            """;

        var conn = await OpenAsync(ct);
        await using var cmd = 建命令(sql, conn);
        cmd.Parameters.AddRange(ps.ToArray());

        long total;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            total = await r.ReadAsync(ct) ? Convert.ToInt64(r.GetValue(0)) : 0;
            await r.NextResultAsync(ct);
            var rows = await 读行Async(r, ct);
            return new 分页结果(total, rows);
        }
    }

    public async Task<Dictionary<string, object?>?> 按主键Async(string 表名, object id, CancellationToken ct = default)
    {
        var m = 元数据.取(表名);
        var conn = await OpenAsync(ct);
        await using var cmd = 建命令($"SELECT * FROM \"{m.名称}\" WHERE \"{m.主键}\" = @id", conn);
        cmd.Parameters.Add(new NpgsqlParameter("id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var rows = await 读行Async(r, ct);
        return rows.FirstOrDefault();
    }

    public async Task<int> 新增Async(string 表名, IDictionary<string, object?> values, CancellationToken ct = default)
    {
        var m = 元数据.取(表名);
        var (cols, ps) = 取写入列(m, values, 排除主键: m.自增);
        if (cols.Count == 0) throw new ArgumentException("没有可写入的列");

        var sql = $"INSERT INTO \"{m.名称}\" ({string.Join(",", cols.Select(c => $"\"{c}\""))}) " +
                  $"VALUES ({string.Join(",", ps.Select(x => "@" + x.ParameterName))}) " +
                  $"RETURNING \"{m.主键}\"";

        var conn = await OpenAsync(ct);
        await using var cmd = 建命令(sql, conn);
        cmd.Parameters.AddRange(ps.ToArray());
        var id = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(id);
    }

    public async Task<int> 修改Async(string 表名, object id, IDictionary<string, object?> values, CancellationToken ct = default)
    {
        var m = 元数据.取(表名);
        var (cols, ps) = 取写入列(m, values, 排除主键: true);
        if (cols.Count == 0) return 0;
        var sets = string.Join(",", cols.Select((c, i) => $"\"{c}\" = @{ps[i].ParameterName}"));
        var sql = $"UPDATE \"{m.名称}\" SET {sets} WHERE \"{m.主键}\" = @__id";
        ps.Add(new NpgsqlParameter("__id", id));

        var conn = await OpenAsync(ct);
        await using var cmd = 建命令(sql, conn);
        cmd.Parameters.AddRange(ps.ToArray());
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> 删除Async(string 表名, object id, CancellationToken ct = default)
    {
        var m = 元数据.取(表名);
        var conn = await OpenAsync(ct);
        await using var cmd = 建命令($"DELETE FROM \"{m.名称}\" WHERE \"{m.主键}\" = @id", conn);
        cmd.Parameters.Add(new NpgsqlParameter("id", id));
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> 最大单号Async(string 表名, string 列, string 前缀, CancellationToken ct = default)
    {
        var m = 元数据.取(表名);
        var conn = await OpenAsync(ct);
        await using var cmd = 建命令($"SELECT MAX(\"{列}\") FROM \"{m.名称}\" WHERE \"{列}\" LIKE @pre", conn);
        cmd.Parameters.Add(new NpgsqlParameter("pre", 前缀 + "%"));
        return (await cmd.ExecuteScalarAsync(ct)) as string;
    }

    public async Task<long> 计数Async(string 表名, string whereSql = "", CancellationToken ct = default)
    {
        var m = 元数据.取(表名);
        var conn = await OpenAsync(ct);
        await using var cmd = 建命令(
            $"SELECT COUNT(1) FROM \"{m.名称}\" " + (string.IsNullOrWhiteSpace(whereSql) ? "" : "WHERE " + whereSql), conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>自由查询（报表/看板用）。</summary>
    public async Task<List<Dictionary<string, object?>>> 查询SqlAsync(
        string sql, CancellationToken ct = default, params (string 名, object? 值)[] ps)
    {
        var conn = await OpenAsync(ct);
        await using var cmd = 建命令(转Sql(sql), conn);
        await AddParamsAsync(cmd, sql, conn, ps, ct);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await 读行Async(r, ct);
    }

    /// <summary>自由查询单值</summary>
    public async Task<T?> 标量Async<T>(string sql, CancellationToken ct = default, params (string 名, object? 值)[] ps)
    {
        var conn = await OpenAsync(ct);
        await using var cmd = 建命令(转Sql(sql), conn);
        await AddParamsAsync(cmd, sql, conn, ps, ct);
        var o = await cmd.ExecuteScalarAsync(ct);
        if (o is null || o is DBNull) return default;
        var 目标 = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(o, 目标);
    }

    // ---------- 内部工具 ----------

    private static (List<string> cols, List<NpgsqlParameter> ps) 取写入列(
        表元数据 m, IDictionary<string, object?> values, bool 排除主键)
    {
        var cols = new List<string>();
        var ps = new List<NpgsqlParameter>();
        var i = 0;
        foreach (var (k, v) in values)
        {
            if (!m.有列(k)) continue;
            if (排除主键 && k == m.主键) continue;
            cols.Add(k);
            ps.Add(new NpgsqlParameter("w" + i, 转换值(m.取列(k)!, v) ?? DBNull.Value));
            i++;
        }
        return (cols, ps);
    }

    private static object? 转换值(列元数据 col, object? v)
    {
        if (v is null) return null;
        var s = v.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        switch (col.类型)
        {
            case "INTEGER":
            case "COUNTER":
            case "SHORT":
            case "BYTE":
                return int.Parse(s, CultureInfo.InvariantCulture);
            case "CURRENCY":
            case "DOUBLE":
            case "DECIMAL":
            case "NUMERIC":
                return decimal.Parse(s, CultureInfo.InvariantCulture);
            case "DATETIME":
                return DateTime.Parse(s, CultureInfo.InvariantCulture);
            case "BIT":
                return s is "1" or "true" or "True" or "是" or "on";
            default:
                return s;
        }
    }

    private static async Task<List<Dictionary<string, object?>>> 读行Async(NpgsqlDataReader r, CancellationToken ct)
    {
        var list = new List<Dictionary<string, object?>>();
        while (await r.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(r.FieldCount);
            for (var i = 0; i < r.FieldCount; i++)
            {
                var v = await r.IsDBNullAsync(i, ct) ? null : r.GetValue(i);
                row[r.GetName(i)] = v switch
                {
                    DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                    decimal dec => dec,
                    _ => v
                };
            }
            list.Add(row);
        }
        return list;
    }
}

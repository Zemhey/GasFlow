using GasInventory.Infrastructure.Data;

namespace GasInventory.Api.Services;

/// <summary>
/// 单据编号服务：前缀 + 日期(8) + 当日流水(3) ，如 送气20261018001。
/// 依赖 UX_送气单_单据号码 等唯一索引兜底，冲突时重试。
/// </summary>
public class 单号服务
{
    private readonly 数据服务 _data;

    public 单号服务(数据服务 data) => _data = data;

    public async Task<string> 生成Async(string 表名, string 前缀, DateTime 日期, string 列 = "单据号码")
    {
        var day = 日期.ToString("yyyyMMdd");
        var head = $"{前缀}{day}";
        var max = await _data.最大单号Async(表名, 列, head);
        var seq = 1;
        if (!string.IsNullOrEmpty(max) && max.Length > head.Length &&
            int.TryParse(max[head.Length..], out var n)) seq = n + 1;
        return head + seq.ToString("000");
    }
}

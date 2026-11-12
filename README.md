<div align="center">

# GasFlow

**LPG 瓶装液化气配送与钢瓶资产 ERP**

销售 · 配送 · 签收闭环 ｜ 钢瓶一物一码 ｜ 五维库存 ｜ 凭证 · 应收应付 · 月结

![Vue](https://img.shields.io/badge/Vue-3.5-42b883?logo=vue.js&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-5.x-3178c6?logo=typescript&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9.0-512bd4?logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169e1?logo=postgresql&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-green)

</div>

---

## 这是什么

一个面向**瓶装液化气（LPG）经营企业**的全流程进销存与配送管理系统。它不是通用进销存套壳，
而是围绕瓶装气行业特有的三件事设计的：

1. **气是称重的，瓶是要回收的** —— 销售单位是"瓶"，计价基准是"公斤"，空瓶必须回收并核算押金；
2. **钢瓶是受监管的资产** —— 每只钢瓶有唯一编号、检验周期与使用寿命，超期未检不能充装；
3. **配送是最后一百米** —— 装车 → 出车 → 送达签收 → 回场清点，车仓余瓶与差异必须当日结清。

## 功能域

| 域 | 能力 |
|---|---|
| **销售** | 下单 → 审核 → 生成送气单 → 出库 → 签收 → 应收；红冲走销售退货单；客户对账单、押金台账 |
| **采购** | 到货登记（可无订单直采）→ 质检 → 过账入库（自动写库存流水 + 应付 + 凭证）→ 付款核销 |
| **库存** | 五维结存（仓库 × 商品 × 批次 × 状态 × 权属）、出入库流水、调拨（在途两段式）、盘盈盘亏调整、冻结/解冻、安全库存预警 |
| **配送** | 计划 → 转任务 → 装车 → 出车 → 送达签收 → 回场清点/差异盘点；配送台账与考核指标 |
| **钢瓶** | 一物一码批量建档、检验记录（**只增不改**）、报废单、遗失单、仓间移动、流转轨迹、检验提醒 |
| **财务** | 收款/付款/费用（录入即核销 + 生成凭证）、手工凭证、过账与红冲、科目余额、期间月结/反月结/结转损益、移动加权成本 |
| **报表** | 驾驶舱、经营毛利、账龄、盘点差异、配送清单、客户预警、对账单、报表中心 |
| **权限** | 岗位 × 资源 × 权限矩阵；PBKDF2 口令、登录锁定、会话管理、操作日志 |
| **打印** | 送气单三联、人民币大写、签字栏（`/print/orders/{kind}/{id}`） |

## 技术栈

```
Vue 3 + TypeScript + Vite        ← 配置驱动前端（modules.ts 描述字段，通用列表页/编辑页渲染）
    ↓ axios，/api 前缀
ASP.NET Core 9                   ← 控制器很薄；Cookie 认证 + 全局权限过滤器
    ↓ 两条数据通路
    ① 元数据驱动的通用 CRUD 引擎（表名/列名白名单 + 全参数化 SQL）
    ② EF Core DbContext（认证 / 事务 / 权限矩阵）
    ↓
PostgreSQL（Npgsql）
```

**关键设计：配置驱动。** 新增一个单据类型或档案表，多数情况下只需要改前端 `modules.ts` 的字段定义
和后端的三处登记表，不必写新的 Vue 页面。

**关键设计：权限是真拦的。** 资源名在三处登记（前端路由表 / 动态 kind·table 映射 / 控制器特性），
启动时与数据库 `权限资源` 逐一比对，漂移会在日志里 warn；接口级越权返回 403 并点名缺哪个资源与权限码。

## 目录结构

```
GasFlow/
├── backend/
│   ├── GasInventory.slnx
│   └── src/
│       ├── GasInventory.Api/            ← 控制器、服务、认证、权限过滤器、Program.cs
│       ├── GasInventory.Domain/         ← 实体（与库表一一对应，字段名用中文）
│       └── GasInventory.Infrastructure/ ← 通用数据服务、表元数据白名单、DbContext
├── frontend/
│   └── src/
│       ├── config/modules.ts            ← 前端配置中枢：单据字段 + 档案表 + 菜单
│       ├── views/                       ← 页面（含通用列表页/编辑页）
│       ├── api/                         ← 全站唯一 API 封装处
│       ├── stores/ · router/ · layouts/
│       └── styles/
├── db/                                  ← 建库脚本与工具（见下方"数据库"一节）
└── docs/                                ← 方案设计、实施说明、页面截图
```

## 快速开始

### 前置要求

| 组件 | 版本 | 用途 |
|---|---|---|
| .NET SDK | 9.0 | 运行后端 |
| Node.js | 18+ | 构建前端（**仅构建期需要**，运行期不需要） |
| PostgreSQL | 14+ | 数据库 |
| Python | 3.10+ | 建库 / 灌演示数据脚本 |

### 1. 数据库

```bash
# 建库并创建账号（按需调整口令）
psql -U postgres -c "CREATE DATABASE gasinventory;"
```

连接串在 `backend/src/GasInventory.Api/appsettings.json`：

```json
"ConnectionStrings": {
  "Default": "Host=localhost;Port=5432;Database=gasinventory;Username=postgres;Password=your-password"
}
```

> ⚠ **请勿提交真实口令。** 仓库里的 `testpwd` 只是本地开发占位值，部署前请改成自己的口令，
> 或改用 `appsettings.Local.json`（已在 `.gitignore` 中）。

### 2. 后端

```bash
cd backend
dotnet restore
dotnet run --project src/GasInventory.Api
```

默认监听 `http://localhost:10374`（见 `appsettings.json` 的 `Urls`）。
首次启动会为尚未开通登录的员工按系统参数 `sys.初始密码` 初始化口令。

### 3. 前端（开发模式）

```bash
cd frontend
npm install
npm run dev          # Vite 开发服务器，/api 代理到后端
```

生产构建并交给后端静态托管：

```bash
npm run build        # 产物在 frontend/dist
# 把 dist 内容拷到 backend/src/GasInventory.Api/wwwroot/ 后，后端会直接托管 SPA
```

## 数据库

> **现状说明（重要）**：项目早期基于 SQL Server 2022 Express，现已迁移到 PostgreSQL
> （后端使用 `Npgsql.EntityFrameworkCore.PostgreSQL`，数据服务层内置 T-SQL → PG 方言归一化）。
> **完整的建库脚本（`db/v3/*.sql`）目前不在本仓库中** —— 仓库里只有 `backup/` 下的部分片段。
> 因此本仓库当前**无法一键重建数据库**，这一点在继续开发前需要补齐。

表结构设计要点：

- 实体与列名**均为中文**，与数据库列一一对应，避免数百个字段的人工英译映射；
- 通用数据服务只接受 `表元数据` 白名单里的表名/列名，值全部走参数化查询；
- 金额 `decimal(19,4)`、重量 `decimal(18,3)`，由 `GasInventoryDbContext.精度.cs` 统一声明。

## 演示账号

口令均为 `123456`（**仅演示用**）：

| 工号 | 姓名 / 岗位 | 权限范围 |
|---|---|---|
| `E001` | 张立新 / 负责人 | 超级管理员，全部 |
| `E002` | 王慧敏 / 财务出纳 | 财务、收付、凭证；看不到钢瓶建档 |
| `E005` | 刘志强 / 仓管 | 库存查询/流水/调拨/盘点/钢瓶作业 |
| `E004` | 周建军 / 送瓶员 | 配送任务：查看 + 装车/出车/回场，看不到财务 |

权限码共 8 个：`view / add / edit / del / audit / post / print / export`。

## 项目文档

| 文件 | 内容 |
|---|---|
| [`docs/01_方案设计.md`](docs/01_方案设计.md) | 原始方案设计：需求、表设计、界面规划 |
| [`docs/02_实施说明.md`](docs/02_实施说明.md) | 落地实施记录 |

> 单机版交付说明、接口验收探针、代码索引、静态演示包等内部资料仅保留在本地，未随本仓库发布。

## 已知限制

- 建库脚本未随仓库提供（见上文"数据库"）；
- 外围域的作业界面未做：外部接口配置、平台订单认领、GPS 轨迹写入、考勤打卡、促销活动、
  报表订阅、附件管理、提成薪资的生成-审核-发放（相关表与存储过程存在，只是没接界面）；
- 打印模板为固定三联版式，无可视化模板设计器。

## License

MIT

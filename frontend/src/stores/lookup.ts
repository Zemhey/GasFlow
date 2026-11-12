import { defineStore } from 'pinia'
import { ref } from 'vue'
import api from '@/api'

type 行 = Record<string, any>

/**
 * 下拉数据：客户、员工、商品、各类字典一次拉全，页面里用编号换名称。
 * 数据量极小，比每页各查一次更省事也更快。
 * 契约：GET /api/lookups → { 数据源名: 行数组 }，键名与后端 LookupsController.表清单 一致。
 *
 * ⚠ v3 换过表名：钢瓶规格→商品、供气商→供应商、打气车→车辆；
 *   结算方式/付款方式/结算账期/客户状态 在 v3 不是独立表（是 客户 的字段 + 字典），
 *   所以下面**仍保留**这些旧键名只在"兼容旧页面"时有用，新页面请用 v3 的键。
 */

/** 数据源 → 主键字段（契约里每个数据源第一个字段就是主键；这里只是没有数据时的兜底） */
const 主键表: Record<string, string> = {
  // v3 真实数据源
  客户: '客户编号',
  员工: '员工编号',
  商品: '商品编号',
  商品品类: '品类编号',
  供应商: '供应商编号',
  车辆: '车辆编号',
  客户区域: '客户区域编号',
  客户分类: '客户分类编号',
  渠道: '渠道编号',
  部门: '部门编号',
  岗位: '岗位编号',
  仓库: '仓库编号',
  价格方案: '方案编号',
  字典类型: '字典类型编号',
  字典项: '字典项编号',
  计量单位: '计量单位编号',
  费用类型: '费用类型编号',
  // v2 残留键（旧页面还在用，等批次 5 改完可删）
  钢瓶规格: '规格编号',
  供气商: '供气商编号',
  打气车: '打气车编号',
  客户状态: '客户状态编号',
  结算方式: '结算方式编号',
  结算账期: '结算账期编号',
  付款方式: '付款方式编号',
  出货与送气分类: '送气分类编号',
  计提方案: '计提方案编号'
}

export const useLookupStore = defineStore('lookup', () => {
  const 数据 = ref<Record<string, 行[]>>({})
  const 已加载 = ref(false)
  const 加载中 = ref(false)

  async function 加载(force = false) {
    if (已加载.value && !force) return 数据.value
    if (加载中.value) return 数据.value
    加载中.value = true
    try {
      数据.value = (await api.lookups()) ?? {}
      已加载.value = true
    } finally {
      加载中.value = false
    }
    return 数据.value
  }

  function 列表(表名?: string): 行[] {
    if (!表名) return []
    return 数据.value[表名] ?? []
  }

  function 主键(表名: string): string {
    const first = 列表(表名)[0]
    if (first) return Object.keys(first)[0]
    return 主键表[表名] ?? ''
  }

  function 行(表名: string, id: any, 值字段?: string): 行 | undefined {
    if (id === null || id === undefined || id === '') return undefined
    const k = 值字段 || 主键(表名)
    return 列表(表名).find((r) => r[k] === id || String(r[k]) === String(id))
  }

  /** 名称解析：不同表的"显示名字段"不同，这里统一兜底（含 v3 新表的字段名） */
  function 名称(表名: string, id: any, 值字段?: string): string {
    if (id === null || id === undefined || id === '') return ''
    const row = 行(表名, id, 值字段)
    if (!row) return String(id)
    return (
      // v3 主数据
      row.客户简称 ||
      row.商品名称 ||
      row.品类名称 ||
      row.供应商简称 ||
      row.别名 ||          // 车辆
      row.姓名 ||          // 员工
      row.方案名称 ||
      row.渠道名称 ||
      row.仓库名称 ||
      row.公司简称 ||
      row.网点名称 ||
      row.字典类型名称 ||
      row.项名称 ||
      row.计量单位名称 ||
      row.费用类型名称 ||
      row.类型名称 ||      // v3 费用类型（列名是 类型名称，不是 费用类型名称）
      // v2 残留（旧页面仍在用）
      row.资源名称 ||
      row.权限名称 ||
      row.岗位名称 ||
      row.规格名称 ||
      row.区域名称 ||
      row.分类名称 ||
      row.状态名称 ||
      row.部门名称 ||
      row.付款方式名称 ||
      row.结算方式名称 ||
      row.账期名称 ||
      row.送气分类 ||
      row.车牌 ||
      String(id)
    )
  }

  /**
   * 下拉选项。值字段默认是主键；给 值字段 时用它（例：费用类型下拉要提交 `类型编码`，
   * 因为 usp_财务_费用 的入参是编码而不是编号）。
   */
  function 选项(表名: string, 值字段?: string) {
    const 值 = 值字段 || 主键(表名)
    const 主键列 = 主键(表名)
    return 列表(表名).map((r) => ({
      label: 名称(表名, r[主键列]),
      value: 值字段 ? r[值字段] : r[主键列]
    }))
  }

  return { 数据, 已加载, 加载中, 加载, 列表, 行, 名称, 选项, 主键 }
})

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import api, { type 登录结果, type 菜单资源行 } from '@/api'

/** /api/auth/me 的返回结构（字段名与契约一字不差） */
export type 当前用户 = 登录结果

export const useAuthStore = defineStore('auth', () => {
  const 用户 = ref<当前用户 | null>(null)
  const 已加载 = ref(false)
  /** 后端 /api/auth/menus 下发的菜单；为空时菜单页退回本地配置兜底 */
  const 后端菜单 = ref<菜单资源行[]>([])
  /** 前端路由 → 后端权限资源对象名（由后端下发，例如 /inventory/stock → Inv.Stock） */
  const 路由表 = ref<Record<string, string>>({})
  let 菜单拉取中: Promise<菜单资源行[]> | null = null

  const 已登录 = computed(() => !!用户.value)
  /** 首次登录未改初始密码 → 路由强制跳 /system/password */
  const 必须改密 = computed(() => !!用户.value?.必须改密)
  const 资源清单 = computed(() => 用户.value?.资源 ?? [])

  function 存入(u: 登录结果) {
    用户.value = u
    已加载.value = true
  }

  async function 登录(工号: string, 密码: string) {
    const u = await api.auth.登录(工号, 密码)
    存入(u)
    菜单拉取中 = null
    后端菜单.value = []
    路由表.value = {}
    return u
  }

  async function 拉取() {
    if (已加载.value && 用户.value) return 用户.value
    try {
      存入(await api.auth.当前用户())
    } catch {
      用户.value = null
      已加载.value = true
    }
    return 用户.value
  }

  async function 退出() {
    try {
      await api.auth.退出()
    } catch {
      /* 后端会话已失效时忽略 */
    } finally {
      用户.value = null
      已加载.value = false
      后端菜单.value = []
      路由表.value = {}
      菜单拉取中 = null
    }
  }

  /** 登录后强制改密完成后调用，清掉标记 */
  function 标记已改密() {
    if (用户.value) 用户.value = { ...用户.value, 必须改密: false }
  }

  async function 拉取菜单() {
    if (后端菜单.value.length) return 后端菜单.value
    if (菜单拉取中) return 菜单拉取中
    菜单拉取中 = api.auth
      .菜单()
      .then((r) => {
        // 老后端只回 { 菜单: [] }，新后端回 { 菜单, 路由 }（路由 = 前端路径 → 后端登记的资源对象名）。
        // 两者都必须容错：这里一个 undefined 的 .length 就会把渲染打崩（批次 6 实测白屏卡死）。
        路由表.value = r?.路由 && typeof r.路由 === 'object' ? r.路由 : {}
        后端菜单.value = Array.isArray(r?.菜单) ? r.菜单 : []
        return 后端菜单.value
      })
      .catch(() => {
        后端菜单.value = []
        return 后端菜单.value
      })
      .finally(() => {
        菜单拉取中 = null
      })
    return 菜单拉取中
  }

  /**
   * 是否有某个功能资源。
   * - 不传对象名 → 放行（只做登录校验）
   * - 尚未拉到用户 → 视为无权限（不误放行）
   * - 资源清单为空数组时后端可能还没实现资源下发，此时放行（菜单/接口两侧仍会拦）
   */
  function 有资源(对象名?: string) {
    if (!对象名) return true
    if (!用户.value) return false
    const 清单 = 资源清单.value
    if (!清单.length) return true
    return 清单.includes(对象名)
  }

  /** 是否有某个资源下的某个权限代码（edit / approve / post / reverse ...） */
  function 有权限(资源: string, 代码: string) {
    if (!用户.value) return false
    if (!有资源(资源)) return false
    const 表 = 用户.value.权限
    // 后端未下发细粒度权限时不拦截（只靠资源级）
    if (!表) return true
    return (表[资源] ?? []).includes(代码)
  }

  return {
    用户,
    已加载,
    已登录,
    必须改密,
    资源清单,
    后端菜单,
    路由表,
    登录,
    拉取,
    退出,
    标记已改密,
    拉取菜单,
    有资源,
    有权限
  }
})

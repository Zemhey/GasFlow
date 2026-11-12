import axios from 'axios'
import { ElMessage } from 'element-plus'

/** 同源部署（后端托管前端产物）时 baseURL 为空即可；开发环境由 vite proxy 转发 /api */
export const http = axios.create({
  baseURL: '/api',
  timeout: 30000,
  withCredentials: true
})

http.interceptors.response.use(
  (r) => r.data,
  (err) => {
    const status = err.response?.status
    if (status === 401) {
      if (!location.pathname.startsWith('/login')) {
        location.href = '/login?redirect=' + encodeURIComponent(location.pathname + location.search)
      }
      return Promise.reject(err)
    }
    // 调用方若已设置 silent 则自己处理提示（例如"自动带价"失败时静默留空）
    if ((err.config as { silent?: boolean } | undefined)?.silent) return Promise.reject(err)
    const msg =
      err.response?.data?.message ??
      err.response?.data?.title ??
      (status ? `请求失败（HTTP ${status}）` : err.message) ??
      '请求失败'
    ElMessage.error(String(msg))
    return Promise.reject(err)
  }
)

export default http

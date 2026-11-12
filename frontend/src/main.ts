import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'

import App from './App.vue'
import router from './router'
import './style.css'

function 启动() {
  const app = createApp(App)

  for (const [key, comp] of Object.entries(ElementPlusIconsVue)) {
    app.component(key, comp as never)
  }

  app.use(createPinia())
  app.use(router)
  app.use(ElementPlus, { locale: zhCn, size: 'default' })
  app.mount('#app')
}

if (import.meta.env.VITE_DEMO === '1') {
  import('./demo/mockAdapter').then(({ installDemo }) => {
    installDemo()
    启动()
  })
} else {
  启动()
}

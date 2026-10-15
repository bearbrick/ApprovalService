/**
 * 使用现有 Vue 编译器和自定义渲染器验证公共组件，不依赖浏览器模拟库或网络。
 */
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as Vue from 'vue'
import type { Component } from 'vue'

/**
 * 检查程序提供的断言回调。
 * @param name 检查名称。
 * @param value 条件是否成立。
 */
type Check = (name: string, value: boolean) => void

/**
 * 最小渲染宿主节点，记录真实组件输出的文本、事件和原生对话框状态。
 */
interface HostNode {
  /**
   * 元素标签或文本节点类型。
   */
  type: string
  /**
   * 节点直接承载的文字。
   */
  text: string
  /**
   * 子节点列表。
   */
  children: HostNode[]
  /**
   * 当前父节点。
   */
  parent: HostNode | null
  /**
   * Vue 绑定的属性和事件处理器。
   */
  props: Record<string, unknown>
  /**
   * 原生对话框是否打开。
   */
  open: boolean
  /**
   * 模拟浏览器将对话框置为模态打开状态。
   */
  showModal(): void
  /**
   * 模拟浏览器关闭对话框。
   */
  close(): void
  /**
   * 返回固定的对话框位置，用于生成内部和外部的指针事件。
   * @returns 对话框的四条边界。
   */
  getBoundingClientRect(): { left: number; right: number; top: number; bottom: number }
}

/**
 * 模拟在页面上触发对话框的可聚焦元素。
 */
class FocusTarget {
  /**
   * 触发元素是否仍在当前页面。
   */
  isConnected = true

  /**
   * 组件恢复焦点的次数。
   */
  focusCount = 0

  /**
   * 记录组件发出的焦点恢复请求。
   */
  focus(): void {
    this.focusCount += 1
  }
}

/**
 * 将仓库内的单文件组件编译为可由 Node 执行的组件，复用已安装的 Vue 和 TypeScript。
 * @param file 要加载的组件或图标模块文件地址。
 * @param cache 本次检查的模块缓存。
 * @returns 编译后的模块导出。
 */
function loadModule(file: URL, cache: Map<string, Record<string, unknown>>): Record<string, unknown> {
  const normalized = fileURLToPath(file)
  const cached = cache.get(normalized)
  if (cached) return cached
  const source = readFileSync(normalized, 'utf8')
  const script = normalized.endsWith('.vue')
    ? compileScript(parse(source).descriptor, { id: normalized, inlineTemplate: true }).content
    : source
  const output = ts.transpileModule(script.replaceAll('import.meta.env.DEV', 'false'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText
  const module = { exports: {} as Record<string, unknown> }
  // 输入仅来自仓库内的受测组件，不接收用户内容或远程脚本。
  const execute = new Function('require', 'module', 'exports', output)
  execute((name: string) => name === 'vue' ? Vue : loadModule(new URL(name, file), cache), module, module.exports)
  cache.set(normalized, module.exports)
  return module.exports
}

/**
 * 构造组件检查所需的渲染宿主，不模拟业务行为。
 * @returns 渲染器、节点工厂和节点清单。
 */
function createHost() {
  const nodes: HostNode[] = []
  const createNode = (type: string, text = ''): HostNode => {
    // 原生 DOM 元素不会被 Vue 转成响应式代理，测试宿主保持相同的身份语义。
    const node = Vue.markRaw<HostNode>({
      type, text, children: [], parent: null, props: {}, open: false,
      showModal() { this.open = true },
      close() { this.open = false },
      getBoundingClientRect() { return { left: 20, right: 200, top: 20, bottom: 200 } },
    })
    nodes.push(node)
    return node
  }
  const body = createNode('body')
  const renderer = Vue.createRenderer<HostNode, HostNode>({
    patchProp: (node, key, previous, next) => { void previous; node.props[key] = next },
    insert(node, parent, anchor) {
      if (node.parent) {
        const previousIndex = node.parent.children.indexOf(node)
        if (previousIndex >= 0) node.parent.children.splice(previousIndex, 1)
      }
      node.parent = parent
      const index = anchor ? parent.children.indexOf(anchor) : -1
      if (index < 0) parent.children.push(node)
      else parent.children.splice(index, 0, node)
    },
    remove(node) {
      if (node.parent) {
        const index = node.parent.children.indexOf(node)
        if (index >= 0) node.parent.children.splice(index, 1)
      }
      node.parent = null
    },
    createElement: (tag) => createNode(tag),
    createText: (text) => createNode('text', text),
    createComment: (text) => createNode('comment', text),
    setText: (node, text) => { node.text = text },
    setElementText: (node, text) => { node.text = text; node.children = [] },
    parentNode: (node) => node.parent,
    nextSibling: (node) => node.parent?.children[node.parent.children.indexOf(node) + 1] ?? null,
    querySelector: (selector) => selector === 'body' ? body : null,
  })
  return { renderer, createNode, nodes }
}

/**
 * 收集组件实际输出的可见文本。
 * @param node 需要读取的宿主节点。
 * @returns 节点及其子节点的文字。
 */
function renderedText(node: HostNode): string {
  return node.text + ' ' + node.children.map(renderedText).join(' ')
}

/**
 * 调用组件绑定的交互事件，缺失处理器时明确报告失败。
 * @param node 事件所在节点。
 * @param name Vue 宿主事件属性名。
 * @param event 模拟的浏览器事件参数。
 */
function fire(node: HostNode, name: string, event: object = {}): void {
  const handler = node.props[name]
  if (typeof handler !== 'function') throw new Error('组件缺少预期的交互处理器：' + name)
  handler(event)
}

/**
 * 验证渲染或生命周期失败时出现恢复界面，重试创建全新的组件实例。
 * @param boundary 真实错误边界组件。
 * @param check 主检查程序的断言回调。
 * @returns 所有渲染更新完成的通知。
 */
async function checkBoundary(boundary: Component, check: Check): Promise<void> {
  for (const phase of ['渲染', '生命周期']) {
    const host = createHost()
    const root = host.createNode('root')
    let fails = true
    let instances = 0
    const child = Vue.defineComponent({
      setup() {
        instances += 1
        Vue.onMounted(() => {
          if (phase === '生命周期' && fails) throw new Error('INTERNAL_PRIVATE_DETAIL')
        })
        return () => {
          if (phase === '渲染' && fails) throw new Error('INTERNAL_PRIVATE_DETAIL')
          return Vue.h('p', '页面已恢复')
        }
      },
    })
    const app = host.renderer.createApp({ render: () => Vue.h(boundary, null, { default: () => Vue.h(child) }) })
    try {
      app.mount(root)
      await Vue.nextTick()
      const text = renderedText(root)
      check(`${phase}异常显示通用中文提示且不暴露异常内容`, text.includes('页面暂时无法显示') && !text.includes('INTERNAL_PRIVATE_DETAIL'))
      const retry = host.nodes.find((node) => node.type === 'button' && renderedText(node).includes('重试加载'))
      if (!retry) throw new Error('错误界面没有提供重试入口。')
      fails = false
      fire(retry, 'onClick')
      await Vue.nextTick()
      check(`${phase}异常重试后重新挂载子树并显示恢复内容`, instances === 2 && renderedText(root).includes('页面已恢复'))
    } finally {
      app.unmount()
    }
  }
}

/**
 * 验证对话框的受控状态、关闭来源、焦点恢复和卸载清理。
 * @param dialog 真实对话框组件。
 * @param check 主检查程序的断言回调。
 * @returns 所有交互更新完成的通知。
 */
async function checkDialog(dialog: Component, check: Check): Promise<void> {
  const previousDocument = Object.getOwnPropertyDescriptor(globalThis, 'document')
  const previousElement = Object.getOwnPropertyDescriptor(globalThis, 'HTMLElement')
  const trigger = new FocusTarget()
  const host = createHost()
  const visible = Vue.ref(false)
  let closeEvents = 0
  const app = host.renderer.createApp({
    render: () => Vue.h(dialog, {
      open: visible.value, title: '公共对话框',
      onClose: () => { closeEvents += 1; visible.value = false },
    }, { default: () => Vue.h('p', '对话框正文') }),
  })
  try {
    Object.defineProperty(globalThis, 'HTMLElement', { configurable: true, value: FocusTarget })
    Object.defineProperty(globalThis, 'document', { configurable: true, value: { activeElement: trigger } })
    app.mount(host.createNode('root'))
    const element = host.nodes.find((node) => node.type === 'dialog')
    if (!element) throw new Error('公共对话框没有生成原生 dialog 元素。')
    check('对话框默认关闭，不意外抢占页面', !element.open)
    visible.value = true
    await Vue.nextTick()
    check('受控属性打开原生对话框并显示正文', element.open && renderedText(element).includes('对话框正文'))
    fire(element, 'onCancel', { preventDefault() {} })
    fire(element, 'onClose')
    await Vue.nextTick()
    check('Escape 关闭只通知一次并恢复触发元素焦点', !element.open && closeEvents === 1 && trigger.focusCount === 1)
    visible.value = true
    await Vue.nextTick()
    fire(element, 'onPointerdown', { target: element, clientX: 0, clientY: 0 })
    fire(element, 'onClick', { target: element, clientX: 0, clientY: 0 })
    await Vue.nextTick()
    check('单击遮罩可关闭对话框并恢复焦点', !element.open && closeEvents === 2 && trigger.focusCount === 2)
    visible.value = true
    await Vue.nextTick()
    fire(element, 'onPointerdown', { target: element, clientX: 80, clientY: 80 })
    fire(element, 'onClick', { target: element, clientX: 0, clientY: 0 })
    check('从正文拖出到遮罩不会误关闭', element.open && closeEvents === 2)
    visible.value = false
    await Vue.nextTick()
    check('父组件关闭会恢复焦点但不重复发出关闭请求', !element.open && closeEvents === 2 && trigger.focusCount === 3)
    visible.value = true
    await Vue.nextTick()
    app.unmount()
    check('组件卸载会解除模态状态并恢复焦点', !element.open && trigger.focusCount === 4)
  } finally {
    app.unmount()
    if (previousDocument) Object.defineProperty(globalThis, 'document', previousDocument)
    else Reflect.deleteProperty(globalThis, 'document')
    if (previousElement) Object.defineProperty(globalThis, 'HTMLElement', previousElement)
    else Reflect.deleteProperty(globalThis, 'HTMLElement')
  }
}

/**
 * 运行公共组件的恢复与对话框交互场景。
 * @param check 主检查程序的断言回调。
 * @returns 所有组件场景完成的通知。
 */
export async function runUiChecks(check: Check): Promise<void> {
  const directory = new URL('../src/components/ui/', import.meta.url)
  const cache = new Map<string, Record<string, unknown>>()
  await checkBoundary(loadModule(new URL('UiErrorBoundary.vue', directory), cache).default as Component, check)
  await checkDialog(loadModule(new URL('UiDialog.vue', directory), cache).default as Component, check)
}

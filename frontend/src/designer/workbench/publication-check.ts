/**
 * 工作台发布预检与画布组件复用同一份静态校验，避免规则漂移。
 */
export { validateCanvasDefinition as checkPublication } from '../canvas/canvas-validation.ts'

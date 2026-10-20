import type { FlowDefinition } from '../../model/flow.ts'
import { copyFlow } from './workbench-model.ts'

/**
 * 工作台唯一的草稿历史；画布拖动过程只更新 present，松开时才提交一次。
 */
export interface DraftHistory {
  /**
   * 已完成编辑之前的草稿，旧到新排列。
   */
  past: FlowDefinition[]
  /**
   * 当前展示的完整流程草稿。
   */
  present: FlowDefinition
  /**
   * 撤销后可恢复的草稿，新到旧排列。
   */
  future: FlowDefinition[]
}

/**
 * 为新流程建立空历史。
 * @param definition 初始流程草稿。
 * @returns 不共享输入对象的历史状态。
 */
export function createDraftHistory(definition: FlowDefinition): DraftHistory {
  return { past: [], present: copyFlow(definition), future: [] }
}

/**
 * 画布拖动中更新可见草稿，不把每次指针移动都记成撤销步骤。
 * @param history 当前历史。
 * @param next 下一帧草稿。
 * @returns 更新后的历史状态。
 */
export function previewDraft(history: DraftHistory, next: FlowDefinition): DraftHistory {
  return { ...history, present: copyFlow(next) }
}

/**
 * 把一次完整编辑的旧快照加入历史，清除重做分支。
 * @param history 当前历史。
 * @param previous 本次编辑开始前的完整草稿。
 * @returns 已登记编辑的历史状态。
 */
export function commitDraft(history: DraftHistory, previous: FlowDefinition): DraftHistory {
  return { ...history, past: [...history.past.slice(-49), copyFlow(previous)], future: [] }
}

/**
 * 应用工作台属性编辑并立刻提交历史。
 * @param history 当前历史。
 * @param next 编辑后的完整草稿。
 * @returns 新的历史状态。
 */
export function editDraft(history: DraftHistory, next: FlowDefinition): DraftHistory {
  return commitDraft(previewDraft(history, next), history.present)
}

/**
 * 撤销最近一次完整编辑。
 * @param history 当前历史。
 * @returns 撤销后的状态；没有可撤销步骤时返回原状态。
 */
export function undoDraft(history: DraftHistory): DraftHistory {
  if (!history.past.length) return history
  return {
    past: history.past.slice(0, -1),
    present: copyFlow(history.past.at(-1)!),
    future: [...history.future, copyFlow(history.present)],
  }
}

/**
 * 重做刚撤销的完整编辑。
 * @param history 当前历史。
 * @returns 重做后的状态；没有可重做步骤时返回原状态。
 */
export function redoDraft(history: DraftHistory): DraftHistory {
  if (!history.future.length) return history
  return {
    past: [...history.past, copyFlow(history.present)],
    present: copyFlow(history.future.at(-1)!),
    future: history.future.slice(0, -1),
  }
}

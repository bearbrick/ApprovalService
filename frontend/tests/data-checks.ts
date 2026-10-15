import type { FlowDataDefinition, FlowDetailLayout } from '../src/model/flow.ts'
import { copyData, copyLayout, createField, createLayoutGroup, createLayoutItem, fieldReferences, previewGroups, sampleSubmission, validateDataDefinition } from '../src/designer/data/data-model.ts'

/**
 * 检查函数，由前端总入口传入。
 */
type Check = (name: string, value: boolean) => void

/**
 * 审批字段、样例 JSON、排版预览的场景检查。
 * @param check 记录检查结果的函数。
 */
export function runDataChecks(check: Check): void {
  const amount = createField('amount', '合计金额')
  amount.type = 'money'
  amount.required = true
  const items = createField('items', '采购明细')
  items.type = 'detailTable'
  const name = createField('name', '品名')
  const price = createField('price', '价格')
  price.type = 'money'
  items.children = [name, price]
  const approved = createField('approved', '核定金额')
  approved.type = 'money'
  approved.approverInput = true
  const calculated = createField('calculated', '计算金额')
  calculated.type = 'money'
  calculated.formula = 'SUM(${items.price})'
  const data: FlowDataDefinition = { fields: [amount, items, approved, calculated], summaryFields: ['amount'] }

  check('合法审批字段可生成不含审批填写和计算字段的接口 JSON 样例',
    JSON.stringify(sampleSubmission(data)) === JSON.stringify({ amount: 0, items: [{ name: '', price: 0 }] }))
  check('明细子字段可作为节点权限引用，且路径带表 key',
    fieldReferences(data).some(reference => reference.key === 'items.price' && reference.label === '采购明细 · 价格'))
  check('合法数据定义没有编辑问题', validateDataDefinition(data).length === 0)
  const duplicate = copyData(data)
  duplicate.fields.push(createField('amount', '重名金额'))
  check('同级重复字段会被发现且拷贝不修改原定义',
    validateDataDefinition(duplicate).some(problem => problem.includes('重复')) && data.fields.length === 4)
  const unusual = { fields: [createField('采购.金额', '金额')], summaryFields: ['采购.金额'] }
  check('合法的非 ASCII 字段 key 不被前端额外拒绝', validateDataDefinition(unusual).length === 0)
  const invalid = copyData(data)
  invalid.fields[1]!.children = []
  invalid.summaryFields = ['missing']
  check('空明细表与失效摘要字段会提示问题',
    validateDataDefinition(invalid).some(problem => problem.includes('至少需要一个')) && validateDataDefinition(invalid).some(problem => problem.includes('摘要字段')))

  const layout: FlowDetailLayout = { mode: 'auto', groups: [] }
  check('自动排版按字段定义顺序生成只读详情结构',
    previewGroups(data, layout)[0]?.items.map(item => item.field.key).join(',') === 'amount,items,approved,calculated')
  const group = createLayoutGroup('采购信息')
  group.columns = 3
  group.items = [createLayoutItem('items'), createLayoutItem('amount'), createLayoutItem('missing')]
  group.items[0]!.columnSpan = 2
  group.items[0]!.detailColumnWidths.price = 35
  layout.mode = 'custom'
  layout.groups = [group]
  const preview = previewGroups(data, layout)
  check('自定义排版保持顺序、跨列与明细列宽并忽略已删字段',
    preview[0]?.items.map(item => item.field.key).join(',') === 'items,amount'
    && preview[0]?.items[0]?.layout.columnSpan === 2
    && preview[0]?.items[0]?.layout.detailColumnWidths.price === 35)
  const copied = copyLayout(layout)
  copied.groups[0]!.items[0]!.detailColumnWidths.price = 60
  check('修改预览草稿不会污染传入的排版', layout.groups[0]?.items[0]?.detailColumnWidths.price === 35)
  layout.mode = 'embed'
  check('嵌入业务原单时不生成本地字段预览', previewGroups(data, layout).length === 0)
}

/**
 * 仅对没有字段引用的常量表达式求值，用于镜像后端发布时的提前错误检查。
 */
import type { StaticExpressionNode } from './expression-static.ts'

/**
 * .NET decimal 最大值的保守数字近似；超过时前端预检直接拒绝。
 */
const decimalMaximum = 7.922816251426433e28
/**
 * 人民币财务大写数字。
 */
const uppercaseDigits = ['零', '壹', '贰', '叁', '肆', '伍', '陆', '柒', '捌', '玖']
/**
 * 每个四位组内的单位。
 */
const smallUnits = ['', '拾', '佰', '仟']
/**
 * 每个四位组之间的单位。
 */
const largeUnits = ['', '万', '亿', '兆']

/**
 * 将一个非零四位组转换为中文财务大写。
 * @param value 0 到 9999 的整数。
 * @returns 四位组中文文本。
 */
function uppercaseGroup(value: number): string {
  let result = ''
  let pendingZero = false
  for (let position = 3; position >= 0; position--) {
    const digit = Math.floor(value / 10 ** position) % 10
    if (digit === 0) { pendingZero = result.length > 0; continue }
    if (pendingZero) result += uppercaseDigits[0]
    result += uppercaseDigits[digit] + smallUnits[position]
    pendingZero = false
  }
  return result
}

/**
 * 只为常量表达式预检转换人民币大写，与后端同样限定 9999 兆。
 * @param amount 待转换金额。
 * @returns 财务大写文本。
 */
function uppercaseMoney(amount: number): string {
  // 预检只计算字面量，实际金额运算与格式化仍由服务端 decimal 决定。
  const rounded = Math.round(Math.abs(amount) * 100 + Number.EPSILON)
  if (rounded >= 1000000000000000000) throw new Error('金额超出人民币大写支持范围。')
  let yuan = Math.floor(rounded / 100)
  const cents = rounded % 100
  const groups: number[] = []
  for (let index = 0; index < largeUnits.length; index++) { groups.push(yuan % 10000); yuan = Math.floor(yuan / 10000) }
  let result = ''
  let pendingZero = false
  for (let index = groups.length - 1; index >= 0; index--) {
    const group = groups[index]!
    if (group === 0) { pendingZero = result.length > 0; continue }
    if (result && (pendingZero || group < 1000)) result += uppercaseDigits[0]
    result += uppercaseGroup(group) + largeUnits[index]
    pendingZero = false
  }
  if (!result) result = uppercaseDigits[0]!
  result += '元'
  if (cents === 0) result += '整'
  else {
    const jiao = Math.floor(cents / 10)
    const fen = cents % 10
    if (jiao > 0) result += uppercaseDigits[jiao] + '角'
    else if (Math.floor(rounded / 100) > 0) result += uppercaseDigits[0]
    result += fen > 0 ? uppercaseDigits[fen] + '分' : '整'
  }
  return amount < 0 && rounded > 0 ? '负' + result : result
}

/**
 * 要求表达式值是有限十进制数。
 * @param value 待检查的值。
 * @returns 数字。
 */
function number(value: unknown): number {
  if (typeof value !== 'number') throw new Error('表达式需要数字字段。')
  if (!Number.isFinite(value) || Math.abs(value) > decimalMaximum) throw new Error('数字计算超出范围。')
  return value
}

/**
 * 要求表达式值为布尔值。
 * @param value 待检查的值。
 * @returns 布尔值。
 */
function boolean(value: unknown): boolean {
  if (typeof value !== 'boolean') throw new Error('表达式需要布尔值。')
  return value
}

/**
 * 在相同类型之间比较常量。
 * @param left 左值。
 * @param right 右值。
 * @returns 小于、等于或大于的符号。
 */
function compare(left: unknown, right: unknown): number {
  if (left === null || right === null) return left === right ? 0 : left === null ? -1 : 1
  if (typeof left !== typeof right) throw new Error('比较的两个值类型不一致。')
  if (typeof left === 'number') return number(left) - number(right)
  if (typeof left === 'boolean') return Number(left) - Number(right)
  if (typeof left === 'string') return left < (right as string) ? -1 : left > (right as string) ? 1 : 0
  throw new Error('比较的两个值类型不一致。')
}

/**
 * 把 ISO 日期常量转换为 UTC 日期序数。
 * @param value 日期文本。
 * @returns 日期序数。
 */
function dateNumber(value: unknown): number {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) throw new Error('日期应使用 yyyy-MM-dd 格式。')
  const [year, month, day] = value.split('-').map(Number)
  const checked = new Date(0)
  checked.setUTCFullYear(year!, month! - 1, day!)
  const time = checked.getTime()
  if (checked.getUTCFullYear() !== year || checked.getUTCMonth() + 1 !== month || checked.getUTCDate() !== day) throw new Error('日期应使用 yyyy-MM-dd 格式。')
  return time / 86400000
}

/**
 * 对常量表达式执行与后端一致的短路与类型检查。
 * @param node 解析后的语法树。
 * @returns 常量计算结果。
 */
export function evaluateConstantExpression(node: StaticExpressionNode): number | boolean | string | null {
  if (node.kind === 'literal') return node.value
  if (node.kind === 'field') throw new Error('常量表达式不能包含字段。')
  if (node.kind === 'unary') {
    const value = evaluateConstantExpression(node.children[0]!)
    return node.name === '-' ? number(-number(value)) : !boolean(value)
  }
  if (node.kind === 'binary') {
    const left = evaluateConstantExpression(node.children[0]!)
    if (node.name === 'AND' || node.name === '&&') return boolean(left) && boolean(evaluateConstantExpression(node.children[1]!))
    if (node.name === 'OR' || node.name === '||') return boolean(left) || boolean(evaluateConstantExpression(node.children[1]!))
    const right = evaluateConstantExpression(node.children[1]!)
    switch (node.name) {
      case '+': return number(number(left) + number(right))
      case '-': return number(number(left) - number(right))
      case '*': return number(number(left) * number(right))
      case '/': { const divisor = number(right); if (divisor === 0) throw new Error('除数不能为零。'); return number(number(left) / divisor) }
      case '==': return compare(left, right) === 0
      case '!=': return compare(left, right) !== 0
      case '>': return compare(left, right) > 0
      case '<': return compare(left, right) < 0
      case '>=': return compare(left, right) >= 0
      case '<=': return compare(left, right) <= 0
      default: throw new Error('不支持的运算符。')
    }
  }
  if (node.name === 'IF') return evaluateConstantExpression(node.children[boolean(evaluateConstantExpression(node.children[0]!)) ? 1 : 2]!)
  if (node.name === 'DATEDIFF') return dateNumber(evaluateConstantExpression(node.children[1]!)) - dateNumber(evaluateConstantExpression(node.children[0]!))
  if (node.name === 'RMBUPPER') {
    const amount = number(evaluateConstantExpression(node.children[0]!))
    return uppercaseMoney(amount)
  }
  if (node.name === 'ANY') {
    const value = evaluateConstantExpression(node.children[0]!)
    return node.children.length === 1 ? boolean(value) : compare(value, evaluateConstantExpression(node.children[1]!)) === 0
  }
  const values = node.children.map(child => number(evaluateConstantExpression(child)))
  switch (node.name) {
    case 'SUM': return number(values.reduce((sum, value) => sum + value, 0))
    case 'AVG': return number(values.reduce((sum, value) => sum + value, 0) / values.length)
    case 'MAX': return Math.max(...values)
    case 'MIN': return Math.min(...values)
    default: throw new Error(`不支持函数 ${node.name}。`)
  }
}

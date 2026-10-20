/**
 * 后端 ApprovalExpression 的前端静态解析镜像；只用于画布预检，不参与审批运行求值。
 */

/**
 * 受限表达式的最大字符数。
 */
const maxLength = 2048
/**
 * 受限表达式的最大嵌套深度。
 */
const maxDepth = 32
/**
 * 受限表达式的最大语法单元数。
 */
const maxTokens = 512
/**
 * 后端支持的函数白名单及其参数数目。
 */
const functions: Record<string, (count: number) => boolean> = {
  SUM: count => count >= 1,
  AVG: count => count >= 1,
  MAX: count => count >= 1,
  MIN: count => count >= 1,
  IF: count => count === 3,
  ANY: count => count === 1 || count === 2,
  DATEDIFF: count => count === 2,
  RMBUPPER: count => count === 1,
}

/**
 * 受限表达式语法树节点。
 */
export interface StaticExpressionNode {
  /**
   * 字面量、字段、一元运算、二元运算或函数调用。
   */
  kind: 'literal' | 'field' | 'unary' | 'binary' | 'call'
  /**
   * 运算符、字段路径或函数名。
   */
  name: string
  /**
   * 字面量值。
   */
  value: number | boolean | string | null
  /**
   * 子表达式。
   */
  children: StaticExpressionNode[]
}

/**
 * 表达式解析结果和按出现顺序去重的字段路径。
 */
export interface StaticExpressionResult {
  /**
   * 解析后的语法树。
   */
  tree: StaticExpressionNode
  /**
   * 引用字段路径。
   */
  references: string[]
}

/**
 * 无副作用递归下降解析器，与后端采用同一优先级及函数约束。
 */
class StaticExpressionParser {
  /**
   * 待解析表达式。
   */
  private readonly source: string
  /**
   * 当前字符位置。
   */
  private offset = 0
  /**
   * 已消费语法单元数。
   */
  private tokens = 0
  /**
   * 按出现顺序去重的字段引用。
   */
  private readonly references: string[] = []

  /**
   * 建立解析器并检查长度。
   * @param source 表达式原文。
   */
  constructor(source: string) {
    if (!source.trim() || source.length > maxLength) throw new Error('表达式为空或超过长度上限。')
    this.source = source
  }

  /**
   * 解析完整输入并拒绝末尾未消费文本。
   * @returns 语法树和字段引用。
   */
  parse(): StaticExpressionResult {
    const tree = this.parseExpression(0, 0)
    this.skipSpace()
    if (this.offset !== this.source.length) throw new Error(`表达式在第 ${this.offset + 1} 个字符附近无效。`)
    return { tree, references: this.references }
  }

  /**
   * 按优先级解析二元运算。
   * @param minimum 当前允许的最低优先级。
   * @param depth 当前嵌套深度。
   * @returns 二元语法树或单个值。
   */
  private parseExpression(minimum: number, depth: number): StaticExpressionNode {
    if (depth > maxDepth) throw new Error('表达式嵌套过深。')
    let left = this.parsePrimary(depth)
    while (true) {
      this.skipSpace()
      const operator = this.readOperator()
      if (!operator || operator.priority < minimum) return left
      this.offset += operator.symbol.length
      this.countToken()
      const right = this.parseExpression(operator.priority + 1, depth)
      left = { kind: 'binary', name: operator.symbol, value: null, children: [left, right] }
    }
  }

  /**
   * 解析字段、数字、字符串、布尔值、函数、一元运算或括号。
   * @param depth 当前嵌套深度。
   * @returns 一个语法树节点。
   */
  private parsePrimary(depth: number): StaticExpressionNode {
    if (depth > maxDepth) throw new Error('表达式嵌套过深。')
    this.skipSpace()
    this.countToken()
    if (this.take('(')) { const inner = this.parseExpression(0, depth + 1); this.expect(')'); return inner }
    if (this.take('-')) return { kind: 'unary', name: '-', value: null, children: [this.parsePrimary(depth + 1)] }
    if (this.take('!')) return { kind: 'unary', name: '!', value: null, children: [this.parsePrimary(depth + 1)] }
    if (this.take('${')) {
      const start = this.offset
      while (this.offset < this.source.length && this.source[this.offset] !== '}') this.offset++
      if (this.offset === this.source.length) throw new Error('字段引用缺少右花括号。')
      const path = this.source.slice(start, this.offset++)
      if (!path.trim()) throw new Error('字段引用路径无效。')
      if (!this.references.includes(path)) this.references.push(path)
      return { kind: 'field', name: path, value: null, children: [] }
    }
    if (this.offset < this.source.length && /[0-9.]/.test(this.source[this.offset]!)) {
      const start = this.offset
      while (this.offset < this.source.length && /[0-9.]/.test(this.source[this.offset]!)) this.offset++
      const raw = this.source.slice(start, this.offset)
      if (!/^(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)$/.test(raw) || !Number.isFinite(Number(raw)) || Number(raw) > 7.922816251426433e28) throw new Error('数字字面量无效。')
      return { kind: 'literal', name: '', value: Number(raw), children: [] }
    }
    if (this.source[this.offset] === "'" || this.source[this.offset] === '"') {
      const quote = this.source[this.offset++]
      const start = this.offset
      while (this.offset < this.source.length && this.source[this.offset] !== quote) this.offset++
      if (this.offset === this.source.length) throw new Error('字符串缺少结束引号。')
      return { kind: 'literal', name: '', value: this.source.slice(start, this.offset++), children: [] }
    }
    const start = this.offset
    while (this.offset < this.source.length && /\p{L}/u.test(this.source[this.offset]!)) this.offset++
    const word = this.source.slice(start, this.offset)
    if (/^(TRUE|FALSE)$/i.test(word)) return { kind: 'literal', name: '', value: word.toUpperCase() === 'TRUE', children: [] }
    const functionName = word.toUpperCase()
    if (!functions[functionName]) throw new Error(word ? `不支持函数 ${word}。` : '表达式缺少操作数。')
    this.expect('(')
    const args: StaticExpressionNode[] = []
    this.skipSpace()
    if (!this.take(')')) {
      while (true) {
        if (args.length === 64) throw new Error('函数参数过多。')
        args.push(this.parseExpression(0, depth + 1))
        this.skipSpace()
        if (this.take(')')) break
        this.expect(',')
      }
    }
    if (!functions[functionName]!(args.length)) throw new Error(`${functionName} 的参数数量不正确。`)
    return { kind: 'call', name: functionName, value: null, children: args }
  }

  /**
   * 读取当前位置的二元运算符。
   * @returns 运算符和优先级，未匹配时为空。
   */
  private readOperator(): { symbol: string; priority: number } | null {
    const operators: ReadonlyArray<[string, number]> = [['||', 1], ['OR', 1], ['&&', 2], ['AND', 2], ['==', 3], ['!=', 3], ['>=', 4], ['<=', 4], ['>', 4], ['<', 4], ['+', 5], ['-', 5], ['*', 6], ['/', 6]]
    for (const [symbol, priority] of operators) {
      if (this.source.slice(this.offset, this.offset + symbol.length).toUpperCase() !== symbol) continue
      if ((symbol === 'OR' || symbol === 'AND') && /\p{L}/u.test(this.source[this.offset + symbol.length] ?? '')) continue
      return { symbol, priority }
    }
    return null
  }

  /**
   * 从当前位置消费指定文本。
   * @param text 期望文本。
   * @returns 已消费时为真。
   */
  private take(text: string): boolean {
    this.skipSpace()
    if (!this.source.startsWith(text, this.offset)) return false
    this.offset += text.length
    return true
  }

  /**
   * 要求指定文本紧随当前位置。
   * @param text 期望文本。
   */
  private expect(text: string): void {
    if (!this.take(text)) throw new Error(`表达式在第 ${this.offset + 1} 个字符附近缺少 ${text}。`)
  }

  /**
   * 跳过 Unicode 空白。
   */
  private skipSpace(): void {
    while (this.offset < this.source.length && /\s/u.test(this.source[this.offset]!)) this.offset++
  }

  /**
   * 防止特别宽的表达式耗尽前端资源。
   */
  private countToken(): void {
    if (++this.tokens > maxTokens) throw new Error('表达式操作数过多。')
  }
}

/**
 * 静态解析表达式并返回字段引用；非法语法抛中文可纠正错误。
 * @param expression 受限表达式。
 * @returns 语法树及字段路径。
 */
export function parseStaticExpression(expression: string): StaticExpressionResult {
  return new StaticExpressionParser(expression).parse()
}

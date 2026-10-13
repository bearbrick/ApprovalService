/**
 * 注释规范检查（AGENTS.md R5）：前端源码里每个模块级声明都有完整的 JSDoc。
 * 规则和后端、调用端的 XML 注释一致：说明写成三行（不写成一行的 /** … *\/），函数的每个参数有 @param，
 * 有返回值时有 @returns；接口、类型、类的每个成员，store（reactive 对象）的每个状态，组件的 props 和 emits 都要有说明。
 * 函数里面的局部变量和局部函数不要求。用 TypeScript 编译器解析源码，.vue 文件取 <script> 里的代码。
 */
import ts from 'typescript'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'

/**
 * 一个要检查的源文件。
 */
interface SourceUnit {
  /**
   * 给编译器的文件名（.vue 文件后面加 .ts）。
   */
  fileName: string
  /**
   * 报错时显示的路径（相对 frontend 目录）。
   */
  display: string
  /**
   * 代码（.vue 文件是 <script> 里的部分）。
   */
  code: string
  /**
   * 代码在原文件里从第几行开始（0 表示第一行），报错时换算行号。
   */
  lineOffset: number
}

/**
 * 检查 frontend 目录下 src 和 tests 的全部 .ts、.vue 文件。
 * @param root frontend 目录。
 * @returns 每处问题一条说明（文件:行 名称 问题）；没有问题时为空。
 */
export function checkDocs(root: string): string[] {
  const units = [...sourceFiles(join(root, 'src')), ...sourceFiles(join(root, 'tests'))].flatMap((path) => toUnits(root, path))
  const byName = new Map(units.map((unit) => [unit.fileName, unit]))
  const options: ts.CompilerOptions = { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext, moduleResolution: ts.ModuleResolutionKind.Bundler, strict: true, allowImportingTsExtensions: true, noEmit: true, resolveJsonModule: true, skipLibCheck: true }
  const host = ts.createCompilerHost(options)
  const readFile = host.readFile.bind(host)
  const fileExists = host.fileExists.bind(host)
  // .vue 文件以虚拟的 .vue.ts 交给编译器，返回值类型要靠类型检查器判断。
  host.readFile = (name) => byName.get(normalize(name))?.code ?? readFile(name)
  host.fileExists = (name) => byName.has(normalize(name)) || fileExists(name)
  host.getSourceFile = (name, version) => {
    const text = host.readFile(name)
    return text === undefined ? undefined : ts.createSourceFile(name, text, version, true)
  }
  const program = ts.createProgram([...byName.keys()], options, host)
  const checker = program.getTypeChecker()
  const problems: string[] = []
  for (const unit of units) {
    const source = program.getSourceFile(unit.fileName)
    if (source) new FileInspector(source, unit, checker, problems).run()
  }
  return problems
}

/**
 * 逐个检查一个文件里的模块级声明。
 */
class FileInspector {
  /**
   * 解析好的源文件。
   */
  private readonly source: ts.SourceFile
  /**
   * 源文件的信息（显示路径、行号偏移）。
   */
  private readonly unit: SourceUnit
  /**
   * 类型检查器，判断函数有没有返回值。
   */
  private readonly checker: ts.TypeChecker
  /**
   * 问题清单，往里追加。
   */
  private readonly problems: string[]

  /**
   * 准备检查一个文件。
   * @param source 解析好的源文件。
   * @param unit 源文件的信息。
   * @param checker 类型检查器。
   * @param problems 问题清单。
   */
  constructor(source: ts.SourceFile, unit: SourceUnit, checker: ts.TypeChecker, problems: string[]) {
    this.source = source
    this.unit = unit
    this.checker = checker
    this.problems = problems
  }

  /**
   * 检查全部模块级语句。
   */
  run(): void {
    for (const statement of this.source.statements) this.statement(statement)
  }

  /**
   * 检查一条模块级语句：函数、变量、接口、类型、类；组件宏（defineProps、defineEmits）的类型参数里的成员。
   * @param statement 语句。
   */
  private statement(statement: ts.Statement): void {
    if (ts.isFunctionDeclaration(statement)) {
      this.callable(statement, statement, statement.name?.text ?? 'default')
    } else if (ts.isVariableStatement(statement)) {
      for (const declaration of statement.declarationList.declarations) {
        const name = declaration.name.getText(this.source)
        const initializer = declaration.initializer
        if (initializer && (ts.isArrowFunction(initializer) || ts.isFunctionExpression(initializer))) {
          this.callable(statement, initializer, name)
        } else {
          this.require(statement, name)
        }

        if (initializer) this.expression(initializer, name)
      }
    } else if (ts.isInterfaceDeclaration(statement)) {
      this.require(statement, statement.name.text)
      this.members(statement.members, statement.name.text)
    } else if (ts.isTypeAliasDeclaration(statement)) {
      this.require(statement, statement.name.text)
      this.typeMembers(statement.type, statement.name.text)
    } else if (ts.isClassDeclaration(statement)) {
      const name = statement.name?.text ?? 'default'
      this.require(statement, name)
      for (const member of statement.members) this.classMember(member, name)
    } else if (ts.isEnumDeclaration(statement)) {
      this.require(statement, statement.name.text)
    } else if (ts.isExpressionStatement(statement)) {
      this.expression(statement.expression)
    }
  }

  /**
   * 在初始化表达式里找要检查的成员：reactive({...}) 的每个状态，defineProps、defineEmits 的类型参数。
   * @param expression 表达式。
   * @param owner 赋给的变量名（报错时显示，如 designer.template）；没有时用函数名。
   */
  private expression(expression: ts.Expression, owner?: string): void {
    if (!ts.isCallExpression(expression)) return
    const callee = expression.expression.getText(this.source)
    if (callee === 'reactive' && expression.arguments[0] && ts.isObjectLiteralExpression(expression.arguments[0])) {
      this.objectState(expression.arguments[0], owner ?? 'reactive')
    } else if (callee === 'defineProps' || callee === 'defineEmits') {
      for (const type of expression.typeArguments ?? []) this.typeMembers(type, callee)
    } else if (callee === 'withDefaults' && expression.arguments[0]) {
      this.expression(expression.arguments[0], owner)
    }
  }

  /**
   * store 的状态对象：每个属性都要有说明，嵌套的对象（如 ui）逐层检查。
   * @param literal 对象字面量。
   * @param owner 报错时显示的所属名称。
   */
  private objectState(literal: ts.ObjectLiteralExpression, owner: string): void {
    for (const property of literal.properties) {
      const name = `${owner}.${property.name?.getText(this.source) ?? '?'}`
      this.require(property, name)
      if (ts.isPropertyAssignment(property)) {
        const value = skipAssertions(property.initializer)
        if (ts.isObjectLiteralExpression(value)) this.objectState(value, name)
      }
    }
  }

  /**
   * 类型里的成员：类型字面量的每个成员；联合、交叉类型逐个看。
   * @param type 类型节点。
   * @param owner 报错时显示的所属名称。
   */
  private typeMembers(type: ts.TypeNode, owner: string): void {
    if (ts.isTypeLiteralNode(type)) {
      this.members(type.members, owner)
    } else if (ts.isUnionTypeNode(type) || ts.isIntersectionTypeNode(type)) {
      for (const part of type.types) this.typeMembers(part, owner)
    }
  }

  /**
   * 接口或类型字面量的成员：方法签名还要核对参数和返回值。
   * @param members 成员。
   * @param owner 报错时显示的所属名称。
   */
  private members(members: ts.NodeArray<ts.TypeElement>, owner: string): void {
    for (const member of members) {
      const name = `${owner}.${member.name?.getText(this.source) ?? '()'}`
      if (ts.isMethodSignature(member)) {
        this.callable(member, member, name)
      } else {
        this.require(member, name)
      }
    }
  }

  /**
   * 类的成员：属性、方法、访问器、带参数的构造函数。
   * @param member 成员。
   * @param owner 类名。
   */
  private classMember(member: ts.ClassElement, owner: string): void {
    const name = `${owner}.${member.name?.getText(this.source) ?? 'constructor'}`
    if (ts.isMethodDeclaration(member) || ts.isGetAccessorDeclaration(member) || (ts.isConstructorDeclaration(member) && member.parameters.length > 0)) {
      this.callable(member, member, name)
    } else if (ts.isPropertyDeclaration(member) || ts.isSetAccessorDeclaration(member)) {
      this.require(member, name)
    }
  }

  /**
   * 函数类的声明：要有说明，每个参数有 @param，有返回值时有 @returns。
   * @param host JSDoc 挂在哪个节点上（变量声明的 JSDoc 在整条语句上）。
   * @param fn 函数本身（取参数和返回值）。
   * @param name 报错时显示的名称。
   */
  private callable(host: ts.Node, fn: ts.SignatureDeclaration, name: string): void {
    const doc = this.require(host, name)
    if (!doc) return
    const tags: readonly ts.JSDocTag[] = doc.tags ?? []
    const documented = new Set(tags
      .filter((tag): tag is ts.JSDocParameterTag => ts.isJSDocParameterTag(tag) && !!ts.getTextOfJSDocComment(tag.comment)?.trim())
      .map((tag) => tag.name.getText(this.source)))
    fn.parameters.forEach((parameter, index) => {
      const parameterName = ts.isIdentifier(parameter.name) ? parameter.name.text : null
      if (parameterName === 'this') return
      // 解构的参数没有名字，按个数核对。
      const ok = parameterName ? documented.has(parameterName) : documented.size > index
      if (!ok) this.report(host, name, `缺参数 ${parameterName ?? `#${index + 1}`} 的说明`)
    })
    const returns = tags.find((tag) => ts.isJSDocReturnTag(tag))
    if (this.returnsValue(fn) && !ts.getTextOfJSDocComment(returns?.comment)?.trim()) this.report(host, name, '缺返回值说明')
  }

  /**
   * 函数有没有返回值：返回类型不是 void、undefined、null、never，也不是 Promise<void>（只是赋值的箭头函数不算有返回值）。构造函数和 set 访问器没有。
   * @param fn 函数。
   * @returns 有返回值时为 true。
   */
  private returnsValue(fn: ts.SignatureDeclaration): boolean {
    if (ts.isConstructorDeclaration(fn) || ts.isSetAccessorDeclaration(fn)) return false
    const signature = this.checker.getSignatureFromDeclaration(fn)
    if (!signature) return false
    let type = this.checker.getReturnTypeOfSignature(signature)
    const symbol = type.getSymbol()
    if (symbol?.name === 'Promise') type = this.checker.getTypeArguments(type as ts.TypeReference)[0] ?? type
    const empty = ts.TypeFlags.Void | ts.TypeFlags.Undefined | ts.TypeFlags.Null | ts.TypeFlags.Never
    return !(type.flags & empty)
  }

  /**
   * 要求节点前面有 JSDoc：说明不为空，写成多行。文件最开头、和下面的声明隔着空行的 JSDoc 是整个文件的说明，不算这个声明的注释。
   * @param node 节点。
   * @param name 报错时显示的名称。
   * @returns 节点的 JSDoc；没有时为 null（已记下问题）。
   */
  private require(node: ts.Node, name: string): ts.JSDoc | null {
    const header = this.source.text.search(/\S/)
    const isHeader = (item: ts.JSDoc) => item.getStart(this.source) === header && /\n[ \t]*\r?\n/.test(this.source.text.slice(item.end, node.getStart(this.source)))
    const docs = (node as { jsDoc?: ts.JSDoc[] }).jsDoc?.filter((item) => !isHeader(item))
    const doc = docs?.[docs.length - 1]
    if (!doc) {
      this.report(node, name, '没有注释')
      return null
    }

    if (!ts.getTextOfJSDocComment(doc.comment)?.trim()) this.report(node, name, '没有说明')
    if (!doc.getText(this.source).includes('\n')) this.report(node, name, '注释要写成三行（/**、说明、*/）')
    return doc
  }

  /**
   * 记下一处问题，行号换算成原文件的行号。
   * @param node 出问题的节点。
   * @param name 名称。
   * @param message 问题。
   */
  private report(node: ts.Node, name: string, message: string): void {
    const line = this.source.getLineAndCharacterOfPosition(node.getStart(this.source)).line + 1 + this.unit.lineOffset
    this.problems.push(`${this.unit.display}:${line} ${name} ${message}`)
  }
}

/**
 * 去掉 as、satisfies、括号，取里面的表达式（如 {} as Record<string, Metric>）。
 * @param expression 表达式。
 * @returns 里面的表达式。
 */
function skipAssertions(expression: ts.Expression): ts.Expression {
  let current = expression
  while (ts.isAsExpression(current) || ts.isSatisfiesExpression(current) || ts.isParenthesizedExpression(current)) current = current.expression
  return current
}

/**
 * 读一个文件。.vue 文件的每个 <script> 块（普通的和 setup 的）各算一个源文件，并记下它从第几行开始。
 * @param root frontend 目录。
 * @param path 文件的完整路径。
 * @returns 要检查的源文件。
 */
function toUnits(root: string, path: string): SourceUnit[] {
  const text = readFileSync(path, 'utf8')
  const display = relative(root, path).replaceAll('\\', '/')
  if (!path.endsWith('.vue')) return [{ fileName: normalize(path), display, code: text, lineOffset: 0 }]
  return [...text.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/g)].map((match, index) => {
    const start = (match.index ?? 0) + match[0].indexOf('>') + 1
    return { fileName: normalize(`${path}.${index}.ts`), display, code: match[1], lineOffset: text.slice(0, start).split('\n').length - 1 }
  })
}

/**
 * 目录下的全部源码文件（递归）：.ts 和 .vue，不含类型声明文件 .d.ts。
 * @param directory 目录。
 * @returns 文件的完整路径。
 */
export function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((name) => {
    const path = join(directory, name)
    if (statSync(path).isDirectory()) return sourceFiles(path)
    return /\.(ts|vue)$/.test(path) && !path.endsWith('.d.ts') ? [path] : []
  })
}

/**
 * 统一路径写法（编译器内部用正斜杠），用来查虚拟文件。
 * @param path 路径。
 * @returns 正斜杠的路径。
 */
function normalize(path: string): string {
  return path.replaceAll('\\', '/')
}

/**
 * 检查程序用到的几个 Node 内置函数的类型。前端没有引用 @types/node（只有检查程序在 Node 下运行），
 * 这里只声明用到的部分，让 vue-tsc 能检查 tests 目录。
 */
declare module 'node:fs' {
  /**
   * 读取文本文件。
   * @param path 文件路径。
   * @param encoding 编码，固定 utf8。
   * @returns 文件内容。
   */
  export function readFileSync(path: string, encoding: 'utf8'): string
  /**
   * 列出目录里的文件和子目录名。
   * @param path 目录。
   * @returns 名称列表。
   */
  export function readdirSync(path: string): string[]
  /**
   * 取文件信息。
   * @param path 路径。
   * @returns 文件信息（只用到是否目录）。
   */
  export function statSync(path: string): { isDirectory(): boolean }
}

declare module 'node:path' {
  /**
   * 拼接路径。
   * @param parts 路径的各段。
   * @returns 拼好的路径。
   */
  export function join(...parts: string[]): string
  /**
   * 相对路径。
   * @param from 起点目录。
   * @param to 目标路径。
   * @returns 从 from 到 to 的相对路径。
   */
  export function relative(from: string, to: string): string
}

declare module 'node:url' {
  /**
   * 把 file: URL 转成本地路径。
   * @param url file: URL。
   * @returns 本地路径。
   */
  export function fileURLToPath(url: string | URL): string
}

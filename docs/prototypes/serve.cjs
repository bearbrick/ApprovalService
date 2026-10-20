/**
 * 使用 Node 内置模块提供本地原型预览，不需要安装依赖。
 */
const http = require('node:http');
/**
 * 读取本地文件。
 */
const fs = require('node:fs');
/**
 * 规范化文件路径。
 */
const path = require('node:path');
/**
 * 服务仅公开当前原型目录。
 */
const root = __dirname;
/**
 * 可通过环境变量为并行评审指定端口。
 */
const port = Number(process.env.APPROVAL_PROTOTYPE_PORT || 4174);
/**
 * 返回指定资源的媒体类型。
 */
const types = {'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.png':'image/png','.jpg':'image/jpeg','.svg':'image/svg+xml','.md':'text/plain; charset=utf-8'};
/**
 * 提供只读的原型文件访问，拒绝目录外路径。
 * @param {import('node:http').IncomingMessage} request 请求。
 * @param {import('node:http').ServerResponse} response 响应。
 * @returns {void} 无返回值。
 */
function serve(request,response) {
    let name;
    try { name = decodeURIComponent(new URL(request.url,'http://127.0.0.1').pathname); }
    catch { response.writeHead(400);response.end('请求地址不正确');return; }
    const file = path.resolve(root,'.'+(name==='/'?'/designer.html':name));
    if(!file.startsWith(root+path.sep)){response.writeHead(403);response.end('不能访问原型目录以外的文件');return;}
    fs.readFile(file,(error,data)=>{
        if(error){response.writeHead(404);response.end('页面不存在');return;}
        response.setHeader('Content-Type',types[path.extname(file)]||'application/octet-stream');
        response.setHeader('Cache-Control','no-store');response.end(data);
    });
}
http.createServer(serve).listen(port,'127.0.0.1',()=>process.stdout.write(`审批服务原型：http://127.0.0.1:${port}/designer.html\n`));

/**
 * 原型导航及建议评审路径。
 */
const prototypePages=[
 ['edit','流程设计器','流程绘制、审批规则、数据与排版','designer.html','切换两种画法，选中审批节点调整审批人和会签方式；配置字段后预览、测试并发布。'],
 ['table','审批中心','待办、详情、审批操作与打印','center.html','打开一张待办，查看数据与流程，填写意见并同意；再到已办中查看记录。'],
 ['gear','管理后台','组织、权限、接入、运行与报表','admin.html','检查组织绑定和接入向导，进入回调队列查看失败记录、重试和运行干预。'],
 ['preview','手机审批','四个底部标签与触屏审批','mobile.html','打开待办，在详情底部完成审批；检查意见草稿、抄送和个人设置。'],
 ['box','示例业务系统','业务单据、发起、回写与场景','demo.html','选场景、预览并提交单据，模拟处理、补充与重新提交，观察业务状态回写。'],
 ['user','账号登录','独立账号与统一登录预留','login.html','查看登录、记住登录、演示身份和首次修改密码的交互。']
];
UI.shell('overview','审批服务 · 原型评审','设计基准：标签服务最终页面 · 2026-10-13 · 本地交互演示',`<div class="notice" style="margin-bottom:20px">所有页面均使用虚构样例，可直接点击体验。原型覆盖初版产品范围；规则引擎、权限校验、接口调用和持久化将在后续开发任务中实现。</div><section class="panel"><div class="panel-head"><h2>页面与评审路径</h2><span class="pill accent">6 个模块</span></div><div class="table-wrap"><table><thead><tr><th style="width:200px">模块</th><th style="width:260px">覆盖内容</th><th>建议体验</th><th></th></tr></thead><tbody>${prototypePages.map(([icon,title,scope,url,walk])=>`<tr><td><a href="${url}" style="font-size:14px;font-weight:600;display:flex;gap:10px;align-items:center">${UI.icon(icon)}${title}</a></td><td>${scope}</td><td class="muted">${walk}</td><td><a class="btn sm" href="${url}">打开</a></td></tr>`).join('')}</tbody></table></div></section><div class="grid-2"><section class="panel"><div class="panel-head"><h2>统一视觉规范</h2></div><div class="panel-body"><p style="margin-bottom:14px">沿用标签服务的品牌资产、Bootstrap 图标、深蓝顶栏、浅色导航、下划线页签、紧凑表格与属性面板。</p><div class="toolbar"><span class="pill accent">主操作</span><span class="pill ok">已通过</span><span class="pill warn">待处理</span><span class="pill danger">需关注</span></div><div class="toolbar"><button class="btn primary" onclick="UI.toast('主操作状态示例')">主要按钮</button><button class="btn" onclick="UI.toast('次要操作状态示例')">次要按钮</button><button class="btn" disabled>禁用状态</button></div></div></section><section class="panel"><div class="panel-head"><h2>评审时重点看什么</h2></div><div class="panel-body"><p style="margin-bottom:12px">1. 业务人员是否能找到需要的流程、配置和待办。</p><p style="margin-bottom:12px">2. 审批前是否看得清数据、意见、操作后果和当前进度。</p><p style="margin-bottom:12px">3. 手机操作是否方便，表格和复杂配置是否仍然清晰。</p><p class="hint">评审说明与覆盖清单保存在原型目录 README.md；本轮不改变生产接口或已评审的设计书。</p></div></section></div>`,`<a class="btn primary" href="designer.html">${UI.icon('edit')} 开始体验流程设计</a>`);

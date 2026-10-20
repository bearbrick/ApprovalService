/**
 * 业务示例的本地状态；审批单据仅由这个业务系统页面提交。
 */
const demo = JSON.parse(localStorage.getItem('approval-prototype-business') || 'null') || {type:'expense',status:'草稿',title:'10 月深圳客户现场服务差旅',amount:1334.50,user:'王强',step:0,backend:true,events:[],tab:'document'};
/**
 * 可演示的完整业务场景，顺序对应设计书。
 */
const scenarios = [
 ['小额报销','860 元差旅，验证相邻去重和办理节点','expense',860],['三路会审','项目、跨部门、招待费触发包容分支','expense',6200],['大额招待','单张招待费 2600 元触发总经理审批','expense',2600],['补票据','会计要求补充，原节点恢复处理','expense',1334.50],['核定金额','财务核定 1200 元并触发复核','expense',1334.50],['退回从头走','修改后重新经过直属上级','expense',6800],['撤回限制','首人处理前允许撤回','expense',860],['委托代理','刘副总代周总处理，保留双方身份','expense',26000],['一人多部门','李娜从研发部发起，按发起部门找人','expense',1600],['预算不足','预算余额 8000 元，提交采购受阻','purchase',12800],['单一来源票签','采购经理 2 票，60% 通过','purchase',48000],['大额采购','合同子流程、会签和自动生成订单','purchase',120000],['退回到节点','采购经理退回，重提回采购经理','purchase',38000],['接口失败重试','订单接口两次失败后第三次成功','purchase',12800],['待办与内嵌审批','展示角标、跳转及嵌入中心','expense',1334.50],['没有业务后端','查询同步与回调去重','purchase',12800],['审批单打印','含审批意见、签名和票据页','expense',1334.50]
];
/**
 * 保存演示状态。
 * @returns {void} 无返回值。
 */
function saveDemo(){localStorage.setItem('approval-prototype-business',JSON.stringify(demo));}
/**
 * 格式化金额。
 * @param {number} amount 金额。
 * @returns {string} 显示值。
 */
function money(amount){return Number(amount).toLocaleString('zh-CN',{minimumFractionDigits:2,maximumFractionDigits:2});}
/**
 * 按选定场景返回可见的流程步骤。
 * @returns {string[]} 流程步骤。
 */
function demoSteps(){
 if(demo.scenario==='三路会审')return ['直属上级 · 李娜','包容会审 · 项目经理、研发、行政','部门负责人 · 相邻去重','财务审核 · 赵敏 / 钱丽','出纳付款 · 刘洋'];
 if(demo.scenario==='单一来源票签')return ['部门负责人','采购委员会 · 权重票签 ≥ 60%','采购经理 → 分管领导','自动生成采购订单'];
 if(demo.type==='purchase')return demo.amount>50000?['部门负责人','合同评审子流程 · 法务 / 财务','采购经理 · 吴芳','分管领导 · 陈副总','总经理 / 财务总监 · 会签','自动生成采购订单']:['部门负责人','采购经理 · 吴芳','分管领导 · 陈副总','自动生成采购订单'];
 return demo.amount>20000||demo.scenario==='大额招待'?['直属上级 · 李娜','部门负责人','分管领导','总经理 · 周总','财务审核 · 赵敏 / 钱丽','出纳付款 · 刘洋']:['直属上级 · 李娜','部门负责人 · 相邻去重','财务审核 · 赵敏 / 钱丽','出纳付款 · 刘洋'];
}
/**
 * 渲染业务单据及审批同步区域。
 * @returns {string} 页面片段。
 */
function documentView(){
 const purchase=demo.type==='purchase',editable=['草稿','已撤回','待补充','已退回'].includes(demo.status),steps=demoSteps();
 return `<div class="demo-overview"><div class="demo-status"><span class="pill accent">${purchase?'采购申请':'费用报销'}</span><span class="mono">${purchase?'PR20261013001':'FY20261013001'}</span><span class="pill ${demo.status==='已通过'?'ok':demo.status==='草稿'?'':'warn'}">${demo.status}</span></div><div class="toolbar" style="margin:0"><label for="business-user" class="muted">当前用户</label><select id="business-user" class="sel inline-role">${['王强','李娜','赵敏','吴芳','周总'].map(p=>`<option ${p===demo.user?'selected':''}>${p}</option>`).join('')}</select></div></div><div class="demo-layout"><section class="panel"><div class="panel-head"><h2>${UI.escape(demo.title)}</h2><span class="hint">业务系统中的单据</span></div><div class="doc-summary"><div><label>申请人 / 部门</label><b>${demo.user} · ${demo.scenario==='一人多部门'?'研发部':'生产一部'}</b></div><div><label>申请日期</label><b>2026-10-13</b></div><div><label>申请金额（元）</label><b class="amount">${money(demo.amount)}</b></div></div><div class="demo-detail"><div class="form-grid"><label class="field full">${purchase?'采购事由':'报销事由'}<input class="inp" id="business-title" value="${UI.escape(demo.title)}" ${editable?'':'disabled'}></label><label class="field">${purchase?'供应商':'费用承担部门'}<input class="inp" value="${purchase?'华强电子':'生产部'}" ${editable?'':'disabled'}></label><label class="field">${purchase?'期望交货日期':'项目'}<input class="inp" value="${purchase?'2026-10-30':'客户现场服务项目'}" ${editable?'':'disabled'}></label></div><div class="table-wrap"><table><thead><tr><th>${purchase?'物料名称':'费用项目'}</th><th>${purchase?'数量':'日期'}</th><th>说明</th><th style="text-align:right">金额（元）</th></tr></thead><tbody>${purchase?`<tr><td>65W 电源适配器</td><td>500 个</td><td>EU 规格 · 产线备料</td><td style="text-align:right">${money(demo.amount)}</td></tr>`:`<tr><td>交通费</td><td>2026-10-08</td><td>深圳往返高铁</td><td style="text-align:right">${money(demo.amount===1334.5?686:demo.amount*.6)}</td></tr><tr><td>住宿费</td><td>2026-10-08</td><td>酒店 2 晚</td><td style="text-align:right">${money(demo.amount===1334.5?560:demo.amount*.3)}</td></tr><tr><td>市内交通</td><td>2026-10-09</td><td>客户现场往返</td><td style="text-align:right">${money(demo.amount===1334.5?88.5:demo.amount*.1)}</td></tr>`}<tr><td colspan="3"><b>合计</b></td><td style="text-align:right"><b>${money(demo.amount)}</b></td></tr></tbody></table></div><div class="toolbar" style="margin-top:18px"><span class="muted">附件</span><button class="btn sm" id="demo-file">${UI.icon('image')} ${purchase?'供应商报价单.pdf':'行程与票据.pdf'}</button>${editable?'<button class="btn sm" id="demo-upload">补充附件</button>':''}</div>${demo.status==='待补充'?'<div class="notice warn">会计赵敏要求补充酒店发票。请在业务系统补齐材料后重新提交，审批将回到财务审核。</div>':''}${demo.approvedAmount?`<div class="notice">审批回写：核定金额 ¥${money(demo.approvedAmount)}；付款日期 2026-10-15。</div>`:''}${demo.orderNo?`<div class="notice">审批回写：采购订单 ${demo.orderNo} 已由业务系统生成。</div>`:''}<div class="toolbar" style="margin-top:22px;margin-bottom:0">${editable?`<button class="btn" id="demo-save">保存单据</button><button class="btn" id="demo-preview">预览审批流程</button><button class="btn primary" id="demo-submit">${['待补充','已退回'].includes(demo.status)?'补充并重新提交':'提交审批'}</button>`:`<button class="btn" id="demo-query">${UI.icon('refresh')} 刷新审批状态</button><button class="btn" id="demo-remind">催办</button><button class="btn" id="demo-withdraw" ${demo.step>0||demo.status!=='审批中'?'disabled title="本流程仅在首人处理前允许撤回"':''}>撤回</button><button class="btn" id="demo-print">${UI.icon('preview')} 审批单预览</button>`}</div></div></section><aside><div class="panel"><div class="panel-head"><h3>审批进度</h3><span class="pill ${demo.status==='已通过'?'ok':'accent'}">${demo.status==='草稿'?'未提交':'SP-202610-0007'}</span></div><div class="flow-steps">${steps.map((s,i)=>`<div class="flow-step ${demo.step>i?'done':demo.status==='审批中'&&demo.step===i?'current':''}"><b>${s}</b><small>${demo.step>i?'已完成 · 审批意见已记录':demo.status==='审批中'&&demo.step===i?'待处理 · 已发送提醒':'等待前序节点'}</small></div>`).join('')}</div><div class="sync-box"><label class="check"><input id="demo-backend" type="checkbox" ${demo.backend?'checked':''}> 启用演示业务后端</label><p class="hint">${demo.backend?'状态变化通过回调更新，按事件 ID 去重。':'无业务后端，使用打开单据时查询的方式同步。'}</p><button class="btn wide" id="demo-inbox">${UI.icon('table')} 打开待办审批</button></div></div><div class="notice">${demo.scenario?`当前场景：${demo.scenario}<br>`:''}这里演示业务系统如何接入；所有操作只影响浏览器样例。</div></aside></div>`;
}
/**
 * 渲染场景列表与操作记录。
 * @returns {void} 无返回值。
 */
function renderDemo(){
 const tabs=[['document','业务单据'],['scenarios','17 个演示场景'],['events','回调与同步记录'],['integration','对接改动']];
 let content=documentView();
 if(demo.tab==='scenarios')content=`<div class="notice" style="margin-bottom:18px">选择场景将载入对应的虚构单据。提交后可在“打开待办审批”中模拟处理，观察流程和业务状态变化。</div><div class="scenario-cards">${scenarios.map((s,i)=>`<button class="scenario-card" data-scenario="${i}"><span class="pill">场景 ${String(i+1).padStart(2,'0')}</span><b>${s[0]}</b><p>${s[1]}</p></button>`).join('')}</div>`;
 if(demo.tab==='events')content=`<section class="panel"><div class="panel-head"><h2>事件投递记录</h2><span class="hint">本地演示 · 接收后按 eventId 去重</span></div>${demo.events.length?`<div class="event-list">${demo.events.map((e,i)=>`<div class="event-row"><span class="pill ${e.ok?'ok':'warn'}">${e.ok?'已同步':'待查询'}</span> ${UI.escape(e.text)}<small>${e.time} · event-demo-${String(i+1).padStart(4,'0')}</small></div>`).join('')}</div>`:'<div class="empty">暂无事件，提交一张示例单据后查看。</div>'}</section>`;
 if(demo.tab==='integration')content=`<div class="grid-2"><section class="panel"><div class="panel-head"><h2>业务系统需要增加的能力</h2><span class="pill accent">数据接口</span></div><table><thead><tr><th>位置</th><th>职责</th></tr></thead><tbody>${[['单据模型','保存审批单 ID、审批状态和回写字段'],['提交按钮','预览、选人、发起审批'],['单据操作','撤回、补充和重新提交'],['取数接口','按单号返回业务数据快照'],['回调接收','验签、事件去重、回写状态'],['查询刷新','无业务后端时查询审批状态'],['待办入口','角标、跳转或内嵌审批中心'],['业务联动','预算校验、项目经理取人、生成订单']].map(row=>`<tr><td>${row[0]}</td><td>${row[1]}</td></tr>`).join('')}</tbody></table></section><section class="panel"><div class="panel-head"><h2>提交请求示意</h2><span class="pill">POST /api/v1/instances</span></div><div class="panel-body"><pre>${UI.escape(JSON.stringify({flowCode:demo.type==='expense'?'EXPENSE':'PURCHASE_REQUEST',businessKey:demo.type==='expense'?'FY20261013001':'PR20261013001',initiator:'E10023',title:demo.title},null,2))}</pre><p class="hint">API Key 由业务服务端持有。这里不包含真实密钥，不向任何接口发送请求。生产对接类库独立于 WinUI 界面。</p></div></section></div>`;
 UI.shell('demo','示例业务系统','费用报销 / 采购申请 · 单据由业务系统维护，审批状态通过接口同步',`<nav class="tabs" aria-label="示例业务页签">${tabs.map(([id,label])=>`<button class="${demo.tab===id?'active':''}" data-tab="${id}">${label}</button>`).join('')}</nav>${content}`,`<a class="btn" href="winui.html">WinUI 3 客户端</a><button class="btn" id="demo-reset">${UI.icon('refresh')} 重置样例</button><button class="btn primary" id="demo-new">${UI.icon('plus')} 新建业务单据</button>`);
 bindDemo();
}
/**
 * 记录本地事件。
 * @param {string} text 事件说明。
 * @returns {void} 无返回值。
 */
function demoEvent(text){demo.events.unshift({text,time:new Date().toLocaleTimeString('zh-CN',{hour12:false}),ok:demo.backend});saveDemo();}
/**
 * 预览审批人与路由。
 * @returns {void} 无返回值。
 */
function previewDemo(){UI.modal('预览审批流程',`<div class="notice">审批人按当前组织和单据数据预览，到节点时重新计算。</div><div class="flow-steps">${demoSteps().map(s=>`<div class="flow-step"><b>${s}</b><small>条件符合 · ${demo.user}发起</small></div>`).join('')}</div><label class="field">补充指定的抄送人<select class="sel"><option>费用承担部门负责人</option><option>采购部</option><option>不额外抄送</option></select></label>`,()=>{},'确认预览');}
/**
 * 提交前保存输入并执行示例校验。
 * @returns {void} 无返回值。
 */
function submitDemo(){
 demo.title=document.querySelector('#business-title')?.value.trim()||demo.title;
 if(demo.scenario==='预算不足'){UI.modal('预算校验未通过','<div class="notice warn">本次采购 ¥12,800.00，预算余额 ¥8,000.00，还差 ¥4,800.00。请调整业务单据或申请预算后重试。</div>',()=>{demo.amount=7800;demo.scenario='预算调整后采购';renderDemo();},'演示：调整为 ¥7,800');return;}
 UI.modal('确认提交审批',`<p>将“${UI.escape(demo.title)}”提交至${demo.type==='expense'?'费用报销':'采购申请'}流程。</p><div class="notice" style="margin-top:16px">提交后业务单据锁定。审批服务保存一份数据快照，当前金额 ¥${money(demo.amount)}。</div>`,()=>{const resume=['待补充','已退回'].includes(demo.status);demo.status='审批中';if(!resume)demo.step=0;demoEvent(resume?'instance.resubmitted · 已重新提交':'instance.started · 已创建审批单');renderDemo();UI.toast('提交成功，待办已生成');},'确认提交');
}
/**
 * 展示可推进的演示审批操作。
 * @returns {void} 无返回值。
 */
function openDemoInbox(){
 if(demo.status!=='审批中'){UI.toast('请先提交业务单据');return;}
 UI.modal('演示待办 · '+demoSteps()[demo.step],`<div class="notice">在业务系统中调用审批操作的交互示例，可切换至审批中心查看完整页面。</div><div class="form-grid" style="margin-top:16px"><label class="field">处理结果<select id="demo-decision" class="sel"><option>同意</option><option>拒绝</option><option>退回</option><option>要求补充</option></select></label><label class="field">财务核定金额<input class="inp" id="demo-approved-amount" type="number" step="0.01" min="0" value="${demo.amount}"></label><label class="field full">审批意见<textarea class="inp" id="demo-opinion" required>情况属实，同意。</textarea></label></div><p class="section-note"><a href="center.html" target="_blank">打开完整审批中心</a> · <button class="btn ghost sm" id="demo-embed-open">预览内嵌页面</button></p>`,d=>{
 const result=d.querySelector('#demo-decision').value;
 if(result==='同意'){demo.step++;if(demo.step>=demoSteps().length){demo.status='已通过';demo.approvedAmount=Number(d.querySelector('#demo-approved-amount').value);if(demo.type==='purchase')demo.orderNo='PO20261015008';}}
 else if(result==='拒绝')demo.status='已拒绝';else if(result==='要求补充')demo.status='待补充';else{demo.status='已退回';if(demo.type==='expense')demo.step=0;}
 demoEvent(`${result} · ${demo.status} · ${d.querySelector('#demo-opinion').value}`);renderDemo();UI.toast('审批操作已记录，业务状态已同步');},'确认处理');
 document.querySelector('#demo-embed-open').onclick=()=>UI.modal('内嵌审批中心',`<iframe class="demo-embed" src="center.html" title="内嵌审批中心"></iframe>`,()=>{},'返回业务单据');
}
/**
 * 绑定当前页面的交互。
 * @returns {void} 无返回值。
 */
function bindDemo(){
 document.querySelectorAll('[data-tab]').forEach(b=>b.onclick=()=>{demo.tab=b.dataset.tab;renderDemo();});
 document.querySelectorAll('[data-scenario]').forEach(b=>b.onclick=()=>{const s=scenarios[Number(b.dataset.scenario)];Object.assign(demo,{scenario:s[0],type:s[2],amount:s[3],status:'草稿',step:0,title:s[0]+' · '+s[1],tab:'document',approvedAmount:null,orderNo:null,backend:s[0]!=='没有业务后端'});saveDemo();renderDemo();});
 const on=(id,fn)=>{const el=document.getElementById(id);if(el)el.onclick=fn;};
 on('demo-new',()=>UI.modal('新建业务单据','<div class="form-grid"><label class="field">单据类型<select id="new-type" class="sel"><option value="expense">费用报销</option><option value="purchase">采购申请</option></select></label><label class="field">金额（元）<input id="new-amount" class="inp" type="number" min="0.01" step="0.01" required value="1334.50"></label><label class="field full">事由<input id="new-title" class="inp" required value="10 月深圳客户现场服务差旅"></label></div>',d=>{Object.assign(demo,{type:d.querySelector('#new-type').value,amount:Number(d.querySelector('#new-amount').value),title:d.querySelector('#new-title').value,status:'草稿',step:0,tab:'document',scenario:null,approvedAmount:null,orderNo:null});saveDemo();renderDemo();},'创建单据'));
 on('demo-reset',()=>UI.modal('重置演示数据','<p>清空此业务示例的本地进度和事件记录，恢复费用报销草稿。</p>',()=>{localStorage.removeItem('approval-prototype-business');location.reload();},'重置'));
 on('demo-save',()=>{demo.title=document.querySelector('#business-title').value;saveDemo();renderDemo();UI.toast('业务单据草稿已保存');});on('demo-preview',previewDemo);on('demo-submit',submitDemo);on('demo-inbox',openDemoInbox);
 on('demo-withdraw',()=>UI.modal('撤回审批','<p>首位审批人尚未处理，可以撤回并修改业务单据。</p>',()=>{demo.status='已撤回';demoEvent('instance.withdrawn · 业务系统撤回');renderDemo();},'确认撤回'));
 on('demo-query',()=>{demo.events.forEach(e=>e.ok=true);demoEvent('查询同步完成 · 当前状态 '+demo.status);renderDemo();UI.toast('已同步当前审批状态');});
 on('demo-remind',()=>{if(demo.lastRemind&&Date.now()-demo.lastRemind<3600000){UI.toast('当前节点每小时最多催办一次');return;}demo.lastRemind=Date.now();demoEvent('已催办当前审批人');UI.toast('催办提醒已发送');});
 on('demo-print',()=>UI.modal('审批单预览',`<h2 style="text-align:center;margin-bottom:20px">${demo.type==='expense'?'费用报销':'采购申请'}审批单</h2><table><tbody><tr><th>审批编号</th><td>SP-202610-0007</td><th>状态</th><td>${demo.status}</td></tr><tr><th>申请人</th><td>${demo.user}</td><th>合计</th><td>¥${money(demo.amount)}</td></tr></tbody></table><h3 style="margin:20px 0">审批记录</h3>${demoSteps().map((s,i)=>`<p style="padding:8px;border-bottom:1px solid var(--line)">${s} <span class="pill ${i<demo.step?'ok':''}">${i<demo.step?'已处理':'未处理'}</span></p>`).join('')}<p class="section-note">附件：票据 3 张；打印样式完整配置见流程设计器。</p>`,()=>{window.print();},'打印'));
 on('demo-file',()=>UI.modal('附件预览','<div class="notice">行程与票据.pdf · 3 页 · 1.2 MB · 示例附件</div><table style="margin-top:18px"><tr><th>票据</th><th>金额</th></tr><tr><td>高铁行程单</td><td>¥686.00</td></tr><tr><td>酒店发票</td><td>¥560.00</td></tr><tr><td>出租车发票</td><td>¥88.50</td></tr></table>',()=>{},'关闭'));
 on('demo-upload',()=>UI.modal('补充附件','<label class="field">选择演示附件<input type="file" class="inp" id="demo-file-input" accept=".pdf,.png,.jpg" required></label><p class="hint">只读取文件名用于演示，不上传文件。</p>',d=>{demoEvent('补充附件 · '+d.querySelector('#demo-file-input').files[0].name);UI.toast('附件已加入单据');},'添加'));
 const user=document.querySelector('#business-user');if(user)user.onchange=e=>{demo.user=e.target.value;saveDemo();renderDemo();};
 const backend=document.querySelector('#demo-backend');if(backend)backend.onchange=e=>{demo.backend=e.target.checked;saveDemo();renderDemo();};
}
renderDemo();

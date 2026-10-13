/** 管理原型使用的独立演示状态，不包含任何真实凭据。 */
const adminDefaults={page:'overview',tab:'',search:'',status:'全部状态',dept:'全部部门',reportPeriod:'本周',reportFlow:'全部流程',reportDept:'全部部门',reportApp:'全部来源',
  flows:[{name:'采购申请',code:'PURCHASE_REQUEST',version:'v2',mode:'钉钉式',app:'ERP 采购系统',status:'已发布'},{name:'费用报销',code:'EXPENSE',version:'v3',mode:'钉钉式',app:'费用报销系统',status:'已发布'},{name:'合同评审',code:'CONTRACT_REVIEW',version:'v1',mode:'钉钉式',app:'ERP 采购系统',status:'已发布'},{name:'质量异常处理',code:'QUALITY_EXCEPTION',version:'v1',mode:'自由画布',app:'ERP 采购系统',status:'草稿'},{name:'用印申请',code:'SEAL_REQUEST',version:'v1',mode:'钉钉式',app:'ERP 采购系统',status:'草稿'}],
  people:[{name:'周总',code:'EMP001',dept:'总经办',job:'总经理',level:'P9',manager:'—',status:'已开通'},{name:'吴芳',code:'EMP002',dept:'采购部',job:'采购经理',level:'P6',manager:'陈副总',status:'已开通'},{name:'赵敏',code:'EMP003',dept:'财务部',job:'会计',level:'P4',manager:'何静',status:'已开通'},{name:'钱丽',code:'EMP004',dept:'财务部',job:'会计',level:'P4',manager:'何静',status:'已开通'},{name:'王强',code:'EMP005',dept:'生产一部',job:'生产专员',level:'P4',manager:'李娜',status:'已开通'},{name:'郑涛',code:'EMP006',dept:'采购部',job:'采购员',level:'P4',manager:'吴芳',status:'已离职'},{name:'冯伟',code:'EMP007',dept:'财务部',job:'财务总监',level:'P8',manager:'陈副总',status:'已开通'},{name:'何静',code:'EMP008',dept:'财务部',job:'财务经理',level:'P6',manager:'冯伟',status:'已开通'},{name:'李娜',code:'EMP009',dept:'生产一部',job:'主管',level:'P5',manager:'张明',status:'已开通'},{name:'张明',code:'EMP010',dept:'生产部',job:'部门负责人',level:'P6',manager:'刘副总',status:'已开通'},{name:'陈副总',code:'EMP011',dept:'总经办',job:'分管采购、财务',level:'P8',manager:'周总',status:'已开通'},{name:'刘副总',code:'EMP012',dept:'总经办',job:'分管生产、行政',level:'P8',manager:'周总',status:'已开通'}],
  bindings:[{type:'人员',external:'erp-u-1001',person:'吴芳 / EMP002',mode:'工号匹配'},{type:'人员',external:'erp-u-1002',person:'赵敏 / EMP003',mode:'手工绑定'},{type:'部门',external:'erp-d-003',person:'采购部',mode:'接口推送'}],
  roles:[{name:'系统管理员',members:'管理员',scope:'全部数据',rights:'系统配置、组织管理、流程管理、运行干预、统计报表'},{name:'流程管理员',members:'吴芳、赵敏',scope:'授权流程',rights:'流程管理、运行干预、统计报表'},{name:'部门负责人',members:'吴芳',scope:'本部门及下级',rights:'统计报表、审批记录'}],
  matrix:[{dept:'采购部',amount:'0 ≤ 金额 < 10,000',category:'全部',person:'吴芳'},{dept:'采购部',amount:'10,000 ≤ 金额 < 100,000',category:'全部',person:'赵敏、吴芳'},{dept:'全部部门',amount:'金额 ≥ 100,000',category:'固定资产',person:'周总'}],
  holidays:{1:'国庆假期',2:'国庆假期',3:'国庆假期',4:'国庆假期',5:'国庆假期',6:'国庆假期',7:'国庆假期',10:'调休上班'},
  apps:[{name:'ERP 采购系统',code:'erp-demo',flows:'采购申请、合同审批',callback:'https://erp.example.com/approval/callback',status:'启用'},{name:'费用报销系统',code:'expense-demo',flows:'费用报销',callback:'https://expense.example.com/approval/callback',status:'启用'}],
  instances:[{id:'PO-20261013-001',title:'华东仓库包装设备采购',node:'财务复核',person:'赵敏',status:'审批中',hours:52},{id:'EX-20261012-028',title:'研发部十月差旅报销',node:'部门负责人',person:'吴芳',status:'审批中',hours:6},{id:'PO-20261011-009',title:'生产线备品备件采购',node:'采购会签 · 异常待处理',person:'吴芳、王强',status:'审批中',hours:32},{id:'CT-20261010-006',title:'年度运输服务合同',node:'结束',person:'—',status:'已完成',hours:0}],
  queue:[{id:'q1',event:'instance.approved',business:'PO-20261012-006',target:'ERP · 结果回调',status:'等待重试',attempts:3},{id:'q2',event:'instance.rejected',business:'EX-20261012-018',target:'费用系统 · 结果回调',status:'已停止',attempts:8},{id:'q3',event:'task.created',business:'PO-20261013-001',target:'ERP · 待办通知',status:'成功',attempts:1}],
  hooks:[{name:'预算占用校验',trigger:'发起前',app:'ERP 采购系统',timeout:'5 秒',policy:'阻止发起并提示原因'},{name:'库存可用量校验',trigger:'节点同意前',app:'ERP 采购系统',timeout:'5 秒',policy:'阻止同意并提示原因'},{name:'项目经理取人',trigger:'审批人解析',app:'ERP 采购系统',timeout:'5 秒',policy:'转流程管理员处理'}],
  fonts:['演示中文字体（已授权）'],settings:{smtp:'smtp.example.com',digest:'08:30',notifications:[true,true,true,true],storage:'本地文件',storagePath:'./data/files',fileLimit:20,tokenExpiry:30},
  logs:[{time:'10-13 14:26:18',user:'管理员',action:'发布流程版本 v3',target:'采购申请'},{time:'10-13 11:08:32',user:'管理员',action:'修改审批矩阵',target:'采购授权矩阵'},{time:'10-13 09:42:05',user:'管理员',action:'更新业务绑定',target:'ERP 采购系统'},{time:'10-12 16:30:20',user:'管理员',action:'配置回调地址',target:'费用报销系统'}]};
/** 当前内存状态。 */
let adminState=structuredClone(adminDefaults);
try { const saved=JSON.parse(localStorage.getItem('approval-prototype-admin')||'null');if(saved)adminState={...adminState,...saved}; } catch { /* 本地演示缓存不可用时使用默认数据。 */ }
// 清理旧原型缓存中的未设计状态，异常以节点提示呈现，审批仍保持运行。
for(const instance of adminState.instances){if(instance.status==='已暂停'){instance.status='审批中';instance.node+=' · 异常待处理';}}
/** 功能入口名称。 */
const adminNavigation=[['overview','管理工作台'],['flows','流程与模板'],['org','组织与规则'],['apps','应用接入'],['runtime','运行实例'],['outbound','回调与钩子'],['reports','统计报表'],['system','系统管理']];
/** 功能入口默认页签。 */
const adminDefaultTabs={flows:'list',org:'people',apps:'list',outbound:'queue',reports:'flow',system:'storage'};
/**
 * 将状态保存到本机。
 * @returns {void} 无返回值。
 */
function adminPersist(){try{localStorage.setItem('approval-prototype-admin',JSON.stringify(adminState));}catch{/* 浏览器禁用存储时保持本次会话可操作。 */}}
/**
 * 渲染功能壳和当前页面。
 * @returns {void} 无返回值。
 */
function adminRender(){UI.shell('admin','管理控制台','流程、组织与接入应用的统一管理',`<div class="admin-nav" aria-label="管理功能">${adminNavigation.map(([id,n])=>`<button data-go="${id}" class="${adminState.page===id?'active':''}" aria-current="${adminState.page===id?'page':'false'}">${n}</button>`).join('')}</div><div class="admin-content">${adminPage(adminState.page)}</div>`,`<span class="pill">本地演示</span>`);}
/**
 * 切换功能和页签。
 * @param {string} page 功能编号。
 * @param {string} tab 页签编号。
 * @returns {void} 无返回值。
 */
function adminNavigate(page,tab=''){adminState.page=page;adminState.tab=tab||adminDefaultTabs[page]||'';adminState.search='';adminState.status='全部状态';location.hash=`${page}/${adminState.tab}`;adminRender();}
/**
 * 记录管理动作，更新页面并反馈成功。
 * @param {string} action 操作说明。
 * @param {string} target 操作对象。
 * @returns {void} 无返回值。
 */
function adminSave(action,target){adminState.logs.unshift({time:'10-13 14:35:00',user:'管理员',action,target});adminPersist();adminRender();UI.toast(`${action}成功，已保存在本地演示中`);}
/**
 * 构造有标签的输入控件。
 * @param {string} label 标签。
 * @param {string} name 字段名。
 * @param {string} value 初始值。
 * @param {string} type 控件类型。
 * @returns {string} 字段标记。
 */
function adminField(label,name,value='',type='text'){return `<label class="field"><span>${label}</span><input class="inp" name="${name}" type="${type}" value="${UI.escape(String(value))}"></label>`;}
/**
 * 构造选择控件。
 * @param {string} label 标签。
 * @param {string} name 字段名。
 * @param {Array} values 可选值。
 * @param {string} selected 当前选中。
 * @returns {string} 字段标记。
 */
function adminSelect(label,name,values,selected=''){return `<label class="field"><span>${label}</span><select class="sel" name="${name}">${values.map(v=>`<option ${v===selected?'selected':''}>${UI.escape(v)}</option>`).join('')}</select></label>`;}
/**
 * 读取对话框中的字段值。
 * @param {HTMLElement} dialog 对话框。
 * @param {string} name 字段名。
 * @returns {string} 去除空格的值。
 */
function adminValue(dialog,name){return dialog.querySelector(`[name="${name}"]`)?.value.trim()||'';}
/**
 * 检查必填字段，并在缺失时保留对话框。
 * @param {HTMLElement} dialog 对话框。
 * @param {Array} names 字段名。
 * @returns {boolean} 是否完整。
 */
function adminRequired(dialog,names){for(const n of names){if(!adminValue(dialog,n)){UI.toast('请填写所有必填项');dialog.querySelector(`[name="${n}"]`)?.focus();return false;}}return true;}
/**
 * 下载本地演示文件。
 * @param {string} name 文件名称。
 * @param {string} content 文件内容。
 * @param {string} type 文件类型。
 * @returns {void} 无返回值。
 */
function adminDownload(name,content,type='text/plain'){const a=document.createElement('a');a.href=URL.createObjectURL(new Blob(['\ufeff',content],{type}));a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);UI.toast('演示文件已下载');}
/**
 * 打开普通配置对话框并保存对象。
 * @param {string} title 标题。
 * @param {string} html 字段标记。
 * @param {Function} save 保存逻辑。
 * @returns {void} 无返回值。
 */
function adminDialog(title,html,save){UI.modal(title,`<div class="admin-content">${html}</div>`,save);}
/**
 * 分派所有管理操作，使用真实本地状态驱动演示。
 * @param {string} action 操作类型。
 * @param {HTMLElement} button 操作按钮。
 * @returns {void} 无返回值。
 */
function adminAction(action,button){const id=button.dataset.id;switch(action){
case 'refresh': adminRender();UI.toast('演示状态已刷新');break;
case 'flow-new':case 'copy-template': adminDialog(action==='flow-new'?'新建流程':'从模板创建流程',adminField('流程名称 *','name',button.dataset.name||'')+adminField('流程编码 *','code')+adminSelect('设计方式','mode',['钉钉式','自由画布']),d=>{if(!adminRequired(d,['name','code']))return false;const code=adminValue(d,'code');if(!/^[A-Z][A-Z0-9_]*$/.test(code)){UI.toast('流程编码须为大写字母、数字或下划线，以字母开头');return false;}if(adminState.flows.some(f=>f.code===code)){UI.toast('流程编码已存在');return false;}adminState.flows.unshift({name:adminValue(d,'name'),code,mode:adminValue(d,'mode'),status:'草稿',app:'待授权',version:'v1'});adminState.tab='list';adminSave('创建流程',code);});break;
case 'flow-toggle':{const f=adminState.flows.find(f=>f.code===id);adminDialog(`${f.status==='已停用'?'启用':'停用'}流程`, `<p>流程：${UI.escape(f.name)}</p><p class="hint">停用后不再接受新的审批；在途审批继续使用原有版本。</p>`+adminField('操作原因 *','reason'),d=>{if(!adminRequired(d,['reason']))return false;f.status=f.status==='已停用'?'已发布':'已停用';adminSave(f.status,`${f.name}：${adminValue(d,'reason')}`);});break;}
case 'versions':{const f=adminState.flows.find(f=>f.code===id);UI.modal('流程版本 · '+f.name,`<div class="admin-content">${adminTable(['版本','发布时间','发布人','说明'],[[f.version,'2026-10-13 11:26','管理员','调整金额条件与财务复核'],['v2','2026-10-08 09:10','管理员','增加预算校验钩子'],['v1','2026-10-01 16:30','管理员','首次发布']])}<p class="hint">历史版本只读。可复制当前版本到新草稿后修改。</p></div>`,()=>{f.version+=' 草稿';f.status='草稿';adminSave('复制版本为新草稿',f.name);},'复制为新草稿');break;}
case 'flow-import': adminDialog('导入流程定义','<p class="hint">只接受本系统导出的流程 JSON / BPMN 文件。以下示例演示导入校验。</p><textarea class="inp mono" name="json" rows="6">{"name":"设备维修审批","code":"EQUIPMENT_REPAIR","schemaVersion":1}</textarea>',d=>{try{const v=JSON.parse(adminValue(d,'json'));if(!v.name||!v.code||v.schemaVersion!==1)throw Error();if(adminState.flows.some(f=>f.code===v.code)){UI.toast('流程编码已存在');return false;}adminState.flows.unshift({name:String(v.name),code:String(v.code),mode:'钉钉式',status:'草稿',app:'待授权',version:'v1'});adminSave('导入流程',String(v.name));}catch{UI.toast('格式不正确，需包含 name、code 和 schemaVersion: 1');return false;}});break;
case 'person-new':case 'person-edit':{const p=adminState.people.find(p=>p.code===id)||{name:'',code:'',dept:'采购部',job:'',level:'P4',manager:'吴芳',status:'已开通'};adminDialog(id?'编辑人员':'新增人员',`<div class="form-grid">${adminField('姓名 *','name',p.name)}${adminField('工号 *','code',p.code)}${adminField('所属部门（可用顿号分隔）','dept',p.dept)}${adminField('岗位','job',p.job)}${adminField('职级','level',p.level)}${adminSelect('直属上级','manager',['—',...adminState.people.filter(x=>x.code!==id).map(x=>x.name)],p.manager)}${adminField('手机','phone','13800000000')}${adminField('邮箱','email','user@example.com')}${adminSelect('在职状态','status',['已开通','未开通','已离职'],p.status)}</div>`,d=>{if(!adminRequired(d,['name','code','dept']))return false;const code=adminValue(d,'code');if(adminState.people.some(x=>x.code===code&&x!==p)){UI.toast('工号已存在');return false;}for(const n of ['name','code','dept','job','level','manager','status'])p[n]=adminValue(d,n);if(!id)adminState.people.push(p);adminSave('保存人员',p.name);});break;}
case 'department':adminDialog('部门信息',adminSelect('选择部门','dept',['采购部','财务部','研发部','行政部'])+adminSelect('部门负责人','owner',adminState.people.map(p=>p.name),'吴芳')+adminSelect('分管领导','leader',['周总','赵敏'],'周总'),d=>{adminState.departments??={};adminState.departments[adminValue(d,'dept')]={owner:adminValue(d,'owner'),leader:adminValue(d,'leader')};adminSave('更新部门负责人',adminValue(d,'dept'));});break;
case 'account':{const p=adminState.people.find(p=>p.code===id);adminDialog('账号管理 · '+p.name,`<p class="hint">工号：${p.code} · ${p.status}</p>`+adminSelect('操作','operation',['作废全部登录令牌','要求下次登录修改密码','禁用账号','开通账号'])+adminField('操作原因 *','reason'),d=>{if(!adminRequired(d,['reason']))return false;const op=adminValue(d,'operation');if(op==='禁用账号')p.status='已禁用';if(op==='开通账号')p.status='已开通';adminSave(op,p.name+'：'+adminValue(d,'reason'));});break;}
case 'org-import':UI.modal('组织导入 · 校验预览',`<div class="admin-content"><div class="admin-callout">演示导入 6 行数据，4 行通过、2 行需要修正。修正后才可导入。</div>${adminTable(['行号','工号','问题'],[['3','EMP002','工号与已有人员重复'],['6','EMP009','直属上级 EMP099 不存在']])}<label class="checkrow"><input type="checkbox" id="skip-invalid">已修正问题行，仅导入校验通过的 4 行</label></div>`,d=>{if(!d.querySelector('#skip-invalid').checked){UI.toast('请先确认问题行已修正');return false;}if(!adminState.people.some(p=>p.code==='IMP001'))for(let i=1;i<=4;i++)adminState.people.push({name:['周磊','刘静','孙悦','吴强'][i-1],code:'IMP00'+i,dept:'研发部',job:'工程师',level:'P4',manager:'周总',status:'未开通'});adminSave('导入组织人员','4 条有效记录');},'确认导入');break;
case 'binding':{const b=adminState.bindings[Number(id)]||{type:'人员',external:'',person:'吴芳 / EMP002',mode:'手工绑定'};adminDialog('业务对象绑定',adminSelect('绑定类型','type',['人员','部门'],b.type)+adminField('业务系统 ID *','external',b.external)+adminField('审批服务对象 *','person',b.person),d=>{if(!adminRequired(d,['external','person']))return false;Object.assign(b,{type:adminValue(d,'type'),external:adminValue(d,'external'),person:adminValue(d,'person'),mode:'手工绑定'});if(id===undefined)adminState.bindings.push(b);adminSave('保存绑定',b.external);});break;}
case 'auto-bind':UI.modal('自动匹配预览',`<div class="admin-content">${adminTable(['业务用户','工号','匹配对象'],[['erp-u-1003','EMP005','王强'],['erp-u-1004','EMP004','钱丽']])}<p class="hint">按工号精确匹配，不覆盖已有绑定。</p></div>`,()=>{for(const [e,p]of[['erp-u-1003','王强 / EMP005'],['erp-u-1004','钱丽 / EMP004']])if(!adminState.bindings.some(b=>b.external===e))adminState.bindings.push({type:'人员',external:e,person:p,mode:'工号匹配'});adminSave('自动绑定','2 名人员');},'确认绑定');break;
case 'role':case 'role-new':{const r=adminState.roles[Number(id)]||{name:'',members:'',scope:'本部门及下级',rights:''};adminDialog('角色权限',adminField('角色名称 *','name',r.name)+adminField('成员','members',r.members)+adminSelect('管理数据范围','scope',['全部数据','授权流程','本部门及下级','仅本人'],r.scope)+['系统配置','组织管理','流程管理','运行干预','统计报表','审批记录'].map(n=>`<label class="checkrow"><input type="checkbox" name="right" value="${n}" ${r.rights.includes(n)?'checked':''}>${n}</label>`).join(''),d=>{if(!adminRequired(d,['name']))return false;r.name=adminValue(d,'name');r.members=adminValue(d,'members');r.scope=adminValue(d,'scope');r.rights=[...d.querySelectorAll('[name="right"]:checked')].map(c=>c.value).join('、');if(id===undefined)adminState.roles.push(r);adminSave('更新角色权限',r.name);});break;}
case 'matrix':case 'matrix-new':{const r=adminState.matrix[Number(id)]||{dept:'采购部',amount:'',category:'全部',person:'吴芳'};adminDialog('采购授权矩阵规则',adminSelect('部门','dept',['全部部门','采购部','财务部'],r.dept)+adminField('金额区间 *','amount',r.amount)+adminField('物料类别','category',r.category)+adminField('审批人 *','person',r.person),d=>{if(!adminRequired(d,['amount','person']))return false;Object.assign(r,{dept:adminValue(d,'dept'),amount:adminValue(d,'amount'),category:adminValue(d,'category'),person:adminValue(d,'person')});if(id===undefined)adminState.matrix.push(r);adminSave('更新审批矩阵',r.dept+' / '+r.amount);});break;}
case 'calendar-hours':adminDialog('工作时间',adminField('开始时间','start',adminState.settings.workStart||'09:00','time')+adminField('结束时间','end',adminState.settings.workEnd||'18:00','time'),d=>{if(adminValue(d,'start')>=adminValue(d,'end')){UI.toast('结束时间必须晚于开始时间');return false;}adminState.settings.workStart=adminValue(d,'start');adminState.settings.workEnd=adminValue(d,'end');adminSave('保存工作时间','标准工作日历');});break;
case 'handover':adminDialog('离职交接 · 郑涛',`<div class="admin-callout">2 项在途待办将转交给指定人员，原审批记录保留。</div>`+adminSelect('接收人','person',['吴芳','王强','赵敏'])+adminField('交接原因 *','reason'),d=>{if(!adminRequired(d,['reason']))return false;adminState.handover=adminValue(d,'person');adminSave('办理离职交接','郑涛转交给'+adminState.handover);});break;
case 'app-new':adminAppWizard(1,{});break;
case 'app-edit':{const a=adminState.apps[Number(id)];adminDialog('接入应用 · '+a.name,`<div class="form-grid">${adminField('应用名称 *','name',a.name)}${adminSelect('状态','status',['启用','停用'],a.status)}${adminField('授权流程 *','flows',a.flows)}${adminField('回调地址','callback',a.callback)}${adminField('允许嵌入的来源','origins',a.origins||'https://erp.example.com')}${adminSelect('待办范围','scope',['仅本应用','全部应用（统一待办）'],a.scope||'仅本应用')}</div><p class="hint">回调请求带 X-Approval-Signature；业务系统按 eventId 幂等去重。</p>`,d=>{if(!adminRequired(d,['name','flows']))return false;for(const n of ['name','status','flows','callback','origins','scope'])a[n]=adminValue(d,n);adminSave('保存接入应用',a.name);});break;}
case 'app-key':UI.modal('密钥管理 · 演示',`<div class="admin-content"><p class="hint">API Key 只存 SHA-256。实际生成时仅展示一次明文；轮换后旧 Key 失效。</p><div class="admin-code">API Key：DEMO_ONLY_NOT_A_REAL_KEY<br>签名密钥：DEMO_ONLY_NOT_A_REAL_SECRET</div><label class="checkrow"><input type="checkbox" id="key-confirm">我已了解轮换会影响业务系统调用</label></div>`,d=>{if(!d.querySelector('#key-confirm').checked){UI.toast('请确认已了解轮换影响');return false;}adminState.apps[Number(id)].keyRotated=true;adminSave('轮换演示密钥',adminState.apps[Number(id)].name);},'轮换密钥');break;
case 'app-test':UI.modal('测试回调结果',`<div class="admin-result">HTTP 200 · 126 ms · 验签通过（本地模拟）</div><pre class="admin-code">${UI.escape(JSON.stringify({eventId:'demo-test-event',event:'instance.approved',businessKey:'DEMO-001',status:'approved'},null,2))}</pre><p class="hint">本次测试未访问真实网络，也不会改变业务单据。</p>`,()=>{adminSave('测试回调',adminState.apps[Number(id)].name);},'记录测试结果');break;
case 'api-run':adminRunApi();break;
case 'sdk-download':adminDownload('approval-integration-demo.cs','// 审批服务接入示意，具体签名以发布后的 SDK 为准。\r\nvar count = await approval.GetTaskCountAsync(currentUser.Code);\r\nvar url = await approval.CreateOpenUrlAsync(taskId, currentUser.Code);');break;
case 'intervene':{const i=adminState.instances.find(i=>i.id===id);adminDialog('管理干预 · '+i.id,`<p>${UI.escape(i.title)} · ${i.node}</p>`+adminSelect('干预操作','operation',['更换审批人','跳转节点','终止','重发提醒','重发回调'])+adminSelect('目标节点 / 人员','target',['部门负责人 · 吴芳','财务复核 · 赵敏','总经理 · 周总'])+adminField('操作原因（必填） *','reason')+'<p class="hint">干预将追加到审批记录中，不能删除或覆盖历史记录。</p>',d=>{if(!adminRequired(d,['reason']))return false;const op=adminValue(d,'operation');if(op==='终止')i.status='已终止';if(op==='更换审批人')i.person=adminValue(d,'target').split(' · ')[1];if(op==='跳转节点'){i.node=adminValue(d,'target').split(' · ')[0];i.person=adminValue(d,'target').split(' · ')[1];i.status='审批中';}adminSave(op+'审批',i.id+'：'+adminValue(d,'reason'));});break;}
case 'retry':case 'retry-all':{const rows=adminState.queue.filter(q=>action==='retry-all'?q.status!=='成功':q.id===id);UI.modal('确认重试',`<p>将重新投递 ${rows.length} 项事件，保持 eventId 不变。</p><p class="hint">本地演示将模拟投递成功，不发送实际请求。</p>`,()=>{for(const q of rows){q.status='成功';q.attempts++;}adminSave('重试投递',rows.length+' 项事件');},'重试');break;}
case 'queue-detail':{const q=adminState.queue.find(q=>q.id===id);UI.modal('投递请求记录',`<div class="admin-content">${adminTable(['次数','时间','响应','耗时'],[[q.attempts,'14:32:08',q.status==='成功'?'HTTP 200':'HTTP 503','126 ms'],[Math.max(1,q.attempts-1),'14:27:08','HTTP 503','5012 ms']])}<pre class="admin-code">${UI.escape(JSON.stringify({eventId:q.id,event:q.event,businessKey:q.business,status:'approved',headers:{'X-Request-Id':'demo-request-id','X-Approval-Signature':'sha256=DEMO_SIGNATURE_REDACTED'}},null,2))}</pre></div>`,()=>{},'关闭');break;}
case 'hook':case 'hook-new':{const h=adminState.hooks[Number(id)]||{name:'',trigger:'发起前',app:'ERP 采购系统',timeout:'5 秒',policy:'阻止发起并提示原因'};adminDialog('配置钩子',adminField('名称 *','name',h.name)+adminSelect('触发时机','trigger',['发起前','节点同意前','审批人解析','自动节点动作'],h.trigger)+adminField('接口地址 *','url',h.url||'https://erp.example.com/approval/validate')+adminSelect('超时','timeout',['5 秒','10 秒','30 秒'],h.timeout)+adminField('失败策略','policy',h.policy),d=>{if(!adminRequired(d,['name','url']))return false;for(const n of ['name','trigger','url','timeout','policy'])h[n]=adminValue(d,n);if(id===undefined)adminState.hooks.push(h);adminSave('保存钩子',h.name);});break;}
case 'hook-test':UI.modal('钩子沙箱结果','<div class="admin-result">校验通过 · 预算余额 ¥ 320,000.00<br>本地模拟响应 · 不调用实际业务系统</div><pre class="admin-code">{ "allowed": true, "message": "预算充足" }</pre>',()=>{},'关闭');break;
case 'report-apply':adminState.reportPeriod=document.getElementById('report-period').value;adminState.reportFlow=document.getElementById('report-flow').value;adminState.reportDept=document.getElementById('report-dept').value;adminState.reportApp=document.getElementById('report-app').value;adminPersist();adminRender();break;
case 'report-export':{const f=adminState.reportPeriod==='本周'?1:4;let rows=['流程,发起量,通过率,拒绝率,撤回率,平均耗时,P90耗时',...adminReportRows().map(r=>[r[0],r[1]*f,...r.slice(2)].join(','))];if(adminState.tab==='node')rows=['节点,平均停留,P90耗时,超时率,处理量','财务复核,18.6 h,36.2 h,12.4%,62','采购会签,12.4 h,24.8 h,6.8%,48','分管领导,6.2 h,14.6 h,2.1%,34'];if(adminState.tab==='people')rows=['人员,部门,当前积压,平均处理时长,超时次数,处理量','赵敏,财务部,12,8.4 h,3,46','吴芳,采购部,8,4.2 h,1,38','周总,总经办,5,6.8 h,0,21','钱丽,财务部,4,3.6 h,0,42'];adminDownload('审批效率报表.csv',rows.join('\r\n'),'text/csv;charset=utf-8');break;}
case 'export':{const rows=adminState.page==='runtime'?adminState.instances.map(i=>[i.id,i.title,i.status,i.node,i.person]):adminState.logs.map(l=>[l.time,l.user,l.action,l.target]);adminDownload('审批管理导出.csv',rows.map(r=>r.map(v=>'"'+String(v).replaceAll('"','""')+'"').join(',')).join('\r\n'),'text/csv;charset=utf-8');break;}
case 'notifications-save':{const f=document.getElementById('notification-form');adminState.settings.smtp=adminValue(f,'smtp');adminState.settings.digest=adminValue(f,'digest');adminState.settings.notifications=[0,1,2,3].map(i=>f.querySelector(`[name="n${i}"]`).checked);adminSave('保存消息设置','邮件与站内提醒');break;}
case 'mail-test':adminDialog('测试邮件',adminField('收件人 *','email','admin@example.com','email'),d=>{if(!adminRequired(d,['email']))return false;adminState.lastMail=adminValue(d,'email');adminSave('模拟发送测试邮件',adminState.lastMail);});break;
case 'storage-save':{const limit=Number(document.getElementById('file-limit').value);if(limit<1||!Number.isFinite(limit)){UI.toast('附件上限必须大于 0');break;}adminState.settings.storage=document.getElementById('storage-type').value;adminState.settings.storagePath=document.getElementById('storage-path').value;adminState.settings.fileLimit=limit;adminSave('保存附件存储配置',adminState.settings.storage);break;}
case 'storage-test':UI.modal('存储连接检测','<div class="admin-result">演示检测通过<br>目录可写 · 可用空间 128 GB · 附件权限检查已启用</div>',()=>{},'关闭');break;
case 'migration':UI.modal('附件迁移预览','<div class="admin-content"><p>本地文件迁移至 MinIO / S3</p><div class="admin-callout">826 个文件 · 1.24 GB。迁移后校验 SHA-256，确认无误才切换存储位置。</div><p class="hint">此操作仅演示任务完成状态，不移动真实文件。</p></div>',()=>{adminState.migration='已完成';adminSave('模拟附件迁移','826 个文件校验通过');},'模拟迁移');break;
case 'font-upload':adminDialog('上传打印字体','<label class="field"><span>字体文件（TTF / OTF）</span><input class="inp" type="file" name="font" accept=".ttf,.otf"></label><label class="checkrow"><input type="checkbox" name="license">确认已获得字体使用授权</label>',d=>{const f=d.querySelector('[name="font"]').files[0];if(!f||!d.querySelector('[name="license"]').checked){UI.toast('请选择字体并确认授权');return false;}adminState.fonts.push(f.name);adminSave('添加字体',f.name);});break;
case 'font-default':{const [f]=adminState.fonts.splice(Number(id),1);adminState.fonts.unshift(f);adminSave('设置默认打印字体',f);break;}
case 'security-save':{const v=Number(document.getElementById('token-expiry').value);if(v<5||v>120){UI.toast('有效期须为 5–120 分钟');break;}adminState.settings.tokenExpiry=v;adminSave('保存登录设置','访问令牌有效期 '+v+' 分钟');break;}
}}
/**
 * 演示应用接入向导，在各步骤之间保留表单值。
 * @param {number} step 当前步骤。
 * @param {object} draft 应用草稿。
 * @returns {void} 无返回值。
 */
function adminAppWizard(step,draft){const steps=['基本信息','流程与回调','调用示例'];let body=`<div class="admin-steps">${steps.map((n,i)=>`<span class="${step===i+1?'active':''}"><b>${i+1}</b>${n}</span>`).join('')}</div>`;if(step===1)body+=adminField('应用名称 *','name',draft.name||'')+adminField('应用编码 *','code',draft.code||'')+adminField('允许嵌入的来源','origins','https://example.com');if(step===2)body+=adminSelect('授权流程','flows',['采购申请','费用报销','采购申请、费用报销'])+adminField('结果回调地址','callback','https://example.com/approval/callback')+'<p class="hint">密钥仅在创建完成时展示一次。测试和正式环境请分别创建应用。</p>';if(step===3)body+=`<div class="admin-callout">配置已就绪。创建后可在应用列表继续调整授权与回调。</div><pre class="admin-code">应用：${UI.escape(draft.name)} / ${UI.escape(draft.code)}
API Key：DEMO_ONLY_NOT_A_REAL_KEY
签名密钥：DEMO_ONLY_NOT_A_REAL_SECRET

// 调用代码为示意。
var approval = new ApprovalClient(options);
var count = await approval.GetTaskCountAsync(userCode);</pre>`;UI.modal('新建接入应用',`<div class="admin-content">${body}</div>`,d=>{if(step===1){if(!adminRequired(d,['name','code']))return false;if(adminState.apps.some(a=>a.code===adminValue(d,'code'))){UI.toast('应用编码已存在');return false;}Object.assign(draft,{name:adminValue(d,'name'),code:adminValue(d,'code'),origins:adminValue(d,'origins')});}if(step===2)Object.assign(draft,{flows:adminValue(d,'flows'),callback:adminValue(d,'callback'),status:'启用'});if(step<3){UI.closeModal();adminAppWizard(step+1,draft);return false;}adminState.apps.push(draft);adminSave('创建接入应用',draft.name);},step===3?'完成创建':'下一步');}
/**
 * 模拟接口响应，并显示有效的 JSON。
 * @returns {void} 无返回值。
 */
function adminRunApi(){let data;try{data=JSON.parse(document.getElementById('api-body').value);}catch{document.getElementById('api-status').textContent='400 · INVALID_JSON';document.getElementById('api-result').textContent=JSON.stringify({code:'INVALID_JSON',message:'请求体不是合法的 JSON。',traceId:'demo-trace-001'},null,2);return;}const scenario=document.getElementById('api-scenario').value;const method=document.getElementById('api-method').value;let status=200,result;if(scenario!=='正常响应'){const map={'参数错误':[400,'VALIDATION_FAILED','审批数据不完整。'],'未授权':[401,'UNAUTHORIZED','API Key 无效。'],'状态冲突':[409,'CONFLICT','审批任务已经被处理。']};const item=map[scenario];status=item[0];result={code:item[1],message:item[2],traceId:'demo-trace-001'};}else if(method.includes('tasks/count'))result={pending:12,urgent:3,overdue:2};else if(method.includes('embed-tokens'))result={openUrl:'center.html',expiresInSeconds:300,oneTime:true};else if(method.includes('instances'))result={id:'demo-001',status:'running',currentNode:'财务复核'};else result={businessKey:data.businessKey,nodes:['部门负责人','财务复核','总经理'],assignees:['吴芳','赵敏','周总'],estimatedWorkDays:3};document.getElementById('api-status').textContent=status+' · 本地模拟 · 126 ms';document.getElementById('api-result').textContent=JSON.stringify(result,null,2);}
/**
 * 统一处理页面点击事件。
 * @param {MouseEvent} event 浏览器事件。
 * @returns {void} 无返回值。
 */
document.addEventListener('click',event=>{const b=event.target.closest('button');if(!b)return;if(b.dataset.go){adminNavigate(b.dataset.go,b.dataset.sub);return;}if(b.dataset.tab){adminState.tab=b.dataset.tab;adminState.search='';adminState.status='全部状态';location.hash=`${adminState.page}/${adminState.tab}`;adminRender();return;}if(b.dataset.dept){adminState.dept=b.dataset.dept;adminRender();return;}if(b.dataset.day){const day=b.dataset.day;adminDialog(`2026 年 10 月 ${day} 日`,adminSelect('日期类型','type',['工作日','休息日','节假日','调休上班'])+adminField('备注','note',adminState.holidays[day]||''),d=>{adminState.holidays[day]=adminValue(d,'note')||adminValue(d,'type');adminSave('修改工作日历','10 月 '+day+' 日');});return;}if(b.dataset.action)adminAction(b.dataset.action,b);});
/**
 * 处理列表筛选，并保留输入焦点。
 * @param {Event} event 浏览器事件。
 * @returns {void} 无返回值。
 */
document.addEventListener('input',event=>{if(!event.target.matches('[data-filter]'))return;const pos=event.target.selectionStart;adminState.search=event.target.value;adminRender();const input=document.querySelector('[data-filter]');input?.focus();input?.setSelectionRange(pos,pos);});
/**
 * 处理状态过滤。
 * @param {Event} event 浏览器事件。
 * @returns {void} 无返回值。
 */
document.addEventListener('change',event=>{if(event.target.matches('[data-status]')){adminState.status=event.target.value;adminRender();}});
/**
 * 根据地址恢复可分享的功能位置。
 * @returns {void} 无返回值。
 */
function adminReadHash(){const [page,tab]=location.hash.slice(1).split('/');if(adminNavigation.some(([id])=>id===page)){adminState.page=page;adminState.tab=tab||adminDefaultTabs[page]||'';}else{adminState.page='overview';adminState.tab='';}adminRender();}
window.addEventListener('hashchange',adminReadHash);
adminReadHash();

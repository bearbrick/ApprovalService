/**
 * 流程设计交互原型，仅使用本地虚构数据，不调用审批接口。
 */
const D = {
  /** 当前页签。 */
  tab: 'flow',
  /** 当前节点。 */
  selected: 'department',
  /** 本地演示草稿。 */
  state: {
    mode:'canvas', version:3, threshold:10000, settings:{},
    nodes:[
      {id:'start',name:'业务系统提交',type:'start',source:'发起人本人',people:'由 ERP 业务系统发起',method:'依次审批',x:40,y:230},
      {id:'department',name:'部门负责人审批',type:'approval',source:'部门负责人',people:'发起人所在部门 · 第 1 级',method:'依次审批',x:240,y:230},
      {id:'management',name:'总经理 / 财务总监',type:'approval',source:'角色',people:'总经理、财务总监',method:'会签',x:465,y:100},
      {id:'finance',name:'采购执行',type:'handle',source:'角色',people:'采购专员',method:'依次审批',x:680,y:230},
      {id:'cc',name:'抄送采购部',type:'cc',source:'指定成员',people:'陈晓、王强',method:'依次审批',x:680,y:430}
    ],
    edges:[['start','department',''],['department','management','金额 ≥ 10,000'],['department','finance','其他情况'],['management','finance',''],['finance','cc','']],
    fields:[{key:'applicant',name:'申请人',type:'人员',required:true,summary:true},{key:'department',name:'申请部门',type:'部门',required:true,summary:true},{key:'amount',name:'采购金额',type:'金额',required:true,summary:true},{key:'purpose',name:'采购用途',type:'文本',required:true,summary:true},{key:'date',name:'申请日期',type:'日期',required:true},{key:'items',name:'采购明细',type:'明细表',required:true},{key:'attachments',name:'相关附件',type:'附件'},{key:'approvedAmount',name:'核定金额',type:'金额',source:'审批填写'}],
    history:[{version:3,date:'2026-10-12 16:20',note:'调整大额采购阈值，完善财务审批规则。'},{version:2,date:'2026-10-09 11:35',note:'新增采购明细与附件展示。'},{version:1,date:'2026-10-01 09:00',note:'首次发布采购申请流程。'}]
  },
  /** 审批人来源清单。 */
  sources:['指定成员','角色','岗位、职级','直属上级','连续多级上级','部门负责人','业务数据里的部门','分管领导','业务数据里的人员','审批矩阵','业务系统接口','上一步指定','发起人自选','发起人本人'],
  /**
   * 读取本地草稿并开始呈现。
   * @returns {void}
   */
  init() {
    this.base=structuredClone(this.state);this.drafts={};this.loadFlow();
    window.addEventListener('hashchange',()=>{this.drafts[this.state.flowKey]=this.state;this.loadFlow();});
  },
  /**
   * 按路由载入独立模板或该流程的本地草稿。
   * @returns {void}
   */
  loadFlow() {
    const route=location.hash.slice(1),key=['purchase','expense','contract','quality'].includes(route)?route:'purchase';
    this.state=this.drafts[key]||DesignerTemplates.create(key,this.base);
    if(!this.drafts[key]){try{const saved=JSON.parse(localStorage.getItem('approval-prototype-designer-'+key));if(DesignerGraph.accepts(saved)&&saved.flowKey===key)this.state=saved;else if(saved)UI.toast('本地草稿结构异常，原稿保留，已载入示例。');}catch{UI.toast('本地草稿无法读取，已载入示例。');}}
    if(this.state.schemaVersion!==2){localStorage.setItem('approval-prototype-designer-backup-'+key+'-'+Date.now(),JSON.stringify(this.state));}
    this.state=DesignerGraph.migrate(this.state);this.selected=this.state.nodes.find(n=>n.type==='approval')?.id||this.state.nodes[0]?.id||'';this.tab='flow';this.zoom=.9;this.render();setTimeout(()=>this.revealNode(this.selected),0);
  },
  /**
   * 渲染设计器工作台。
   * @returns {void}
   */
  render() {
    if(this.tab!=='flow'&&this.focused){this.focused=false;document.body.classList.remove('canvas-focused');}
    const scroller=document.querySelector('#canvas-scroll'),scroll=scroller?{left:scroller.scrollLeft,top:scroller.scrollTop}:null;
    const tabs=[['flow','流程设计'],['fields','审批数据'],['layout','详情排版'],['rules','流程规则'],['print','打印设置'],['versions','版本管理']];
    const actions='<div class="designer-actions"><button class="btn" onclick="D.preview()">流程预览</button><button class="btn" onclick="D.test()">测试模式</button><button class="btn" onclick="D.save()">保存草稿</button><button class="btn primary" onclick="D.publish()">发布流程</button></div>';
    const content=`<div class="design-head"><span class="pill warn">草稿</span><span class="pill">基于 V${this.state.version}</span><div class="design-meta"><span class="mono">${UI.escape(this.state.code)}</span><span>${UI.escape(this.state.category)}</span><span>负责人：${UI.escape(this.state.owner)}</span></div><span style="flex:1"></span><button class="btn sm ghost" onclick="D.basic()">基本信息</button></div><div class="tabs design-tabs">${tabs.map(([key,label])=>`<button class="${this.tab===key?'active':''}" onclick="D.tab='${key}';D.render()">${label}</button>`).join('')}</div>${this.tab==='flow'?this.flow():DesignerConfig[this.tab]()}`;
    UI.shell('designer',this.state.name,'流程定义、审批数据与展示配置',content,actions);
    this.bindDrag();if(scroll)document.querySelector('#canvas-scroll')?.scrollTo(scroll.left,scroll.top);
  },
  /**
   * 获取当前选中的节点。
   * @returns {object} 节点配置。
   */
  node() { return this.state.nodes.find(n=>n.id===this.selected)||this.state.nodes[0]; },
  /**
   * 配置子流程字段映射或自动节点重试。
   * @returns {void}
   */
  extraNode() {
    const n=this.node();
    if(['parallel','parallelJoin','inclusive','inclusiveJoin','exclusive'].includes(n.type)){this.edge();return;}
    const mapping=n.mapping||Object.entries(n.inputMapping||{amount:'amount',department:'department',parentBusinessKey:'businessKey'}).map(([target,source])=>source+' → '+target).join('\n');
    UI.modal(n.type==='sub'?'子流程字段映射':'自动节点执行策略',n.type==='sub'?`<label class="field">目标流程编码<input class="inp" name="target" value="${UI.escape(n.subFlow||'CONTRACT_REVIEW')}"></label><label class="field">输入字段映射说明<textarea class="inp mono" rows="4" name="mapping">${UI.escape(mapping)}</textarea></label><p class="hint">输出映射：${UI.escape(JSON.stringify(n.outputMapping||{}))}</p>`:`<label class="field">业务动作标识<input class="inp" name="target" value="${UI.escape(n.action||'createPurchaseOrder')}"></label><label class="field">接口地址<input class="inp" name="endpoint" value="${UI.escape(n.target||'https://erp.example.invalid/approval/actions')}"></label><label class="field">失败重试策略<input class="inp" name="mapping" value="${UI.escape(n.retry||n.mapping||'1 分钟、5 分钟、30 分钟、2 小时')}"></label><div class="notice">原型不调用接口；重试耗尽后转交管理员。</div>`,dialog=>{const target=dialog.querySelector('[name=target]').value;if(n.type==='sub'){n.subFlow=target;n.mapping=dialog.querySelector('[name=mapping]').value;}else{n.action=target;n.target=dialog.querySelector('[name=endpoint]').value;n.retry=dialog.querySelector('[name=mapping]').value;}});

  },
  /**
   * 绘制选中节点的属性。
   * @returns {string} 面板内容。
   */
  inspector() {
    const n=this.node();if(!n)return '<div class="empty">请选择或添加一个节点</div>';
    const sourceHelp={'指定成员':'张明、王强','角色':'总经理、财务总监','岗位、职级':'部门经理 · M3 及以上','直属上级':'第 1 级上级','连续多级上级':'从第 1 级到第 3 级','部门负责人':'发起人所在部门 · 第 1 级','业务数据里的部门':'department','分管领导':'生产制造分管领导','业务数据里的人员':'projectOwner','审批矩阵':'采购金额审批矩阵','业务系统接口':'ERP · 项目负责人接口','上一步指定':'限定在采购部选择','发起人自选':'限定部门负责人角色','发起人本人':'业务系统传入的发起人'};
    const common=`<div class="inspector-title"><h3>节点设置</h3><span class="pill">${UI.escape(n.id)}</span></div><label class="field"><span>节点名称</span><input class="inp" value="${UI.escape(n.name)}" oninput="D.changeNode('name',this.value)"></label>`;
    const special=this.specialInspector(n,common);if(special!==null)return special;
    return common+`<label class="field"><span>审批人来源 <span class="muted">14 种来源</span></span><select class="sel" onchange="D.changeSource(this.value)">${this.sources.map(s=>`<option ${s===n.source?'selected':''}>${s}</option>`).join('')}</select></label><div class="source-detail"><label class="field" style="margin-bottom:0"><span>${n.source}配置</span><input class="inp" value="${UI.escape(n.people)}" placeholder="${sourceHelp[n.source]}" oninput="D.changeNode('people',this.value)"></label><p class="small-label" style="margin-bottom:0">${sourceHelp[n.source]}</p></div>${n.type==='cc'?`<label class="check-row"><input type="checkbox" ${n.readRequired?'checked':''} oninput="D.changeNode('readRequired',this.checked)">要求抄送人点击“已阅”</label>`:`<div class="field"><span>多人审批方式</span><div class="option-grid">${['依次审批','会签','或签','票签'].map(m=>`<label><input type="radio" name="method" ${n.method===m?'checked':''} oninput="D.changeNode('method','${m}')">${m}</label>`).join('')}</div><p class="small-label">${{依次审批:'按顺序依次处理，全部同意后通过。',会签:'所有人同意后通过，可设置拒绝后的处理方式。',或签:'任意一人同意即通过，其余任务自动取消。',票签:'赞成票达到设定比例后通过，支持权重。'}[n.method]}</p>${n.method==='票签'?`<label class="field"><span>通过比例（%）</span><input class="inp" type="number" min="1" max="100" value="${n.ratio??67}" oninput="D.changeNode('ratio',this.value)"></label><label class="field"><span>人员权重</span><input class="inp" value="${UI.escape(n.weights||'总经理: 2, 财务总监: 1')}" oninput="D.changeNode('weights',this.value)"></label>`:''}</div><div class="inspector-section"><h4>操作权限</h4><div class="option-grid">${['退回','转交','加签','减签','征询意见','要求补充','抄送','撤销同意','评论与@','暂存','批量同意'].map(a=>`<label><input type="checkbox" ${!(n.denied||[]).includes(a)?'checked':''} onchange="D.operation('${a}',this.checked)">${a}</label>`).join('')}</div></div>${this.policyFields(n)}<div class="inspector-section"><h4>意见与字段</h4><label class="check-row"><input type="checkbox" ${n.commentRequired?'checked':''} oninput="D.changeNode('commentRequired',this.checked)">审批意见必填</label><label class="check-row"><input type="checkbox" ${n.signature?'checked':''} oninput="D.changeNode('signature',this.checked)">要求手写 / 图片签名</label><button class="btn sm" onclick="D.permissions()">配置字段权限</button></div>`}<div class="inspector-section"><button class="btn sm danger" onclick="D.deleteNode()">删除此节点</button></div>`;
  },
  /**
   * 更新节点属性并重绘。
   * @param {string} key 属性名称。
   * @param {unknown} value 属性值。
   * @returns {void}
   */
  changeNode(key,value) {const n=this.node();n[key]=value;if(key==='method'){this.render();return;}const card=document.querySelector(`[data-node="${n.id}"]`);if(key==='name'&&card)card.querySelector('.node-body b').textContent=value;if(key==='people'&&card)card.querySelector('.node-body span').textContent=value;},
  /**
   * 切换审批人来源并清空原来源选择。
   * @param {string} value 审批人来源。
   * @returns {void}
   */
  changeSource(value) {this.node().source=value;this.node().people='';this.render();},
  /**
   * 更新节点允许的审批动作。
   * @param {string} name 操作名称。
   * @param {boolean} allowed 是否允许。
   * @returns {void}
   */
  operation(name,allowed) {const n=this.node();n.denied=(n.denied||[]).filter(a=>a!==name);if(!allowed)n.denied.push(name);},
  /**
   * 更新流程设置。
   * @param {string} key 属性名。
   * @param {unknown} value 属性值。
   * @returns {void}
   */
  setting(key,value) {this.state.settings[key]=value;if(['columns','uppercase','paper','printMode','watermark'].includes(key))this.render();},
  /**
   * 保存独立于生产系统的本地草稿。
   * @returns {void}
   */
  save() {this.syncInputs();this.state.saved=new Date().toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});localStorage.setItem('approval-prototype-designer-'+this.state.flowKey,JSON.stringify(this.state));this.render();UI.toast('草稿已保存到当前浏览器。');},
  /**
   * 校验草稿并发布新的本地演示版本。
   * @returns {void}
   */
  publish() {this.syncInputs();if(this.errors().length){this.validate();return;}UI.modal('发布新版本',`<div class="notice">基础配置检查通过，将发布为 V${this.state.version+1}。</div><label class="field">发布说明<textarea name="note" class="inp" rows="3" placeholder="说明本次流程调整"></textarea></label><p class="hint">在途审批仍使用原版本；此原型仅保存浏览器本地版本。</p>`,dialog=>{const note=dialog.querySelector('[name=note]').value.trim();if(!note){UI.toast('请填写发布说明。');return false;}this.state.version++;this.state.history.unshift({version:this.state.version,note,date:new Date().toLocaleString('zh-CN')});this.state.publishedNodes=this.state.nodes.length;this.state.publishedFields=this.state.fields.length;this.state.publishedMode=this.state.mode;this.tab='versions';this.save();UI.toast('本地演示版本 V'+this.state.version+' 已发布。');},'确认发布');},
  /**
   * 展示测试中的拒绝终态。
   * @returns {void}
   */
  testRejected() {UI.modal('测试结束 · 已拒绝','<div class="notice warn">审批被拒绝，流程结束。</div><p>模拟结果回调：业务系统收到“已拒绝”状态，可修改原单后重新提交。</p>',()=>{},'关闭测试');},
  /**
   * 设置节点的字段可见及审批填写权限。
   * @returns {void}
   */
  permissions() {const n=this.node();UI.modal('节点字段权限',`<table class="designer-table"><tr><th>字段</th><th>权限</th></tr>${this.state.fields.map((f,i)=>`<tr><td>${UI.escape(f.name)}</td><td><select class="sel" data-permission="${i}">${(f.source==='审批填写'?['只读','可填写','隐藏']:['只读','隐藏']).map(p=>`<option ${(n.permissions?.[f.key]||(n.writableFields?.includes(f.key)?'可填写':'只读'))===p?'selected':''}>${p}</option>`).join('')}</select></td></tr>`).join('')}</table><p class="hint">业务系统传入的数据不能在审批中心修改。</p>`,dialog=>{n.permissions={};dialog.querySelectorAll('[data-permission]').forEach(el=>n.permissions[this.state.fields[Number(el.dataset.permission)].key]=el.value);});},
  /**
   * 修改业务数据字段。
   * @param {number} index 字段序号。
   * @param {string} key 属性名称。
   * @param {unknown} value 新值。
   * @returns {void}
   */
  field(index,key,value) {this.state.fields[index][key]=value;},
  /**
   * 添加字段并定位到新增行。
   * @returns {void}
   */
  addField() {this.state.fields.push({key:'field'+(this.state.fields.length+1),name:'新字段',type:'文本'});this.render();},
  /**
   * 移除一个数据字段。
   * @param {number} index 字段序号。
   * @returns {void}
   */
  removeField(index) {this.state.fields.splice(index,1);this.render();},
  /**
   * 编辑字段详细来源和计算规则。
   * @param {number} index 字段序号。
   * @returns {void}
   */
  editField(index) {const f=this.state.fields[index];UI.modal('字段设置 · '+f.name,`<label class="field">数据来源<select name="source" class="sel"><option ${f.source!=='审批填写'?'selected':''}>业务系统传入</option><option ${f.source==='审批填写'?'selected':''}>审批填写</option></select></label><label class="field">说明 / 计算表达式 / 明细列<textarea name="note" class="inp" rows="3">${UI.escape(f.note||(f.type==='明细表'?'name: 文本, quantity: 数字, amount: 金额':f.type==='计算字段'?'SUM(items.amount)':''))}</textarea></label><p class="hint">计算字段只用于条件和展示，不回写业务单据。</p>`,dialog=>{f.source=dialog.querySelector('[name=source]').value;f.note=dialog.querySelector('[name=note]').value;this.render();});},
  /**
   * 从本地 JSON 示例生成字段。
   * @returns {void}
   */
  importSchema() {UI.modal('从 JSON 示例生成字段','<label class="field">业务数据示例<textarea name="json" class="inp mono" rows="7">{"projectName":"产线升级","budget":50000,"urgent":false}</textarea></label><p class="hint">追加不存在的字段，同名字段保留现有定义。</p>',dialog=>{let data;try{data=JSON.parse(dialog.querySelector('[name=json]').value);if(!data||Array.isArray(data)||typeof data!=='object')throw new Error();}catch{UI.toast('请输入 JSON 对象。');return false;}Object.entries(data).forEach(([key,v])=>{if(!this.state.fields.some(f=>f.key===key))this.state.fields.push({key,name:key,type:Array.isArray(v)?'明细表':typeof v==='number'?'数字':'文本'});});this.render();},'生成字段');},
  /**
   * 提供详情布局中的只读示例值。
   * @param {object} field 字段。
   * @returns {string} 虚构显示值。
   */
  sample(field) {return {applicant:'王强',department:'生产一部',amount:'¥ 28,600.00',purpose:this.state.flowKey==='expense'?'客户现场服务差旅':'产线设备维护与备件补充',date:'2026-10-13',items:this.state.flowKey==='expense'?'交通费 / 住宿费 / 市内交通':'高精度轴承 ×20 / 液压控制组件 ×5',attachments:this.state.flowKey==='expense'?'行程与票据.pdf':'业务单据附件.pdf',approvedAmount:'由审批人填写',paymentDate:'由出纳填写',contractName:'产线设备年度维护协议',supplier:'华强电子',parentBusinessKey:'PR20261013001',defect:'设备运行振动异常，抽检未通过',batch:'LOT-20261013-A',rework:'是',result:'由质量经理填写',project:'产线升级项目'}[field.key]||'示例数据';},
  /**
   * 展示当前详情排版和字段顺序。
   * @returns {void}
   */
  detailPreview() {UI.modal(this.state.name+' · 详情预览',`<div class="notice">审批人看到的只读业务快照</div><div class="layout-cells" style="grid-template-columns:repeat(${parseInt(this.state.settings.columns||'2')},1fr)">${this.state.fields.filter(f=>!f.hidden).map(f=>`<div class="layout-cell"><span>${UI.escape(f.name)}</span>${UI.escape(this.sample(f))}</div>`).join('')}</div>`,()=>{},'关闭预览');},
  /**
   * 展示可打印审批单。
   * @returns {void}
   */
  printPreview() {UI.modal('打印预览 · 示例审批单',`<div class="paper">${DesignerConfig.paper()}</div>`,()=>{window.print();return false;},'浏览器打印 / 存为 PDF');},
  /**
   * 修改流程基础信息。
   * @returns {void}
   */
  basic() {UI.modal('流程基本信息',`<label class="field">流程编码<input class="inp mono" value="${this.state.code}" disabled></label><label class="field">分类<select class="sel" name="category"><option>采购管理</option><option>财务管理</option><option>人事行政</option></select></label><label class="field">描述<textarea class="inp" name="description" rows="3">${UI.escape(this.state.description||(this.state.name+'的审批规则与处理路径。'))}</textarea></label>`,dialog=>{this.state.description=dialog.querySelector('[name=description]').value;this.state.category=dialog.querySelector('[name=category]').value;this.render();});},
  /**
   * 导出本原型的可恢复草稿。
   * @returns {void}
   */
  export() {this.syncInputs();const blob=new Blob([JSON.stringify({prototype:'approval-designer',version:2,state:this.state},null,2)],{type:'application/json'});const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=this.state.code.toLowerCase()+'.prototype.json';a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);UI.toast('原型草稿已导出。正式流程文件格式由契约任务确定。');},
  /**
   * 导入仅由本原型导出的草稿文件。
   * @returns {void}
   */
  import() {UI.modal('导入原型草稿','<label class="field">选择原型 JSON 文件<input class="inp" type="file" accept=".json" name="file"></label><p class="hint">仅支持本原型导出的文件，导入后替换当前草稿；不接受其他工具的流程文件。</p>',dialog=>{const file=dialog.querySelector('[name=file]').files[0];if(!file){UI.toast('请选择文件。');return false;}file.text().then(text=>{try{const data=JSON.parse(text);if(data.prototype!=='approval-designer'||![1,2].includes(data.version)||!DesignerGraph.accepts(data.state))throw new Error();if(data.state.flowKey!==this.state.flowKey||data.state.nodes.some(n=>!/^[-A-Za-z0-9_]+$/.test(n.id)))throw new Error();this.state=DesignerGraph.migrate(data.state);this.selected=this.state.nodes[0].id;this.render();UI.closeModal();UI.toast('原型草稿导入完成。');}catch{UI.toast('文件格式或版本不兼容，未修改当前草稿。');}});return false;},'导入草稿');}
};
Object.assign(D,DesignerCanvas,DesignerSimulation);
D.init();

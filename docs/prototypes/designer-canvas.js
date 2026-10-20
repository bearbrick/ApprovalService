/**
 * 唯一自由画布界面及其交互；由 designer.js 初始化前合并到 D。
 */
const DesignerCanvas = {
  /** 当前缩放比例。 */
  zoom:0.9,
  /** 是否专注画布。 */
  focused:false,
  /**
   * 切换隐藏全局导航的专注视图，可随时恢复。
   * @returns {void}
   */
  focusCanvas() {this.focused=!this.focused;document.body.classList.toggle('canvas-focused',this.focused);this.render();},
  /**
   * 初次打开时将选中节点移到可视区域，避免只看到大片空白。
   * @param {string} id 节点编号。
   * @returns {void}
   */
  revealNode(id) {const n=this.state.nodes.find(a=>a.id===id),el=document.querySelector('#canvas-scroll');if(n&&el)el.scrollTo(Math.max(0,n.x*this.zoom-250),Math.max(0,n.y*this.zoom-60));},
  /** 待连接的起点。 */
  connectFrom:'',
  /** 当前路径演示结果。 */
  simulation:null,
  /**
   * 保存前从可见控件读取真实值，避免未失焦的最后一次输入丢失。
   * 只识别固定的绑定格式，不执行事件属性中的代码。
   * @returns {void}
   */
  syncInputs() {
    document.querySelectorAll('.page-body input,.page-body select,.page-body textarea').forEach(el=>{
      const handler=el.getAttribute('oninput')||el.getAttribute('onchange')||'',read=kind=>kind==='checked'?el.checked:el.value;
      const node=handler.match(/^D\.changeNode\('([A-Za-z]+)',this\.(value|checked)\)/);
      if(node&&this.node())this.node()[node[1]]=read(node[2]);
      const field=handler.match(/^D\.field\((\d+),'([A-Za-z]+)',this\.(value|checked)\)/);
      if(field&&this.state.fields[Number(field[1])])this.state.fields[Number(field[1])][field[2]]=read(field[3]);
      const setting=handler.match(/^D\.setting\('([A-Za-z]+)',this\.(value|checked)\)/);
      if(setting)this.state.settings[setting[1]]=read(setting[2]);
    });
  },
  /**
   * 绘制事件、网关和服务节点的专属属性。
   * @param {object} n 节点。
   * @param {string} common 通用标题与名称控件。
   * @returns {string|null} 专属属性或空值。
   */
  specialInspector(n,common) {
    if(n.type==='start')return common+'<div class="notice">业务系统通过接口发起后进入流程。</div><button class="btn" style="margin-top:14px" onclick="D.tab=\'rules\';D.render()">设置接入应用与发起范围</button>';
    if(n.type==='end')return common+'<div class="notice">结束事件没有后续连线。审批状态由所有有效分支的处理结果确定。</div>';
    if(['exclusive','inclusive','parallel','parallelJoin','inclusiveJoin'].includes(n.type)) {
      const join=n.gatewayRole==='join'||n.type.endsWith('Join'),parallel=['parallel','parallelJoin','inclusive','inclusiveJoin'].includes(n.type);
      const type=n.type==='parallel'?'parallelJoin':n.type==='parallelJoin'?'parallel':n.type==='inclusive'?'inclusiveJoin':'inclusive';
      return common+`<div class="notice">${join?'汇合前序分支，完成后再继续。':n.type==='exclusive'?'按连线顺序走第一条满足的条件；必须有默认分支。':n.type==='inclusive'?'所有满足条件的路线都执行；未命中时走默认分支。':'所有路线同时执行，配对汇合网关等待分支完成。'}</div>${parallel?`<label class="field" style="margin-top:16px"><span>配对${join?'分叉':'汇合'}网关</span><select class="sel" onchange="D.pairGateway(this.value)"><option value="">请选择配对网关</option>${this.state.nodes.filter(a=>a.type===type).map(a=>`<option value="${a.id}" ${a.id===n.pairedGatewayId?'selected':''}>${UI.escape(a.name)}</option>`).join('')}</select></label>`:''}<button class="btn" style="margin-top:14px" onclick="D.edge()">添加分支连线</button><p class="hint" style="margin-top:12px">点击下方连线可编辑条件、兜底分支或删除路线。</p>`;
    }
    if(['auto','sub'].includes(n.type))return common+`<label class="field"><span>${n.type==='sub'?'子流程编码':'业务动作标识'}</span><input class="inp" value="${UI.escape(n.subFlow||n.action||n.target||n.people||'')}" oninput="D.changeNode('${n.type==='sub'?'subFlow':'action'}',this.value)"></label>${n.type==='sub'?`<label class="check-row"><input type="checkbox" ${n.wait!==false?'checked':''} oninput="D.changeNode('wait',this.checked)">等待子流程完成</label>`:'<p class="hint">失败自动重试；重试耗尽交给管理员处理。</p>'}<button class="btn" onclick="D.extraNode()">${n.type==='sub'?'编辑字段映射':'设置失败重试'}</button>`;
    return null;
  },
  /**
   * 双向设置网关配对，清理已有的反向关联。
   * @param {string} id 目标网关。
   * @returns {void}
   */
  pairGateway(id) {const n=this.node(),old=this.state.nodes.find(a=>a.id===n.pairedGatewayId);if(old?.pairedGatewayId===n.id)delete old.pairedGatewayId;n.pairedGatewayId=id;const target=this.state.nodes.find(a=>a.id===id);if(target){const previous=this.state.nodes.find(a=>a.id===target.pairedGatewayId);if(previous?.id!==n.id&&previous?.pairedGatewayId===id)delete previous.pairedGatewayId;target.pairedGatewayId=n.id;}this.render();},
  /**
   * 绘制节点级拒绝、时限与意见栏规则。
   * @param {object} n 节点。
   * @returns {string} 配置标记。
   */
  policyFields(n) {return `<div class="inspector-section"><h4>节点处理规则</h4>${n.type==='approval'?`<label class="field"><span>有人拒绝时</span><select class="sel" onchange="D.changeNode('reject',this.value)">${['立即结束','等所有人审完再决定','按票签阈值决定'].map(v=>`<option ${(n.reject||n.rejectPolicy)===v?'selected':''}>${v}</option>`).join('')}</select></label>`:''}<label class="field"><span>处理时限（工作日，空值继承流程）</span><input class="inp" type="number" min="1" value="${UI.escape(n.workDays||'')}" oninput="D.changeNode('workDays',this.value)"></label><label class="field"><span>意见栏标题</span><input class="inp" value="${UI.escape(n.opinionTitle||n.name)}" oninput="D.changeNode('opinionTitle',this.value)"></label></div>`;},
  /**
   * 绘制画布工具栏、工作区与节点属性。
   * @returns {string} 界面标记。
   */
  flow() {
    return `${this.state.migrationNote?`<div class="notice" style="margin-bottom:12px">${UI.escape(this.state.migrationNote)}</div>`:''}<div class="design-toolbar"><span class="pill accent">${UI.icon('layers')} 自由画布</span><button class="btn sm" onclick="D.addNode()">${UI.icon('plus')} 添加节点</button><button class="btn sm" onclick="D.edge()">${UI.icon('chevR')} 添加连线</button><span class="toolbar-separator"></span><button class="btn sm" onclick="D.copyNode()">复制节点</button><button class="btn sm danger" onclick="D.deleteNode()">删除</button><span class="spacer"></span><button class="btn sm" onclick="D.focusCanvas()">${this.focused?'退出专注':'专注画布'}</button><button class="btn sm ghost" onclick="D.resetTemplate()">恢复示例</button><button class="btn sm ghost" onclick="D.export()">导出</button><button class="btn sm ghost" onclick="D.import()">导入</button><button class="btn sm" onclick="D.validate()">${UI.icon('check')} 检查流程</button></div><div class="design-board"><div class="canvas-viewport"><div class="canvas-context"><span>${this.connectFrom?'请选择目标节点完成连线':'拖动节点 · 点击连线设置条件 · 方向键微调选中节点'}</span>${this.connectFrom?'<button class="btn sm" onclick="D.connectFrom=\'\';D.render()">取消连线</button>':''}</div><div class="design-stage" id="canvas-scroll">${this.canvas()}</div><div class="canvas-controls"><button class="btn sm" aria-label="缩小" onclick="D.setZoom(D.zoom-.1)">−</button><span>${Math.round(this.zoom*100)}%</span><button class="btn sm" aria-label="放大" onclick="D.setZoom(D.zoom+.1)">＋</button><button class="btn sm" onclick="D.fit()">适应画布</button><button class="btn sm ghost" onclick="D.setZoom(1)">100%</button></div></div><aside class="node-inspector">${this.inspector()}<div class="inspector-section"><h4>节点连线</h4>${this.state.edges.map((e,i)=>e[0]===this.selected||e[1]===this.selected?`<button class="node-edge-row" onclick="D.edge(${i})">${e[0]===this.selected?'至':'从'} ${UI.escape(this.state.nodes.find(n=>n.id===(e[0]===this.selected?e[1]:e[0]))?.name||'未知节点')}<small>${UI.escape(e[2]||'无条件')}</small></button>`:'').join('')||'<p class="hint">尚无关联连线</p>'}</div></aside></div><div class="design-footer"><span>本地草稿 · 自由画布</span><span>${this.state.nodes.length} 个节点</span><span>${this.state.edges.length} 条连线</span><span style="margin-left:auto">${this.state.saved?'已保存 '+this.state.saved:'修改后请保存草稿'}</span></div>`;
  },
  /**
   * 获取画布范围。
   * @returns {object} 宽高。
   */
  bounds() {return {width:Math.max(1100,...this.state.nodes.map(n=>n.x+240)),height:Math.max(650,...this.state.nodes.map(n=>n.y+170))};},
  /**
   * 生成图形与节点层。
   * @returns {string} 画布标记。
   */
  canvas() {const b=this.bounds();return `<div class="canvas-sizer" style="width:${b.width*this.zoom}px;height:${b.height*this.zoom}px"><div class="canvas-space" style="width:${b.width}px;height:${b.height}px;transform:scale(${this.zoom})"><svg id="canvas-edges" aria-label="流程连线">${this.edgesSvg()}</svg>${this.state.nodes.map(n=>this.card(n)).join('')}</div></div>`;},
  /**
   * 生成可操作的节点卡片。
   * @param {object} n 节点。
   * @returns {string} 节点标记。
   */
  card(n) {
    const labels={start:'开始事件',end:'结束事件',approval:'审批任务',handle:'办理任务',cc:'抄送通知',auto:'自动任务',sub:'子流程',exclusive:n.gatewayRole==='join'?'排他汇合':'排他分支',inclusive:'包容分支',parallel:'并行分叉',parallelJoin:'并行汇合',inclusiveJoin:'包容汇合'};
    const icon={start:'play',end:'check',approval:'user',handle:'edit',cc:'send',auto:'gear',sub:'layers'}[n.type]||'layers';
    return `<div class="flow-node canvas-node ${this.selected===n.id?'selected':''} ${['start','end'].includes(n.type)?'event-node':''}" style="left:${n.x}px;top:${n.y}px" data-node="${UI.escape(n.id)}" role="button" tabindex="0" aria-label="${UI.escape(n.name)}，${labels[n.type]}" onclick="D.selectNode('${n.id}')"><div class="node-cap ${n.type}">${UI.icon(icon)}${labels[n.type]||n.type}<small>${n.type==='approval'?UI.escape(n.method||'依次审批'):''}</small></div><div class="node-body"><b>${UI.escape(n.name)}</b><span>${UI.escape(n.people||(['exclusive','inclusive','parallel','parallelJoin','inclusiveJoin'].includes(n.type)?'点击配置路线':'流程事件'))}</span></div>${n.type!=='end'?`<button class="node-port" aria-label="从${UI.escape(n.name)}连线" onclick="event.stopPropagation();D.connectFrom='${n.id}';D.render()">+</button>`:''}</div>`;
  },
  /**
   * 创建连线，按节点相对位置选择折线锚点。
   * @returns {string} SVG 内容。
   */
  edgesSvg() {
    return `<defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10Z" fill="#8397b4"/></marker></defs>${this.state.edges.map((e,i)=>{const a=this.state.nodes.find(n=>n.id===e[0]),b=this.state.nodes.find(n=>n.id===e[1]);if(!a||!b)return '';let path,lx,ly;
      const left=Math.min(a.x,b.x)+180,right=Math.max(a.x,b.x),sameRow=Math.abs(b.y-a.y)<30;
      const obstacles=this.state.nodes.filter(n=>n!==a&&n!==b&&n.x<right&&n.x+180>left&&n.y<Math.max(a.y,b.y)+44&&n.y+88>Math.min(a.y,b.y)+44);
      if(sameRow&&right>left&&obstacles.length){
        // 跨过同排节点的直达路线独立绕行，避免默认分支看起来经过中间审批节点。
        const minX=Math.min(a.x,b.x)+90,maxX=Math.max(a.x,b.x)+90;
        const candidates=[];
        for(let distance=32;distance<=320;distance+=36)candidates.push(Math.min(a.y,b.y)-distance,Math.max(a.y,b.y)+88+distance);
        const y=candidates.find(value=>value>=16&&!this.state.nodes.some(n=>n!==a&&n!==b&&n.x<maxX&&n.x+180>minX&&n.y-8<value&&n.y+96>value))??Math.max(...this.state.nodes.map(n=>n.y+88))+32;
        const below=y>Math.max(a.y,b.y)+88;
        path=`M${a.x+90} ${a.y+(below?88:0)}V${y}H${b.x+90}V${b.y+(below?88:0)}`;lx=(a.x+b.x)/2+90;ly=y+(below?16:-9);
      }else if(b.x>=a.x+185){const x1=a.x+180,y1=a.y+44,x2=b.x,y2=b.y+44,m=(x1+x2)/2;path=`M${x1} ${y1}H${m}V${y2}H${x2}`;lx=m;ly=(y1+y2)/2-10;}else if(Math.abs(b.y-a.y)<30&&b.x<a.x-185){const y=a.y+44;path=`M${a.x} ${y}H${b.x+180}`;lx=(a.x+b.x+180)/2;ly=y-10;}else if(b.y>a.y+130){const x1=a.x+90,y1=a.y+88,x2=b.x+90,y2=b.y,m=(y1+y2)/2;path=`M${x1} ${y1}V${m}H${x2}V${y2}`;lx=(x1+x2)/2+12;ly=m-8;}else {const y=Math.min(a.y,b.y)-30;path=`M${a.x+90} ${a.y}V${y}H${b.x+90}V${b.y}`;lx=(a.x+b.x)/2+90;ly=y-9;}return `<g class="edge-group" role="button" tabindex="0" aria-label="编辑${UI.escape(a.name)}到${UI.escape(b.name)}的连线" onclick="D.edge(${i})" onkeydown="if(event.key==='Enter')D.edge(${i})"><path class="edge-hit" d="${path}"/><path class="canvas-path ${DesignerGraph.isDefault(e[2])?'default-edge':''}" marker-end="url(#arrow)" d="${path}"/>${e[2]?`<text class="edge-label" x="${lx}" y="${ly}" text-anchor="middle">${UI.escape(e[2])}</text>`:''}</g>`;}).join('')}`;
  },
  /**
   * 选择节点或完成端口连线。
   * @param {string} id 节点标识。
   * @returns {void}
   */
  selectNode(id) {if(this.connectFrom&&this.connectFrom!==id){const from=this.connectFrom;this.connectFrom='';this.edge(-1,from,id);return;}this.selected=id;this.render();},
  /**
   * 修改缩放并保留画布滚动。
   * @param {number} value 缩放。
   * @returns {void}
   */
  setZoom(value) {this.zoom=Math.max(.3,Math.min(1.5,value));this.render();},
  /**
   * 将全部节点适配到当前视口。
   * @returns {void}
   */
  fit() {const el=document.querySelector('#canvas-scroll');if(!el)return;const b=this.bounds();this.zoom=Math.max(.3,Math.min(1,(el.clientWidth-30)/b.width,(el.clientHeight-30)/b.height));this.render();document.querySelector('#canvas-scroll')?.scrollTo(0,0);},
  /**
   * 拖动节点、键盘微调，并保留详情字段拖拽。
   * @returns {void}
   */
  bindDrag() {
    document.querySelectorAll('.canvas-node').forEach(el=>{
      el.onkeydown=e=>{if(e.target!==el)return;if(e.key==='Enter'){this.selectNode(el.dataset.node);return;}const delta={ArrowLeft:[-10,0],ArrowRight:[10,0],ArrowUp:[0,-10],ArrowDown:[0,10]}[e.key];if(delta){e.preventDefault();const n=this.state.nodes.find(n=>n.id===el.dataset.node);n.x=Math.max(20,n.x+delta[0]);n.y=Math.max(40,n.y+delta[1]);this.selected=n.id;this.render();document.querySelector(`[data-node="${n.id}"]`)?.focus();}};
      el.onpointerdown=e=>{if(e.button!==0||e.target.closest('.node-port'))return;const n=this.state.nodes.find(n=>n.id===el.dataset.node),x=e.clientX,y=e.clientY,ox=n.x,oy=n.y;let moved=false;el.setPointerCapture(e.pointerId);el.onpointermove=event=>{if(Math.abs(event.clientX-x)+Math.abs(event.clientY-y)>4)moved=true;if(!moved)return;n.x=Math.max(20,Math.round((ox+(event.clientX-x)/this.zoom)/10)*10);n.y=Math.max(40,Math.round((oy+(event.clientY-y)/this.zoom)/10)*10);el.style.left=n.x+'px';el.style.top=n.y+'px';document.querySelector('#canvas-edges').innerHTML=this.edgesSvg();};el.onpointerup=()=>{el.onpointermove=null;if(moved){el.onclick=event=>event.preventDefault();this.selected=n.id;this.render();}};el.onpointercancel=()=>{el.onpointermove=null;};};
    });
    let dragging='';document.querySelectorAll('.layout-cell').forEach(el=>{el.ondragstart=()=>dragging=el.dataset.field;el.ondragover=e=>e.preventDefault();el.ondrop=e=>{e.preventDefault();const from=this.state.fields.findIndex(f=>f.key===dragging),to=this.state.fields.findIndex(f=>f.key===el.dataset.field);if(from<0||to<0)return;this.state.fields.splice(to,0,this.state.fields.splice(from,1)[0]);this.render();};});
  },
  /**
   * 添加节点，可插入选中节点后的单一路线。
   * @returns {void}
   */
  addNode() {
    const n=this.node();UI.modal('添加节点',`<div class="form-grid"><label class="field">节点类型<select name="type" class="sel">${Object.entries({approval:'审批任务',handle:'办理任务',cc:'抄送通知',sub:'子流程',auto:'自动任务',exclusive:'排他网关',inclusive:'包容网关',parallel:'并行分叉',parallelJoin:'并行汇合',inclusiveJoin:'包容汇合',start:'开始事件',end:'结束事件'}).map(([v,t])=>`<option value="${v}">${t}</option>`).join('')}</select></label><label class="field">节点名称<input name="name" class="inp" placeholder="如：法务审核" required></label><label class="field">处理人 / 目标<input name="people" class="inp" placeholder="可稍后设置"></label><label class="field">放置方式<select name="placement" class="sel"><option value="insert">插入选中节点之后</option><option value="free">独立放置，自行连线</option></select></label></div><p class="hint">分叉节点有多条出线时采用独立放置，请明确连线位置。</p>`,dialog=>{const type=dialog.querySelector('[name=type]').value,id='node'+Date.now(),node={id,type,name:dialog.querySelector('[name=name]').value.trim(),people:dialog.querySelector('[name=people]').value,source:'指定成员',method:'依次审批',x:(n?.x||50)+230,y:(n?.y||150)+130};if(type==='start'&&this.state.nodes.some(a=>a.type==='start')){UI.toast('流程已有开始事件。');return false;}this.state.nodes.push(node);const out=this.state.edges.filter(e=>e[0]===n?.id);if(dialog.querySelector('[name=placement]').value==='insert'&&type!=='start'&&n&&n.type!=='end'&&out.length<=1){if(out.length){const e=out[0],target=e[1];e[1]=id;if(type!=='end')this.state.edges.push([id,target,'']);}else this.state.edges.push([n.id,id,'']);}this.selected=id;this.render();},'添加');
  },
  /**
   * 复制节点配置，不复制连线以避免意外分叉。
   * @returns {void}
   */
  copyNode() {const n=this.node();if(!n)return;if(n.type==='start'){UI.toast('开始事件只能有一个。');return;}const copy=structuredClone(n);copy.id='node'+Date.now();copy.name+='（副本）';copy.x+=40;copy.y+=130;delete copy.pairedGatewayId;this.state.nodes.push(copy);this.selected=copy.id;this.render();UI.toast('节点已复制，请连接到流程。');},
  /**
   * 删除节点及关联连线，单进单出时可接回原路线。
   * @returns {void}
   */
  deleteNode() {const n=this.node();if(!n)return;UI.modal('删除节点',`<p>删除“${UI.escape(n.name)}”及其关联连线。</p><label class="check-row"><input type="checkbox" name="bridge" checked>单进单出时接回原路线</label><p class="hint">开始和结束节点也可删除；结构不完整时将阻止发布。</p>`,dialog=>{const incoming=this.state.edges.filter(e=>e[1]===n.id),out=this.state.edges.filter(e=>e[0]===n.id);this.state.edges=this.state.edges.filter(e=>e[0]!==n.id&&e[1]!==n.id);if(dialog.querySelector('[name=bridge]').checked&&incoming.length===1&&out.length===1&&incoming[0][0]!==out[0][1])this.state.edges.push([incoming[0][0],out[0][1],incoming[0][2]]);this.state.nodes=this.state.nodes.filter(a=>a.id!==n.id);this.selected=this.state.nodes[0]?.id||'';this.render();},'删除节点');},
  /**
   * 编辑条件或默认分支，也可删除已有连线。
   * @param {number} index 连线序号。
   * @param {string} fromId 新连线起点。
   * @param {string} toId 新连线终点。
   * @returns {void}
   */
  edge(index=-1,fromId=this.selected,toId='') {const e=this.state.edges[index]||[fromId,toId,''];const opts=value=>this.state.nodes.map(n=>`<option value="${n.id}" ${n.id===value?'selected':''}>${UI.escape(n.name)}</option>`).join('');UI.modal('连线设置',`<div class="form-grid"><label class="field">起点<select class="sel" name="from">${opts(e[0])}</select></label><label class="field">终点<select class="sel" name="to">${opts(e[1])}</select></label><label class="field full">条件表达式 / 路线说明<input class="inp" name="condition" value="${UI.escape(DesignerGraph.isDefault(e[2])?'':e[2])}" placeholder='amount >= 10000 或 rework == "需要返工"'></label></div><label class="check-row"><input name="default" type="checkbox" ${DesignerGraph.isDefault(e[2])?'checked':''}>默认分支（其他条件均不满足时）</label><p class="hint">排他分支按连线顺序匹配第一条满足条件的路线；包容分支执行所有命中路线。</p>${index>=0?`<button class="btn danger" onclick="D.state.edges.splice(${index},1);UI.closeModal();D.render()">删除连线</button>`:''}`,dialog=>{const from=dialog.querySelector('[name=from]').value,to=dialog.querySelector('[name=to]').value;if(!from||!to||from===to){UI.toast('请选择两个不同的节点。');return false;}if(this.state.edges.some((a,i)=>i!==index&&a[0]===from&&a[1]===to)){UI.toast('这两个节点之间已有连线。');return false;}const value=dialog.querySelector('[name=default]').checked?'其他情况':dialog.querySelector('[name=condition]').value.trim();if(value==='其他情况'&&this.state.edges.some((a,i)=>i!==index&&a[0]===from&&DesignerGraph.isDefault(a[2]))){UI.toast('同一网关只能有一条默认路线。');return false;}const edge=[from,to,value];if(index<0)this.state.edges.push(edge);else this.state.edges[index]=edge;this.render();});},
  /**
   * 检查当前草稿。
   * @returns {string[]} 检查问题。
   */
  errors() {return DesignerGraph.validate(this.state);},
  /**
   * 呈现图结构检查结果。
   * @returns {void}
   */
  validate() {const errors=this.errors();UI.modal(errors.length?'流程检查 · '+errors.length+' 项待处理':'流程结构检查通过',errors.length?`<ul class="validation-errors">${errors.map(e=>`<li>${UI.escape(e)}</li>`).join('')}</ul>`:'<div class="notice">开始与结束、节点连线、从开始可达、可到达结束、分支默认路线及基础配置检查通过。</div><p class="hint">生产引擎仍需验证高级表达式、并发语义、权限与外部系统条件。</p>',()=>{},'关闭');}
};

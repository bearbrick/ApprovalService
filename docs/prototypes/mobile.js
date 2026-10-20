/**
 * 手机审批独立导航状态，业务记录与桌面原型共用。
 */
const mobile = { rows:ApprovalDemo.load(), tab:'todo', detail:null, search:'', flow:'', logged:localStorage.getItem('approval-mobile-logged')!=='false' };
/**
 * 安全输出文本。
 * @param {string} text 原始文本。
 * @returns {string} 转义后的文本。
 */
function me(text) { return UI.escape(String(text??'')); }
/**
 * 保存手机操作并重绘。
 * @returns {void} 无返回值。
 */
function saveMobile() { ApprovalDemo.save(mobile.rows);renderMobile(); }
/**
 * 渲染手机审批壳。
 * @returns {void} 无返回值。
 */
function renderMobile() {
  if(!mobile.logged){document.getElementById('app').innerHTML=`<div class="mobile-stage"><div class="mobile-top"><h1>ApprovalService</h1><a href="center.html">电脑版</a></div><div class="mobile-login"><img src="assets/company-logo.png" alt="公司标识" style="max-width:150px;height:36px;object-fit:contain"><h1>欢迎回来</h1><p class="hint">登录审批中心，随时处理工作待办。</p><label class="field">工号<input id="mobile-user" class="inp" placeholder="请输入工号" autocomplete="username"></label><label class="field">密码<input id="mobile-pass" class="inp" type="password" placeholder="请输入密码" autocomplete="current-password"></label><p class="hint"><label><input type="checkbox" checked> 记住登录状态</label></p><button class="btn primary" onclick="mobileLogin()">登录</button><p class="hint" style="margin-top:18px">交互原型：输入任意非空工号和密码即可体验。</p></div></div>`;return;}
  const title=mobile.detail?'审批详情':({todo:'待办审批',done:'已办审批',cc:'抄送我的',mine:'我的'})[mobile.tab];
  document.getElementById('app').innerHTML=`<div class="mobile-stage"><header class="mobile-top">${mobile.detail?'<button aria-label="返回列表" onclick="backMobile()">'+UI.icon('chevL')+'</button>':''}<h1>${title}</h1><button aria-label="刷新" onclick="mobile.rows=ApprovalDemo.load();renderMobile();UI.toast('已刷新')">${UI.icon('refresh')}</button><a href="center.html">电脑版 ${UI.icon('chevR')}</a></header><main class="mobile-body">${mobile.detail?mobileDetail():mobile.tab==='mine'?mobileMine():mobileList()}</main>${mobile.detail?mobileActions():`<nav class="mobile-nav" aria-label="手机导航">${[['todo','待办','cards'],['done','已办','check'],['cc','抄送','send'],['mine','我的','user']].map(t=>`<button class="${mobile.tab===t[0]?'active':''}" onclick="mobile.tab='${t[0]}';mobile.search='';mobile.flow='';renderMobile();window.scrollTo(0,0)">${UI.icon(t[2])}${t[0]==='todo'?`<span class="badge">${mobile.rows.filter(r=>r.category==='todo').length}</span>`:''}<span>${t[1]}</span></button>`).join('')}</nav>`}</div>`;
}
/**
 * 登录演示账号，不持久保存密码。
 * @returns {void} 无返回值。
 */
function mobileLogin() { if(!document.getElementById('mobile-user').value.trim()||!document.getElementById('mobile-pass').value)return UI.toast('请输入工号和密码');mobile.logged=true;localStorage.setItem('approval-mobile-logged','true');renderMobile(); }
/**
 * 按条件筛选手机记录。
 * @returns {Array} 过滤后的审批记录。
 */
function mobileFiltered() { return mobile.rows.filter(r=>r.category===mobile.tab&&(!mobile.flow||r.flow===mobile.flow)&&(!mobile.search||JSON.stringify(r).includes(mobile.search))); }
/**
 * 渲染手机待办列表。
 * @returns {string} 卡片列表内容。
 */
function mobileList() { return `${mobile.tab==='todo'?'<div class="mobile-greeting"><div><h2>上午好，赵敏</h2><p>每一次认真审批，让协作更高效。</p></div><span class="mobile-avatar">赵敏</span></div>':''}<div class="mobile-search"><input aria-label="搜索审批" placeholder="搜索审批主题、单号" value="${me(mobile.search)}" oninput="mobile.search=this.value;document.getElementById('mobile-cards').innerHTML=mobileCards()"><button class="btn" aria-label="筛选" onclick="mobileFilter()">${UI.icon('filter')}</button></div><div class="mobile-filter">${['','费用报销','采购申请'].map(f=>`<button class="${mobile.flow===f?'active':''}" onclick="mobile.flow='${f}';renderMobile()">${f||'全部'}</button>`).join('')}<span class="mobile-count">${mobileFiltered().length} 项</span></div><div id="mobile-cards">${mobileCards()}</div><p class="hint" style="text-align:center;margin-top:23px">已显示全部审批 · 点击右上角刷新</p>`; }
/**
 * 绘制审批摘要卡片。
 * @returns {string} 摘要卡片。
 */
function mobileCards() { return mobileFiltered().map(r=>`<button class="mobile-card" onclick="openMobile('${r.id}')"><div class="mobile-card-top"><span class="flow">${UI.icon('logs')} ${r.flow}</span>${r.urgent?'<span class="mobile-urgent">加急</span>':`<span class="pill ${r.category==='todo'?'accent':r.status==='已拒绝'?'danger':'ok'}">${r.status}</span>`}</div><h3>${r.read?'':'<span class="mobile-dot"></span>'}${me(r.title)}</h3><div class="mobile-card-meta"><span>${r.person}</span><span>·</span><span>${r.dept}</span><span style="margin-left:auto">${r.time.slice(5)}</span></div><div class="mobile-card-amount"><span>${r.flow==='采购申请'?'采购金额':'报销金额'}</span><strong>${ApprovalDemo.money(r.amount)}</strong></div><div class="mobile-card-foot"><span>${me(r.node)}</span><span>${r.key}　${UI.icon('chevR')}</span></div></button>`).join('')||`<div class="mobile-empty">${UI.icon('cards')}暂无符合条件的审批</div>`; }
/**
 * 打开审批详情。
 * @param {string} id 审批编号。
 * @returns {void} 无返回值。
 */
function openMobile(id) { mobile.detail=mobile.rows.find(r=>r.id===id);mobile.detail.read=true;saveMobile();window.scrollTo(0,0); }
/**
 * 从详情回到列表，重置导航滚动位置。
 * @returns {void} 无返回值。
 */
function backMobile() { mobile.detail=null;renderMobile();window.scrollTo(0,0); }
/**
 * 绘制手机详情。
 * @returns {string} 详情内容。
 */
function mobileDetail() { const r=mobile.detail;const purchase=r.flow==='采购申请';return `<h2 class="mobile-detail-title">${me(r.title)}</h2><div class="mobile-detail-meta"><span>${r.id}</span><span class="pill accent">${r.status}</span></div><section class="mobile-section"><h3>基本信息 <span class="hint" style="float:right">业务数据 · 只读</span></h3><div class="mobile-section-body">${[['发起人',r.person+' / '+r.dept],['提交时间',r.time],['业务单号',r.key],['来源系统','示例业务系统'],[purchase?'供应商':'费用事由',purchase?'深圳市启辰科技有限公司':r.title]].map(f=>`<div class="mobile-data-row"><label>${f[0]}</label><span>${me(f[1])}</span></div>`).join('')}</div></section><section class="mobile-section"><h3>${purchase?'采购':'费用'}明细</h3><div class="mobile-section-body">${(purchase && r.amount!==12800?[[r.title,r.amount.toFixed(2),'按来源业务单据明细']]:!purchase && r.amount!==1334.5?[[r.title,r.amount.toFixed(2),r.time.slice(0,10)]]:purchase?[['笔记本电脑','11,200.00','商用 14 英寸 · 2 台'],['显示器','1,600.00','27 英寸 2K · 2 台']]:[['交通费','686.00','2026-10-08 · 深圳往返高铁'],['住宿费','560.00','2026-10-08 · 深圳酒店 2 晚'],['交通费','88.50','2026-10-09 · 客户现场往返打车']]).map(a=>`<div class="mobile-item"><div><span>${a[0]}</span><b>¥ ${a[1]}</b></div><small>${a[2]}</small></div>`).join('')}</div><div class="mobile-sum"><span>合计金额</span><strong>${ApprovalDemo.money(r.amount)}</strong></div></section><section class="mobile-section"><h3>附件 <span class="hint">2 个</span></h3><div class="mobile-section-body"><button class="mobile-file" onclick="mobilePreview()">${UI.icon('logs')} 电子发票.pdf <span style="margin-left:auto">预览 ${UI.icon('chevR')}</span></button><button class="mobile-file" style="margin-top:8px" onclick="mobilePreview()">${UI.icon('image')} 费用凭证.jpg <span style="margin-left:auto">预览 ${UI.icon('chevR')}</span></button></div></section><section class="mobile-section"><h3>审批进度</h3><div class="mobile-section-body"><div class="mobile-step"><b>${r.person} 提交申请</b><small>${r.time}</small></div><div class="mobile-step"><b>部门负责人 · 李娜</b><small>已同意 · 10-13 09:45</small><p>情况属实，同意。</p></div><div class="mobile-step ${r.category==='todo'?'active':''}"><b>财务审核 · 赵敏</b><small>${r.category==='todo'?'等待您处理 · 剩余 1 个工作日':'已处理'}</small>${r.history?.length?`<p>${me(r.history.at(-1).action)}：${me(r.history.at(-1).opinion)}</p>`:''}</div><div class="mobile-step ${r.node==='出纳办理'?'active':'future'}"><b>出纳办理 · 刘洋</b><small>${r.node==='出纳办理'?'待处理':'等待前序节点完成'}</small></div></div></section>${r.category==='todo'?`<section class="mobile-section"><h3>审批意见 <span class="hint" style="float:right">自动暂存</span></h3><div class="mobile-section-body"><textarea class="inp mobile-opinion" id="mobile-opinion" placeholder="请输入审批意见" oninput="localStorage.setItem('approval-draft-${r.id}',this.value)">${me(localStorage.getItem('approval-draft-'+r.id)||'')}</textarea><button class="mobile-phrase" onclick="mobilePhrase('同意，按制度执行。')">同意，按制度执行。</button><button class="mobile-phrase" onclick="mobilePhrase('请补充相关凭证。')">请补充相关凭证。</button><div style="display:flex;gap:8px"><label class="btn sm">${UI.icon('image')} 拍照 / 附件<input type="file" accept="image/*" capture="environment" hidden onchange="mobilePhoto(this.files[0])"></label><button class="btn sm" onclick="mobileSignature()">${UI.icon('pen')} 手写签名</button></div><span id="mobile-attachment" class="hint"></span></div></section>`:''}<button class="mobile-file" onclick="mobileComment()">${UI.icon('text')} 评论与 @ 提醒 <span style="margin-left:auto">${r.comments?.length||0} 条 ${UI.icon('chevR')}</span></button>`; }
/**
 * 输出底部操作栏。
 * @returns {string} 操作栏内容。
 */
function mobileActions() { const r=mobile.detail;return `<div class="mobile-actions">${r.category==='todo'?'<button class="btn more" onclick="mobileMore()">更多</button><button class="btn danger" onclick="mobileAction(\'拒绝\')">拒绝</button><button class="btn primary" onclick="mobileAction(\'同意\')">同意</button>':r.category==='done'&&r.canRevoke?'<button class="btn" onclick="mobileAction(\'撤销同意\')">撤销同意</button><button class="btn primary" onclick="backMobile()">返回列表</button>':'<button class="btn primary" onclick="backMobile()">返回列表</button>'}</div>`; }
/**
 * 快速填写审批意见。
 * @param {string} text 常用语。
 * @returns {void} 无返回值。
 */
function mobilePhrase(text) { document.getElementById('mobile-opinion').value=text;localStorage.setItem('approval-draft-'+mobile.detail.id,text); }
/**
 * 审批操作确认，保存状态避免重复处理。
 * @param {string} action 操作名称。
 * @returns {void} 无返回值。
 */
function mobileAction(action) { UI.closeModal();const opinion=document.getElementById('mobile-opinion')?.value||'';UI.modal('确认'+action,`<p>${me(mobile.detail.title)}</p><p class="hint">${action==='同意'?'通过后进入出纳办理，结果将同步至业务系统。':action==='要求补充'?'通知发起人在业务系统补充材料，随后回到当前节点。':'操作完成后记入审批历史。'}</p>${['转交','加签','征询意见','抄送'].includes(action)?'<label class="field">选择人员<select id="mobile-person" class="sel"><option>刘洋 · 财务部</option><option>李娜 · 生产部</option></select></label>':''}${action==='退回'?'<label class="field">退回目标<select id="mobile-person" class="sel"><option>发起人 · 王强</option><option>部门负责人 · 李娜</option></select></label>':''}<label class="field">${action==='同意'?'审批意见':'操作原因（必填）'}<textarea id="mobile-confirm-opinion" class="inp" rows="3">${me(opinion)}</textarea></label>`,dialog=>{const reason=dialog.querySelector('#mobile-confirm-opinion').value.trim();if(action!=='同意'&&!reason){UI.toast('请填写操作原因');return false;}const fresh=ApprovalDemo.load().find(r=>r.id===mobile.detail.id);if(action==='同意'&&fresh.category!=='todo'){UI.toast('这项审批已处理，请刷新列表');return false;}ApprovalDemo.act(mobile.detail,action,reason||'同意',dialog.querySelector('#mobile-person')?.value||'');saveMobile();UI.toast(action+'成功');},'确认'+action); }
/**
 * 更多操作入口。
 * @returns {void} 无返回值。
 */
function mobileMore() { UI.modal('更多审批操作',`<div style="display:grid;grid-template-columns:repeat(2,1fr);gap:10px">${['退回','转交','加签','征询意见','要求补充','抄送'].map(a=>`<button class="btn" onclick="mobileAction('${a}')">${a}</button>`).join('')}<button class="btn" onclick="UI.closeModal();UI.toast('审批意见已暂存')">暂存意见</button></div>`,()=>{},'关闭'); }
/**
 * 显示手机筛选弹窗。
 * @returns {void} 无返回值。
 */
function mobileFilter() { UI.modal('筛选审批',`<label class="field">流程<select id="mobile-flow" class="sel"><option value="">全部流程</option><option>费用报销</option><option>采购申请</option></select></label><p class="hint">当前演示数据范围：2026 年 10 月。</p>`,d=>{mobile.flow=d.querySelector('#mobile-flow').value;renderMobile();},'应用筛选'); }
/**
 * 附件预览。
 * @returns {void} 无返回值。
 */
function mobilePreview() { UI.modal('附件预览',`<div style="padding:25px 15px;border:1px solid #dce4ee;color:#78607c;background:#fdfcfd;text-align:center"><h3>电子发票（演示）</h3><p>深圳市差旅服务有限公司</p><hr><p>购买方：示例科技有限公司</p><p>项目：差旅服务</p><h3>¥ 1,334.50</h3><p class="hint">非真实票据</p></div>`,()=>{},'关闭'); }
/**
 * 压缩拍照附件并展示状态。
 * @param {File} file 手机选择的图片。
 * @returns {void} 无返回值。
 */
function mobilePhoto(file) { if(!file)return;const url=URL.createObjectURL(file);const img=new Image();img.onload=()=>{const c=document.createElement('canvas');const ratio=Math.min(1,1200/img.width);c.width=img.width*ratio;c.height=img.height*ratio;c.getContext('2d').drawImage(img,0,0,c.width,c.height);mobile.detail.photo=c.toDataURL('image/jpeg',0.75);ApprovalDemo.save(mobile.rows);document.getElementById('mobile-attachment').textContent='已添加并压缩：'+file.name;URL.revokeObjectURL(url);};img.src=url; }
/**
 * 评论和提醒人员。
 * @returns {void} 无返回值。
 */
function mobileComment() { UI.modal('评论与沟通',`${(mobile.detail.comments||[]).map(c=>`<p style="padding:12px;background:#f6f8fc">赵敏：${me(c)}</p>`).join('')}<textarea id="mobile-comment" class="inp" rows="3" placeholder="输入评论，可 @王强 提醒发起人"></textarea><button class="mobile-phrase" onclick="document.getElementById('mobile-comment').value+='@王强 '">@王强</button>`,d=>{const text=d.querySelector('#mobile-comment').value.trim();if(!text){UI.toast('请输入评论');return false;}(mobile.detail.comments||=[]).push(text);saveMobile();UI.toast('评论已发送');},'发送'); }
/**
 * 手机手写签名。
 * @returns {void} 无返回值。
 */
function mobileSignature() { UI.modal('手写签名',`<p class="hint">请在下方区域签名。</p><canvas id="mobile-sign" class="mobile-sign" width="500" height="180"></canvas><button class="btn sm" style="margin-top:8px" onclick="document.getElementById('mobile-sign').getContext('2d').clearRect(0,0,500,180)">重写</button>`,()=>{localStorage.setItem('approval-signature',document.getElementById('mobile-sign').toDataURL());UI.toast('签名已保存');});const c=document.getElementById('mobile-sign');const ctx=c.getContext('2d');ctx.lineWidth=2;ctx.strokeStyle='#17315a';let down=false;c.onpointerdown=e=>{down=true;c.setPointerCapture(e.pointerId);ctx.beginPath();ctx.moveTo(e.offsetX*500/c.clientWidth,e.offsetY*180/c.clientHeight);};c.onpointermove=e=>{if(down){ctx.lineTo(e.offsetX*500/c.clientWidth,e.offsetY*180/c.clientHeight);ctx.stroke();}};c.onpointerup=()=>{down=false;}; }
/**
 * 我的页面。
 * @returns {string} 个人设置与我发起的。
 */
function mobileMine() { return `<div class="mobile-greeting" style="padding:8px 0 20px"><div style="display:flex;gap:14px;align-items:center"><span class="mobile-avatar" style="width:54px;height:54px">赵敏</span><div><h2>赵敏</h2><p>财务部 · 财务审核员</p></div></div><span class="pill ok">已登录</span></div><section class="mobile-section"><h3>我发起的 <span class="hint">只查看进度</span></h3>${mobile.rows.filter(r=>r.category==='mine').map(r=>`<button class="mobile-setting" onclick="openMobile('${r.id}')">${UI.icon('logs')} ${me(r.title)}<span>${UI.icon('chevR')}</span></button>`).join('')}</section><section class="mobile-section">${[['users','委托代理','mobileDelegate()'],['pen','个人签名','mobileSignature()'],['text','常用审批语','mobilePhrases()'],['alert','消息设置','mobileNotices()']].map(t=>`<button class="mobile-setting" onclick="${t[2]}">${UI.icon(t[0])} ${t[1]}<span>${UI.icon('chevR')}</span></button>`).join('')}</section><section class="mobile-section"><button class="mobile-setting" onclick="mobile.logged=false;localStorage.setItem('approval-mobile-logged','false');renderMobile()">${UI.icon('logout')} 退出登录 <span>${UI.icon('chevR')}</span></button></section><p class="hint" style="text-align:center">ApprovalService · v0.1.0 原型<br>业务单据请在来源系统发起或修改</p>`; }
/**
 * 手机委托设置。
 * @returns {void} 无返回值。
 */
function mobileDelegate() { UI.modal('委托代理',`<p class="hint">${me(localStorage.getItem('approval-delegation')||'暂无生效的委托')}</p><label class="field">代理人<select id="m-delegate" class="sel"><option>刘洋 · 财务部</option><option>李娜 · 生产部</option></select></label><label class="field">开始日期<input id="m-start" class="inp" type="date" value="2026-10-14"></label><label class="field">结束日期<input id="m-end" class="inp" type="date" value="2026-10-16"></label><p><label><input type="checkbox" checked> 现有待办一并委托</label></p><p class="hint">全部流程 · 本人仍可处理 · 不连环委托</p>`,d=>{if(d.querySelector('#m-start').value>d.querySelector('#m-end').value){UI.toast('结束日期不能早于开始日期');return false;}localStorage.setItem('approval-delegation',d.querySelector('#m-delegate').value+' · '+d.querySelector('#m-start').value+' 至 '+d.querySelector('#m-end').value);UI.toast('委托设置已保存');}); }
/**
 * 手机常用语维护。
 * @returns {void} 无返回值。
 */
function mobilePhrases() { UI.modal('常用审批语',`<label class="field">每行一条<textarea id="m-phrases" class="inp" rows="5">${me((JSON.parse(localStorage.getItem('approval-phrases')||'null')||['同意，按制度执行。','请补充相关凭证。']).join('\n'))}</textarea></label>`,d=>{localStorage.setItem('approval-phrases',JSON.stringify(d.querySelector('#m-phrases').value.split('\n').filter(Boolean)));UI.toast('常用语已保存');}); }
/**
 * 手机通知偏好维护。
 * @returns {void} 无返回值。
 */
function mobileNotices() { UI.modal('消息设置',`<p><label><input type="checkbox" checked> 站内消息</label></p><p><label><input type="checkbox" checked> 待办与到期提醒邮件</label></p><p><label><input type="checkbox" checked> 每日待办汇总邮件</label></p>`,d=>{localStorage.setItem('approval-mobile-notices',JSON.stringify([...d.querySelectorAll('input')].map(i=>i.checked)));UI.toast('消息设置已保存');}); }
renderMobile();
/**
 * 列表下拉超过阈值时刷新，保留当前筛选条件。
 */
let mobileTouchStart = null;
document.addEventListener('touchstart', event => {
  mobileTouchStart = !mobile.detail && window.scrollY === 0 ? event.touches[0].clientY : null;
}, { passive:true });
document.addEventListener('touchend', event => {
  if (mobileTouchStart !== null && event.changedTouches[0].clientY-mobileTouchStart > 85) {
    mobile.rows=ApprovalDemo.load();
    renderMobile();
    UI.toast('待办已刷新');
  }
  mobileTouchStart=null;
}, { passive:true });

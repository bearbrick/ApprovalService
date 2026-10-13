/**
 * 原型共享界面与对话框工具，不调用真实业务接口。
 */
const UI = {
  /**
   * 输出安全的文本片段。
   * @param {unknown} text 输入文本。
   * @returns {string} 转义后的文本。
   */
  escape(text) { return String(text ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); },
  /**
   * 使用标签服务已有的 Bootstrap 图标。
   * @param {string} name 图标名称。
   * @returns {string} 图标标记。
   */
  icon(name) { return `<svg class="svgi" viewBox="0 0 16 16" aria-hidden="true">${UI_ICONS[name==='close'?'x':name] || UI_ICONS['grid']}</svg>`; },
  /**
   * 渲染页面外壳。
   * @param {string} active 当前模块。
   * @param {string} title 页面标题。
   * @param {string} subtitle 补充信息。
   * @param {string} bodyHtml 工作区域内容。
   * @param {string} actionsHtml 顶部操作按钮。
   * @returns {void} 无返回值。
   */
  shell(active, title, subtitle, bodyHtml, actionsHtml = '') {
    const pages = [['designer','edit','流程设计'],['center','table','审批中心'],['admin','gear','管理后台'],['mobile','preview','手机审批'],['demo','box','业务示例']];
    document.title = `${title} · 审批服务原型`;
    document.querySelector('#app').innerHTML = `<header class="topbar"><button class="icon-btn mobile-menu" aria-label="打开导航">${UI.icon('menu')}</button><a class="brand-link" href="designer.html"><img class="company-logo" src="assets/company-logo.png" alt="智云科技"><strong>审批服务</strong></a><span class="brand-sub">流程设计与审批工作台</span><nav class="topnav" aria-label="主导航">${pages.map(([key,icon,label])=>`<a href="${key}.html" class="${key===active?'active':''}">${UI.icon(icon)}<span>${label}</span></a>`).join('')}<button class="account" id="profile-open"><span class="avatar">系</span><span>系统管理员</span></button></nav></header><aside class="sidebar" aria-label="工作区导航"><div class="side-head"><h2>${active==='designer'?'流程':'审批工作台'}</h2><span class="pill accent">演示环境</span></div><div class="side-search">${UI.icon('search')}<input class="inp" id="side-search" aria-label="搜索导航" placeholder="搜索流程或功能"></div><div class="side-body"><div class="side-label">工作区</div>${pages.map(([key,icon,label])=>`<a class="side-link ${key===active?'active':''}" href="${key}.html">${UI.icon(icon)}${label}</a>`).join('')}<div class="side-label">业务分组</div><a class="side-link" href="designer.html#expense">${UI.icon('folder')}费用管理<span class="count">1</span></a><a class="side-flow" href="designer.html#expense"><b>费用报销</b><small>EXPENSE · 钉钉式 · v3</small></a><a class="side-link" href="designer.html#purchase">${UI.icon('folder')}采购管理<span class="count">2</span></a><a class="side-flow" href="designer.html#purchase"><b>采购申请</b><small>PURCHASE_REQUEST · 钉钉式 · v2</small></a><a class="side-flow" href="designer.html#contract"><b>合同评审</b><small>CONTRACT_REVIEW · 子流程 · v1</small></a><a class="side-link" href="designer.html#quality">${UI.icon('folder')}质量管理<span class="count">1</span></a><a class="side-flow" href="designer.html#quality"><b>质量异常处理</b><small>QUALITY_EXCEPTION · 自由画布</small></a></div><div class="side-foot">4 个流程 · 3 个业务分组<a href="index.html">${UI.icon('cards')} 原型导航与评审清单</a></div></aside><main class="page-content" id="page-content"><div class="page-head"><div class="breadcrumb">审批服务 <span>/</span> ${pages.find(p=>p[0]===active)?.[2]||'原型导航'}</div><div class="title-row"><h1>${UI.escape(title)}</h1><div class="head-actions">${actionsHtml}</div></div><div class="page-subtitle">${subtitle}</div></div><div class="page-body">${bodyHtml}</div></main>`;
    document.querySelector('.mobile-menu').onclick = () => document.querySelector('.sidebar').classList.toggle('open');
    document.querySelector('#side-search').oninput = e => document.querySelectorAll('.side-link,.side-flow').forEach(link=>link.hidden = !link.textContent.toLowerCase().includes(e.target.value.toLowerCase()));
    document.querySelector('#profile-open').onclick = () => UI.modal('个人账号', `<div class="notice">当前为本地交互演示，所有数据均为虚构样例。</div><div class="form-grid" style="margin-top:18px"><label class="field">显示身份<select class="sel" id="demo-role"><option>系统管理员</option><option>流程管理员</option><option>审批人 · 李娜</option><option>申请人 · 王强</option></select></label><div class="field">个人设置<a class="btn" href="center.html#settings">委托、签名与常用语</a></div></div>`, d=>{ document.querySelector('#profile-open span:last-child').textContent=d.querySelector('#demo-role').value;UI.toast('演示身份已切换'); }, '切换身份');
  },
  /**
   * 显示短时状态消息。
   * @param {string} text 消息内容。
   * @returns {void} 无返回值。
   */
  toast(text) {
    let region=document.querySelector('.toast-region');
    if(!region){region=document.createElement('div');region.className='toast-region';region.setAttribute('role','status');region.setAttribute('aria-live','polite');document.body.append(region);}
    const message=document.createElement('div');message.className='toast';message.textContent=text;region.append(message);setTimeout(()=>message.remove(),3800);
  },
  /**
   * 打开带键盘焦点管理的原生对话框。
   * @param {string} title 对话框标题。
   * @param {string} bodyHtml 对话框内容。
   * @param {Function} onSave 确认后的回调，返回 false 保持打开。
   * @param {string} label 确认按钮文字。
   * @returns {HTMLDialogElement} 对话框元素。
   */
  modal(title, bodyHtml, onSave, label='保存') {
    UI.closeModal();
    const d=document.createElement('dialog');d.className='ui-dialog';d.setAttribute('aria-labelledby','ui-dialog-title');
    d.innerHTML=`<div class="dialog-head"><h2 id="ui-dialog-title">${UI.escape(title)}</h2><button class="icon-btn" data-close aria-label="关闭对话框">${UI.icon('close')}</button></div><div class="dialog-body">${bodyHtml}</div><div class="dialog-foot"><button class="btn" data-close>取消</button><button class="btn primary" data-confirm>${UI.escape(label)}</button></div>`;
    document.body.append(d);d.querySelectorAll('[data-close]').forEach(b=>b.onclick=()=>UI.closeModal());
    d.querySelector('[data-confirm]').onclick=async()=>{const invalid=[...d.querySelectorAll('input,select,textarea')].find(el=>!el.checkValidity());if(invalid){invalid.reportValidity();return;}if(!onSave||await onSave(d)!==false){if(d.isConnected){d.close();d.remove();}}};
    d.addEventListener('cancel',()=>{setTimeout(()=>d.remove(),0);});d.showModal();return d;
  },
  /**
   * 关闭当前对话框。
   * @returns {void} 无返回值。
   */
  closeModal() {document.querySelectorAll('.ui-dialog').forEach(d=>{d.close();d.remove();});}
};

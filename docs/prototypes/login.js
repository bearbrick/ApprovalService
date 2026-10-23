/**
 * 密码可见性演示，不保存输入值。
 * @returns {void} 无返回值。
 */
function togglePassword(){const input=document.querySelector('[name=password]');input.type=input.type==='password'?'text':'password';document.querySelector('#toggle-password').innerHTML=UI.icon(input.type==='password'?'eye':'eyeOff');document.querySelector('#toggle-password').setAttribute('aria-label',input.type==='password'?'显示密码':'隐藏密码');}
document.querySelector('#toggle-password').innerHTML=UI.icon('eye');document.querySelector('#toggle-password').onclick=togglePassword;
document.querySelector('#login-form').onsubmit=e=>{e.preventDefault();location.href='center.html';};
document.querySelectorAll('[data-role]').forEach(b=>b.onclick=()=>{location.href=b.dataset.role==='管理员'?'designer.html':'center.html';});
document.querySelector('#forgot-password').onclick=()=>UI.modal('找回账号访问权限','<p>请联系系统管理员重置密码。管理员可在“组织与账号”中管理账号状态。</p><p class="section-note">技术支持：信息部 · 分机 8001（演示信息）</p>',()=>{},'知道了');
document.querySelector('#sso').onclick=()=>UI.modal('统一登录预留','<div class="notice">当前初版使用独立账号。公司统一登录接入后，可通过此入口使用公司账号登录。</div>',()=>{},'知道了');
document.querySelector('#first-password').onclick=()=>UI.modal('首次登录 · 修改密码','<div class="notice">以下仅为交互演示，请勿填写真实密码。</div><div class="form-grid" style="margin-top:18px"><label class="field full">新密码<input id="new-password" class="inp" type="password" required minlength="8" autocomplete="off"></label><label class="field full">确认新密码<input id="confirm-password" class="inp" type="password" required minlength="8" autocomplete="off"></label><p id="password-error" class="field full" style="color:var(--danger)"></p></div>',d=>{if(d.querySelector('#new-password').value!==d.querySelector('#confirm-password').value){d.querySelector('#password-error').textContent='两次输入的密码不一致。';return false;}UI.toast('密码修改交互完成，未保存任何密码');},'完成演示');

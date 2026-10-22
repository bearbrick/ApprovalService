/**
 * 原型执行图检查、旧草稿转换及有限条件路径演示；不执行任意脚本。
 */
const DesignerGraph = {
  /**
   * 在草稿进入界面前检查必要形状，禁止标识破坏内联事件与选择器。
   * @param {unknown} source 待导入的草稿。
   * @returns {boolean} 是否具备可安全编辑的结构。
   */
  accepts(source) {return !!source&&typeof source==='object'&&Array.isArray(source.nodes)&&source.nodes.length>0&&source.nodes.every(n=>n&&typeof n.id==='string'&&/^[-A-Za-z0-9_]+$/.test(n.id)&&typeof n.name==='string'&&['start','end','approval','handle','cc','sub','auto','exclusive','inclusive','parallel','parallelJoin','inclusiveJoin'].includes(n.type))&&Array.isArray(source.edges)&&source.edges.every(e=>Array.isArray(e)&&e.length===3&&e.every(v=>typeof v==='string'))&&Array.isArray(source.fields)&&source.fields.every(f=>f&&typeof f.key==='string'&&typeof f.name==='string'&&typeof f.type==='string');},
  /**
   * 判断是否为默认路线。
   * @param {string} label 连线文字。
   * @returns {boolean} 是否默认路线。
   */
  isDefault(label) { return /^(其他情况|默认|default)(：|:|$)/i.test(String(label).trim()); },
  /**
   * 将旧草稿转换为显式画布图，保留字段与节点配置。
   * @param {object} source 草稿。
   * @returns {object} 转换后的独立草稿。
   */
  migrate(source) {
    const s=structuredClone(source);s.mode='canvas';s.settings=s.settings&&typeof s.settings==='object'&&!Array.isArray(s.settings)?s.settings:{};s.edges||=[];s.history=Array.isArray(s.history)?s.history:[];s.scenarios=Array.isArray(s.scenarios)?s.scenarios:[];
    s.nodes.forEach((n,i)=>{n.x=Number.isFinite(n.x)?n.x:80+i*230;n.y=Number.isFinite(n.y)?n.y:200;n.people||='';});
    if(s.schemaVersion===2)return s;
    // 旧版隐含在节点下的条件分叉改成独立网关，原来的路线和条件继续保留。
    for(const n of [...s.nodes]) {
      const outgoing=s.edges.filter(e=>e[0]===n.id);
      if(outgoing.length>1&&!['exclusive','inclusive','parallel'].includes(n.type)) {
        const id=n.id+'_gateway';s.nodes.push({id,name:'条件判断',type:'exclusive',x:n.x+210,y:n.y,people:'按条件选择路线'});
        outgoing.forEach(e=>e[0]=id);s.edges.push([n.id,id,'']);
      }
      if(n.branches?.length) {
        const targets=s.edges.filter(e=>e[0]===n.id);s.edges=s.edges.filter(e=>e[0]!==n.id);
        const joinId=n.id+'_join';s.nodes.push({id:joinId,name:'分支汇合',type:n.type==='inclusive'?'inclusiveJoin':n.type==='exclusive'?'exclusive':'parallelJoin',gatewayRole:'join',x:n.x+460,y:n.y,people:'等待分支完成',pairedGatewayId:n.id});n.pairedGatewayId=joinId;
        n.branches.forEach((b,i)=>{const id=n.id+'_branch_'+i;s.nodes.push({id,name:b.name,type:'approval',people:b.people,source:'指定成员',method:'依次审批',x:n.x+230,y:n.y+i*150});s.edges.push([n.id,id,n.type==='parallel'?'':b.condition||'其他情况'],[id,joinId,'']);});
        targets.forEach(e=>s.edges.push([joinId,e[1],e[2]]));delete n.branches;
      }
    }
    s.edges.forEach(e=>{e[2]=String(e[2]||'').replace(/金额\s*[≥]/,'amount >=').replace(/金额\s*[>]/,'amount >').replace(/,/g,'');if(this.isDefault(e[2]))e[2]='其他情况';if(e[2]==='需要返工')e[2]='rework == "需要返工"';});
    for(const type of ['parallel','inclusive']){const splits=s.nodes.filter(n=>n.type===type&&!n.pairedGatewayId),joins=s.nodes.filter(n=>n.type===type+'Join'&&!n.pairedGatewayId);if(splits.length===1&&joins.length===1){splits[0].pairedGatewayId=joins[0].id;joins[0].pairedGatewayId=splits[0].id;}}
    if(!s.nodes.some(n=>n.type==='end')){let id='end';while(s.nodes.some(n=>n.id===id))id+='Event';const leaf=s.nodes.filter(n=>!s.edges.some(e=>e[0]===n.id));s.nodes.push({id,name:'流程结束',type:'end',people:'',x:Math.max(...s.nodes.map(n=>n.x))+230,y:220});leaf.forEach(n=>s.edges.push([n.id,id,'']));}
    s.schemaVersion=2;s.migrationNote='旧草稿已转换为自由画布，请检查路线后保存。';return s;
  },
  /**
   * 检查结构、可达性、兜底分支及必需配置。
   * @param {object} state 当前草稿。
   * @returns {string[]} 可定位的问题说明。
   */
  validate(state) {
    const errors=[],nodes=state.nodes,edges=state.edges,ids=new Set(nodes.map(n=>n.id));
    const starts=nodes.filter(n=>n.type==='start'),ends=nodes.filter(n=>n.type==='end');
    if(starts.length!==1)errors.push('开始节点必须且只能有一个。');if(!ends.length)errors.push('至少需要一个结束节点。');
    if(ids.size!==nodes.length)errors.push('节点标识重复。');
    for(const e of edges){if(!ids.has(e[0])||!ids.has(e[1]))errors.push('存在悬空连线：'+e[0]+' → '+e[1]);if(e[0]===e[1])errors.push('节点不能直接连接自身：'+e[0]);}
    const edgeKeys=edges.map(e=>e[0]+'\0'+e[1]);if(new Set(edgeKeys).size!==edgeKeys.length)errors.push('同一对节点之间存在重复连线。');
    for(const n of nodes){
      const out=edges.filter(e=>e[0]===n.id),incoming=edges.filter(e=>e[1]===n.id);
      if(!String(n.name||'').trim())errors.push(n.id+'：节点名称不能为空。');
      if(['approval','handle','cc'].includes(n.type)&&!String(n.people||'').trim())errors.push(n.name+'：请配置处理人。');
      if(n.type==='start'&&incoming.length)errors.push(n.name+'：开始节点不能有入线。');
      if(n.type==='end'&&out.length)errors.push(n.name+'：结束节点不能有出线。');
      if(n.type!=='end'&&!out.length)errors.push(n.name+'：没有后续连线。');
      if(n.type!=='start'&&!incoming.length)errors.push(n.name+'：没有前序连线。');
      if(n.gatewayRole==='join'&&out.length!==1)errors.push(n.name+'：汇合网关必须只有一条出线。');
      if(['exclusive','inclusive'].includes(n.type)&&n.gatewayRole!=='join') {if(out.length<2)errors.push(n.name+'：至少需要两条分支。');if(out.filter(e=>this.isDefault(e[2])).length!==1)errors.push(n.name+'：必须且只能有一条默认分支。');if(out.some(e=>!this.isDefault(e[2])&&!String(e[2]).trim()))errors.push(n.name+'：非默认分支必须填写条件。');}
      if(n.type==='parallel'&&out.length<2)errors.push(n.name+'：并行分叉至少需要两条出线。');
      if(['parallelJoin','inclusiveJoin'].includes(n.type)&&incoming.length<2)errors.push(n.name+'：汇合至少需要两条入线。');
      if(out.length>1&&!['exclusive','inclusive','parallel'].includes(n.type))errors.push(n.name+'：多条出线应使用分支网关。');
      if(n.pairedGatewayId&&!ids.has(n.pairedGatewayId))errors.push(n.name+'：配对网关不存在。');
      const pairTypes={parallel:'parallelJoin',parallelJoin:'parallel',inclusive:'inclusiveJoin',inclusiveJoin:'inclusive'};
      if(pairTypes[n.type]){const pair=nodes.find(a=>a.id===n.pairedGatewayId);if(!pair||pair.type!==pairTypes[n.type]||pair.pairedGatewayId!==n.id)errors.push(n.name+'：必须与对应的分叉 / 汇合网关双向配对。');}
      if(n.method==='票签'&&(!(Number(n.ratio??67)>0)||Number(n.ratio??67)>100))errors.push(n.name+'：票签比例必须在 1～100 之间。');
    }
    const walk=(roots,reverse=false)=>{const visited=new Set(roots),queue=[...roots];while(queue.length){const id=queue.shift();edges.filter(e=>e[reverse?1:0]===id).forEach(e=>{const target=e[reverse?0:1];if(!visited.has(target)){visited.add(target);queue.push(target);}});}return visited;};
    const reachable=walk(starts.map(n=>n.id)),canEnd=walk(ends.map(n=>n.id),true);
    nodes.forEach(n=>{if(!reachable.has(n.id))errors.push(n.name+'：无法从开始节点到达。');if(!canEnd.has(n.id))errors.push(n.name+'：无法到达结束节点。');});
    const fields=state.fields||[];if(fields.some(f=>!String(f.key||'').trim()||!String(f.name||'').trim()))errors.push('字段标识与名称不能为空。');if(new Set(fields.map(f=>f.key)).size!==fields.length)errors.push('存在重复字段标识。');
    for(const n of nodes.filter(a=>['exclusive','inclusive'].includes(a.type)&&a.gatewayRole!=='join')){for(const e of edges.filter(a=>a[0]===n.id&&!this.isDefault(a[2]))){const match=String(e[2]).match(/^([A-Za-z_]\w*)\s*[<>!=]/);if(match&&!fields.some(f=>f.key===match[1]))errors.push(n.name+'：条件引用的字段 '+match[1]+' 不存在。');}}
    return [...new Set(errors)];
  },
  /**
   * 安全计算单一字段比较，不解释任意 JavaScript。
   * @param {string} expression 条件文字。
   * @param {object} data 测试数据。
   * @returns {boolean|null} 条件结果，无法解析时为 null。
   */
  evaluate(expression,data) {
    const match=String(expression).trim().match(/^([A-Za-z_]\w*)\s*(>=|<=|==|!=|>|<|=)\s*(.+)$/);if(!match||!(match[1] in data))return null;
    const raw=match[3].trim();let value;if(raw==='true'||raw==='false')value=raw==='true';else if(/^[-+]?\d+(\.\d+)?$/.test(raw))value=Number(raw);else if(/^(['"]).*\1$/.test(raw))value=raw.slice(1,-1);else return null;
    const actual=data[match[1]];return ({'>=':()=>actual>=value,'<=':()=>actual<=value,'>':()=>actual>value,'<':()=>actual<value,'==':()=>actual===value,'=':()=>actual===value,'!=':()=>actual!==value}[match[2]])();
  },
  /**
   * 按真实连线推演处理批次；遇到未知表达式或再次进入回路时停止并解释。
   * @param {object} state 当前执行图。
   * @param {object} data 测试数据。
   * @returns {object} 批次、提示与结束标记。
   */
  trace(state,data) {
    const issues=this.validate(state);if(issues.length)return {waves:[],notes:issues,complete:false};
    const byId=new Map(state.nodes.map(n=>[n.id,n])),visits=new Map(),waiting=new Map(),expected=new Map(),waves=[],notes=[];
    let active=state.nodes.filter(n=>n.type==='start').map(n=>({id:n.id,from:''})),ended=false;
    for(let round=0;active.length&&round<40;round++) {
      const next=[],wave=[];
      for(const token of active) {
        const n=byId.get(token.id);if(!n)continue;
        if(['parallelJoin','inclusiveJoin'].includes(n.type)) {const arrivals=waiting.get(n.id)||new Set();arrivals.add(token.from);waiting.set(n.id,arrivals);const count=expected.get(n.id)||state.edges.filter(e=>e[1]===n.id).length;if(arrivals.size<count)continue;if(visits.has(n.id))continue;waiting.delete(n.id);}
        const seen=visits.get(n.id)||0;if(seen){notes.push('已沿连线回到“'+n.name+'”。回路需要新的业务处理结果，演示在此暂停，不自动判定通过。');continue;}visits.set(n.id,seen+1);wave.push(n.id);
        if(n.type==='end'){ended=true;continue;}
        let out=state.edges.filter(e=>e[0]===n.id);
        if(['exclusive','inclusive'].includes(n.type)&&n.gatewayRole!=='join') {
          const conditions=out.filter(e=>!this.isDefault(e[2])),values=conditions.map(e=>this.evaluate(e[2],data));
          if(values.some(v=>v===null)){notes.push(n.name+'：条件超出演示的单字段比较范围，请在正式引擎中验证。');continue;}
          const matched=conditions.filter((e,i)=>values[i]);out=matched.length?(n.type==='exclusive'?matched.slice(0,1):matched):out.filter(e=>this.isDefault(e[2]));
        }
        if(n.pairedGatewayId&&['parallel','inclusive'].includes(n.type))expected.set(n.pairedGatewayId,out.length);
        out.forEach(e=>next.push({id:e[1],from:n.id}));
      }
      if(wave.length)waves.push(wave);active=next;
    }
    if(active.length)notes.push('超过 40 个处理批次，演示暂停。');if(waiting.size)notes.push('存在尚未收齐分支的汇合节点，未到达完整结束状态。');
    return {waves,notes,complete:ended&&!notes.length};
  }
};

/**
 * 四套相互独立的虚构流程模板，分别覆盖树形、并行、会签与回路。
 */
const DesignerTemplates = {
  /**
   * 创建指定流程的初始草稿。
   * @param {string} key 流程路由。
   * @param {object} base 基础采购样例。
   * @returns {object} 独立流程草稿。
   */
  create(key,base) {
    const state=structuredClone(base);
    const meta={purchase:['采购申请','PURCHASE_REQUEST','采购管理',2],expense:['费用报销','EXPENSE','费用管理',3],contract:['合同评审','CONTRACT_REVIEW','采购管理',1],quality:['质量异常处理','QUALITY_EXCEPTION','质量管理',1]}[key];
    [state.name,state.code,state.category,state.version]=meta;
    state.owner=key==='expense'?'赵敏':'吴芳';state.flowKey=key;
    state.history=[{version:state.version,date:'2026-10-12 16:20',note:'当前发布版本，供业务系统调用。'}];
    state.settings.company='智云科技有限公司';
    state.nodes[0].people='ERP 采购系统';state.nodes[1].people='张明 · 部门负责人';state.nodes[2].people='周总、冯伟';state.nodes[3].people='吴芳 · 采购经理';state.nodes[4].people='吴芳、郑涛';
    if(key==='expense'){
      state.threshold=20000;
      state.nodes[0].people='费用报销系统';state.nodes[1].name='直属上级 / 部门负责人';state.nodes[1].source='连续多级上级';state.nodes[1].people='李娜 → 张明 · 相邻去重';state.nodes[2].name='总经理审批';state.nodes[2].people='周总';state.nodes[2].method='依次审批';state.nodes[3].name='财务审核';state.nodes[3].type='approval';state.nodes[3].people='赵敏、钱丽';state.nodes[3].method='或签';state.nodes[4].name='出纳付款';state.nodes[4].type='handle';state.nodes[4].people='刘洋';
      state.fields.find(f=>f.key==='amount').name='报销金额';state.fields.find(f=>f.key==='purpose').name='报销事由';state.fields.find(f=>f.key==='items').name='费用明细';state.fields.push({key:'project',name:'所属项目',type:'文本'},{key:'paymentDate',name:'付款日期',type:'日期',source:'审批填写'});
    }
    if(key==='contract'){
      state.linear=true;
      state.nodes=[{id:'start',name:'主流程调用 / 业务系统提交',type:'start',source:'发起人本人',people:'ERP 采购系统',method:'依次审批',x:40,y:220},{id:'department',name:'合同资料核对',type:'approval',source:'角色',people:'吴芳 · 采购经理',method:'依次审批',x:240,y:220},{id:'management',name:'法务与财务并行评审',type:'parallel',people:'全部分支完成后继续',method:'会签',x:450,y:220,branches:[{name:'法务评审',people:'法务专员',condition:''},{name:'财务评审',people:'何静 · 财务经理',condition:''}]},{id:'finance',name:'合同终审',type:'approval',source:'角色',people:'周总',method:'依次审批',x:650,y:220},{id:'cc',name:'通知父流程 / 抄送采购部',type:'cc',source:'指定成员',people:'吴芳',method:'依次审批',x:650,y:420}];
      state.fields=[{key:'contractName',name:'合同名称',type:'文本',required:true,summary:true},{key:'supplier',name:'签约单位',type:'文本',required:true,summary:true},{key:'amount',name:'合同金额',type:'金额',required:true,summary:true},{key:'department',name:'责任部门',type:'部门',required:true},{key:'attachments',name:'合同正文与附件',type:'附件',required:true},{key:'parentBusinessKey',name:'父流程业务单号',type:'文本'}];
      state.edges=[['start','department',''],['department','management',''],['management','finance','全部评审完成'],['finance','cc','']];
    }
    if(key==='quality'){
      state.mode='canvas';state.linear=true;
      state.nodes=[{id:'start',name:'异常单提交',type:'start',source:'发起人本人',people:'质量管理系统',method:'依次审批',x:20,y:300},{id:'department',name:'质检员确认',type:'approval',source:'角色',people:'质量专员',method:'依次审批',x:215,y:300},{id:'management',name:'是否需要返工',type:'exclusive',people:'按检验结果选择一条路线',method:'依次审批',x:410,y:300},{id:'finance',name:'生产返工',type:'handle',source:'角色',people:'生产负责人',method:'依次审批',x:410,y:70},{id:'cc',name:'质量经理审批',type:'approval',source:'角色',people:'质量经理',method:'依次审批',x:820,y:160},{id:'production',name:'生产经理审批',type:'approval',source:'角色',people:'张明',method:'依次审批',x:820,y:420},{id:'parallelSplit',name:'并行分叉',type:'parallel',people:'质量、生产两条路线同时执行',method:'依次审批',x:615,y:300},{id:'parallelJoin',name:'并行汇合',type:'parallelJoin',people:'等待质量、生产两方完成',method:'依次审批',x:1020,y:300},{id:'end',name:'结果归档',type:'auto',people:'质量系统 · 归档结果',method:'依次审批',x:1020,y:545}];
      state.edges=[['start','department',''],['department','management',''],['management','finance','需要返工'],['finance','department','返工后重新确认'],['management','parallelSplit','其他情况：无需返工'],['parallelSplit','cc',''],['parallelSplit','production',''],['cc','parallelJoin',''],['production','parallelJoin',''],['parallelJoin','end','全部分支完成']];
      const qualityOrder=['start','department','management','finance','parallelSplit','cc','production','parallelJoin','end'];
      state.nodes.sort((a,b)=>qualityOrder.indexOf(a.id)-qualityOrder.indexOf(b.id));
      state.fields=[{key:'defect',name:'异常描述',type:'文本',required:true,summary:true},{key:'batch',name:'产品批次',type:'文本',required:true,summary:true},{key:'department',name:'责任部门',type:'部门',required:true,summary:true},{key:'rework',name:'需要返工',type:'文本',required:true},{key:'attachments',name:'检验报告',type:'附件'},{key:'result',name:'处理结论',type:'文本',source:'审批填写'}];
    }
    state.settings.printTitle=state.name+'审批单';
    return state;
  }
};

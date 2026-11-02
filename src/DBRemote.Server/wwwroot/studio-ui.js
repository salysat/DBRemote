(function(){
 const $=id=>document.getElementById(id);
 function modal(title,body){$('infoTitle').textContent=title;$('infoBody').textContent=body;$('infoOverlay').classList.add('visible')}
 $('closeInfo').onclick=()=>$('infoOverlay').classList.remove('visible');
 $('helpBtn').onclick=()=>modal('دليل الاستخدام','ثبّت Branch Agent على الكمبيوتر المصرّح به، ووافق على طلب تسجيله، ثم افتح جهاز الفرع من بطاقته. اتصال الشاشة والملفات والمحادثة يحتاج جلسة تحكم نشطة.');
 $('addDevice').onclick=()=>modal('إضافة جهاز فرع','انشر برنامج DBRemote.BranchAgent على جهاز الفرع واضبط عنوان سيرفر الإدارة. سيظهر طلب تسجيل في قائمة الأجهزة بعد تشغيله. لا يحتاج إدخال مفتاح الإدارة على الفرع.');
 document.querySelectorAll('.nav[data-section]').forEach(b=>b.addEventListener('click',()=>{
  document.querySelectorAll('.nav').forEach(x=>x.classList.remove('active'));b.classList.add('active');const s=b.dataset.section;
  if(s==='devices'){$('pageTitle').textContent='الوصول الدائم';$('catalog').scrollIntoView({behavior:'smooth'})}
  else if(s==='workspace'){$('pageTitle').textContent='الدعم السريع';$('workspace').classList.add('visible');$('workspace').scrollIntoView({behavior:'smooth'})}
  else if(s==='history'){$('workspace').classList.add('visible');document.querySelector('[data-tab=activity]').click();$('workspace').scrollIntoView({behavior:'smooth'})}
  else if(s==='settings')modal('إعدادات مساحة العمل','تُحفظ تفضيلات معدل تحديث الشاشة والتحديث التلقائي محليًا. إعدادات السيرفر والمصادقة تُدار على الخادم، والهوية الخاصة بكل Agent محفوظة على جهازه.');
  else if(s==='branding')modal('الهوية التجارية','الواجهة تحمل هوية DB Solutions. تخصيص الشعار والخلفيات لكل فرع غير مُنفَّذ بعد.');
  else if(s==='integrations')modal('التكاملات','لم تُنفذ تكاملات الطرف الثالث بعد. لا توجد وصلات تعمل من هذا القسم حاليًا.');
  else if(s==='docs')modal('الوثائق','ابدأ من إضافة جهاز ثم اعتماده، واستخدم اتصال لفتح الشاشة. يمكن سحب الملفات إلى تبويب الملفات أثناء الجلسة.');
  else if(s==='about')modal('ما الجديد؟','واجهة إدارة أجهزة ببطاقات ومعاينات جلسة نشطة، بحث وتصفية، عرض قائمة أو شبكة، وأدوات تحكم ومحادثة وملفات في مساحة واحدة.');
 }));
 document.querySelectorAll('.utilitytabs [data-tab]').forEach(b=>b.onclick=()=>{document.querySelectorAll('.utilitytabs button').forEach(x=>x.classList.remove('active'));b.classList.add('active');document.querySelectorAll('.utilpane').forEach(x=>x.classList.remove('active'));$(b.dataset.tab+'Pane').classList.add('active')});
 $('closeWorkspace').onclick=()=>{$('workspace').classList.remove('visible');$('catalog').scrollIntoView({behavior:'smooth'})};
 const apply=()=>{const q=$('searchDevices').value.toLowerCase().trim(),filter=$('statusFilter').value;document.querySelectorAll('.devicecard').forEach(c=>{c.hidden=!(c.dataset.search.includes(q)&&(filter==='all'||c.dataset.status===filter))});};window.applyDeviceFilters=apply;
 $('searchDevices').addEventListener('input',apply);$('statusFilter').addEventListener('change',apply);
 $('gridMode').onclick=()=>{$('devicesList').classList.remove('list');$('gridMode').classList.add('active');$('listMode').classList.remove('active')};
 $('listMode').onclick=()=>{$('devicesList').classList.add('list');$('listMode').classList.add('active');$('gridMode').classList.remove('active')};
})();

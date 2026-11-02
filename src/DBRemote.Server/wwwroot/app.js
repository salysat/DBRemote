const el=id=>document.getElementById(id),log=s=>{el('log').textContent=`${new Date().toLocaleTimeString()} ${s}\n`+el('log').textContent.slice(0,3000)};
let loggedIn=false,ws=null,canControl=true,mouseDown=false,downloads=new Map(), lastMove=0;
let pendingDeviceId=null;
let connectingDeviceId=null;
const send=data=>{if(ws?.readyState===WebSocket.OPEN){ws.send(JSON.stringify(data));return true}return false};
function ready(yes){el('connect').disabled=yes;el('disconnect').disabled=!yes;el('status').textContent=yes?'متصل بجهاز':'غير متصل بجهاز';el('status').classList.toggle('online',yes);el('metricSession').textContent=yes?el('device').value:'لا توجد';if(!yes){connectingDeviceId=null;const pic=el('miniActiveScreen');if(pic){pic.hidden=true;pic.removeAttribute('src')}}}
function pageName(){return document.body?.dataset?.page||''}
function isWorkspaceLike(){return ['workspace','remote'].includes(pageName())}
function setSelectedDevice(id){ if(el('device')) el('device').value=id; pendingDeviceId=id; try{sessionStorage.setItem('dbremote_target_device',id)}catch{} }
async function connectToDevice(id){ if(!id) return; setSelectedDevice(id); if(!loggedIn){ showLogin(); return; } if(ws?.readyState===WebSocket.OPEN && connectingDeviceId===id){ const remote=document.getElementById('remote'); remote?.scrollIntoView({behavior:'smooth',block:'start'}); return; } if(el('connect')) el('connect').click(); const remote=document.getElementById('remote'); remote?.scrollIntoView({behavior:'smooth',block:'start'}); }
function processPendingDevice(){ try{ pendingDeviceId = pendingDeviceId || sessionStorage.getItem('dbremote_target_device'); }catch{} if(!pendingDeviceId) return; if(el('device')) el('device').value=pendingDeviceId; if(loggedIn && isWorkspaceLike()) { const id=pendingDeviceId; el('workspace')?.classList.add('visible'); try{sessionStorage.removeItem('dbremote_target_device')}catch{} pendingDeviceId=null; setTimeout(()=>connectToDevice(id),120); } }
function appendChat(side,text,meta=''){ const list=el('chatMessages'); if(!list) return; const row=document.createElement('div'); row.className='chatmsg '+side; const bubble=document.createElement('div'); bubble.className='bubble'; bubble.textContent=text; const small=document.createElement('div'); small.className='meta'; small.textContent=(side==='out'?'أنت':'الفرع') + (meta? ' · '+meta:''); row.append(bubble,small); list.append(row); list.scrollTop=list.scrollHeight; }
el('connect').onclick=async()=>{if(ws)ws.close();let d=el('device').value.trim();connectingDeviceId=d;if(!/^[a-zA-Z0-9_-]{3,64}$/.test(d)){log('اختر جهازًا مسجلًا');return}try{let r=await fetch('/api/viewer-ticket/'+encodeURIComponent(d),{method:'POST',headers:{'X-DBRemote-Request':'1'}});if(!r.ok){log('سجّل الدخول أولًا لفتح الشاشة: '+r.status);showLogin();return}let {ticket}=await r.json();ws=new WebSocket(`${location.protocol==='https:'?'wss':'ws'}://${location.host}/ws/viewer/${encodeURIComponent(d)}`);ws.onopen=()=>ws.send(JSON.stringify({token:ticket}));ws.onclose=()=>{ready(false);log('انقطع الاتصال')};ws.onerror=()=>log('خطأ في الاتصال');ws.onmessage=async ev=>{let m;try{m=JSON.parse(ev.data)}catch{return}if(m.type==='connected'){ready(true);log('تم الاتصال');send({type:'screen-config',fps:Math.min(10,Math.max(1,Number(el('fps').value)||5))})}else if(m.type==='frame'){el('screen').src='data:image/jpeg;base64,'+m.data;el('screen').style.display='block';el('hint').style.display='none';const mini=el('miniActiveScreen');if(mini){mini.src=el('screen').src;mini.hidden=false};const mp=el('miniPlaceholder');if(mp)mp.hidden=true}else if(m.type==='clipboard'){el('clipboard').value=m.text||'';log('تم جلب الحافظة')}else if(m.type==='file-start'){downloads.set(m.id,{name:m.name,chunks:[]});log('جاري استقبال '+m.name)}else if(m.type==='file-chunk'){let f=downloads.get(m.id);if(f)f.chunks.push(m.data)}else if(m.type==='file-end'){let f=downloads.get(m.id);if(f){let bs=f.chunks.map(c=>Uint8Array.from(atob(c),v=>v.charCodeAt(0))),blob=new Blob(bs),a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=f.name;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),15000);downloads.delete(m.id);log('تم استقبال '+f.name); if(el('transferstate')) el('transferstate').textContent='تم الاستقبال: '+f.name}}else if(m.type==='chat'){appendChat('in',m.text||'',m.from||'')}else if(m.type==='error'||m.type==='status'){log(m.message||m.type)}}}catch(e){log('خطأ الاتصال: '+e.message)}};
el('disconnect').onclick=()=>ws?.close();el('control').onclick=()=>{canControl=!canControl;el('control').textContent='التحكم: '+(canControl?'مفعل':'معطل')};el('fullscreen').onclick=()=>el('view').requestFullscreen?.();
const coords=e=>{let r=el('screen').getBoundingClientRect();return {x:Math.max(0,Math.min(1,(e.clientX-r.left)/r.width)),y:Math.max(0,Math.min(1,(e.clientY-r.top)/r.height))}};
const view=el('view');view.addEventListener('pointerdown',e=>{if(!canControl)return;view.focus();view.setPointerCapture(e.pointerId);mouseDown=true;send({type:'mouse',action:'down',button:e.button,...coords(e)});e.preventDefault()});view.addEventListener('pointerup',e=>{if(!canControl)return;mouseDown=false;send({type:'mouse',action:'up',button:e.button,...coords(e)});e.preventDefault()});view.addEventListener('pointermove',e=>{if(!canControl)return;let now=Date.now();if(now-lastMove<35)return;lastMove=now;send({type:'mouse',action:'move',...coords(e)})});view.addEventListener('wheel',e=>{if(!canControl)return;send({type:'mouse',action:'wheel',delta:Math.sign(e.deltaY)*-120,...coords(e)});e.preventDefault()},{passive:false});view.addEventListener('contextmenu',e=>e.preventDefault());
view.addEventListener('keydown',e=>{if(!canControl)return;send({type:'key',action:'down',code:e.keyCode});if(!['F5','F12'].includes(e.key))e.preventDefault()});view.addEventListener('keyup',e=>{if(canControl)send({type:'key',action:'up',code:e.keyCode});e.preventDefault()});
el('sendtext').onclick=()=>{send({type:'text',text:el('typebox').value});el('typebox').value=''};
el('getclip').onclick=()=>send({type:'clipboard-get'});el('setclip').onclick=()=>send({type:'clipboard-set',text:el('clipboard').value});
el('upload').onclick=async()=>{const chosen=[...(el('file')?.files||[])]; if(!chosen.length){log('اختر ملفًا أو اسحبه إلى منطقة الإرسال'); return;} for(const f of chosen){let id=crypto.randomUUID(),size=24*1024;if(el('transferstate')) el('transferstate').textContent='جاري الإرسال: '+f.name; if(!send({type:'file-start',id,name:f.name,size:f.size}))return;for(let pos=0;pos<f.size;pos+=size){let bytes=new Uint8Array(await f.slice(pos,pos+size).arrayBuffer()),str='';for(let b of bytes)str+=String.fromCharCode(b);send({type:'file-chunk',id,data:btoa(str)});el('progress').value=Math.round(100*Math.min(pos+size,f.size)/f.size);while(ws?.bufferedAmount>512*1024)await new Promise(r=>setTimeout(r,25))}send({type:'file-end',id});log('تم إرسال '+f.name+' (راجع الجهاز المستلم)')} if(el('transferstate')) el('transferstate').textContent='اكتمل الإرسال';};

el('chatInput')?.addEventListener('keydown',e=>{if(e.key==='Enter'&&!e.shiftKey){e.preventDefault();el('sendChat')?.click()}});
const sendChatBtn=el('sendChat'); if(sendChatBtn) sendChatBtn.onclick=()=>{ const input=el('chatInput'); const text=input?.value.trim(); if(!text){ return; } if(!send({type:'chat',text})){ log('اتصل بجهاز أولًا'); return; } appendChat('out',text); input.value=''; };
const dz=el('dropzone'); if(dz){ dz.addEventListener('click',()=>el('file')?.click()); ['dragenter','dragover'].forEach(ev=>dz.addEventListener(ev,e=>{e.preventDefault();dz.classList.add('drag')})); ['dragleave','dragend','drop'].forEach(ev=>dz.addEventListener(ev,e=>{e.preventDefault();dz.classList.remove('drag')})); dz.addEventListener('drop',e=>{ const files=e.dataTransfer?.files; if(!files?.length) return; el('file').files=files; dz.querySelector('.droptext')?.replaceChildren(document.createTextNode(files.length+' ملف جاهز للإرسال')); el('upload')?.click(); }); }
async function deviceApi(path,method='GET') {
  if(!loggedIn){showLogin();return null}
  const response=await fetch(path,{method,headers:method==='GET'?{}:{'X-DBRemote-Request':'1'},cache:'no-store'});
  if(response.status===401){loggedIn=false;showLogin();return null}
  if(!response.ok){log('تعذر تنفيذ الطلب: '+response.status);return null}
  return await response.json();
}
async function refreshDevices(){
 try {
  const items=await deviceApi('/api/devices');if(!items)return;
  const list=el('devicesList');list.replaceChildren();
  el('metricAll').textContent=items.length;el('metricOnline').textContent=items.filter(d=>d.online&&d.status==='approved').length;el('metricPending').textContent=items.filter(d=>d.status==='pending').length;el('deviceCount').textContent=items.length+' أجهزة';if(!items.length){list.innerHTML='<div class=empty>لا توجد أجهزة بعد. افتح Agent على جهاز الفرع لإنشاء طلب تسجيل.</div>';return}
  for(const d of items){
   const box=document.createElement('article'); box.className='devicecard';box.dataset.status=d.status==='pending'?'pending':d.status==='revoked'?'revoked':d.online?'online':'offline';box.dataset.search=(d.name+' '+d.id).toLowerCase();
   const head=document.createElement('div');head.className='cardhead';const state=document.createElement('span');state.textContent=d.status==='pending'?'● بانتظار الموافقة':d.status==='revoked'?'● ملغي':d.online?'● Online':'● Offline';state.style.color=d.online?'#239c74':'#8796a6';const tools=document.createElement('span');tools.className='cardtools';const star=document.createElement('button');star.textContent='☆';star.title='إضافة للمفضلة';star.onclick=()=>{const k='dbremote_fav_'+d.id;const val=localStorage.getItem(k)!=='1';localStorage.setItem(k,val?'1':'0');star.textContent=val?'★':'☆'};star.textContent=localStorage.getItem('dbremote_fav_'+d.id)==='1'?'★':'☆';tools.append(star);head.append(state,tools);box.append(head);
   const preview=document.createElement('div');preview.className='preview-placeholder';preview.textContent=d.online?'الشاشة المباشرة عند فتح الاتصال':'جهاز غير متصل';if(d.id===connectingDeviceId&&el('screen')?.src?.startsWith('data:image/')){const img=document.createElement('img');img.className='devicepreview';img.src=el('screen').src;box.append(img)}else if(d.online && d.status==='approved'){
     const img=document.createElement('img');img.className='devicepreview';img.alt='معاينة الجهاز بعد موافقته';img.loading='lazy';img.src='/api/devices/'+encodeURIComponent(d.id)+'/preview?v='+Math.floor(Date.now()/15000);
     img.onerror=()=>{img.remove();box.insertBefore(preview,info??null)};
     box.append(img);
   }else box.append(preview);
   const info=document.createElement('div');info.className='cardinfo';const name=document.createElement('strong');name.textContent=d.name;const sub=document.createElement('div');sub.className='meta';sub.textContent=d.id+' · '+(d.status==='approved'?'جهاز معتمد':d.status==='pending'?'موافقة مطلوبة':'اعتماد ملغي');info.append(name,sub);box.append(info);
   const actions=document.createElement('div');actions.className='cardbottom';const act=document.createElement('button');const more=document.createElement('button');more.className='more';more.textContent='⋮';more.title='إجراءات الجهاز';more.onclick=()=>{const action=confirm('هل تريد إلغاء اعتماد الجهاز '+d.id+'؟');if(action&&d.status==='approved')deviceApi('/api/devices/'+encodeURIComponent(d.id)+'/revoke','POST').then(refreshDevices)};
   if(d.status==='pending'){act.textContent='✓ قبول الجهاز';act.onclick=async()=>{if(confirm('اعتماد الجهاز '+d.id+'؟')){await deviceApi('/api/devices/'+encodeURIComponent(d.id)+'/approve','POST');refreshDevices()}}}
   else if(d.status==='approved'){act.textContent='▣ اتصال';act.disabled=!d.online;act.onclick=()=>{el('workspace').classList.add('visible');el('selectedLabel').textContent='الاتصال بـ '+d.name;setSelectedDevice(d.id);connectToDevice(d.id);el('workspace').scrollIntoView({behavior:'smooth',block:'start'})}}
   else {act.textContent='بانتظار إعادة التسجيل';act.disabled=true}
   actions.append(act,more);box.append(actions);list.append(box);
  }
  if(typeof window.applyDeviceFilters==='function')window.applyDeviceFilters();
  const needle=el('searchDevices')?.value.trim().toLowerCase();if(needle)for(const card of list.querySelectorAll('.devicecard'))card.style.display=card.textContent.toLowerCase().includes(needle)?'':'none';
 }catch(e){log('تعذر تحميل الأجهزة: '+e.message)}
}
el('refreshDevices').onclick=refreshDevices;


el('loadDevices').onclick=refreshDevices;
el('toggleToken').onclick=()=>showLogin();
el('clearLog').onclick=()=>{el('log').textContent=''};
el('themeToggle').onclick=()=>{document.body.dataset.theme=document.body.dataset.theme==='light'?'dark':'light'};

setInterval(()=>{if(el('autoRefresh').checked && loggedIn)refreshDevices()},15000);

processPendingDevice();

// Server-owned login sessions: no administrator token in browser storage.
function showLogin(){document.getElementById('authOverlay').style.display='grid'}
function hideLogin(){document.getElementById('authOverlay').style.display='none'}
async function initializeAuth(){try{let r=await fetch('/api/auth/me',{cache:'no-store'});loggedIn=r.ok;if(loggedIn){hideLogin();await refreshDevices();processPendingDevice()}else showLogin()}catch{showLogin()}}
document.getElementById('authForm').onsubmit=async e=>{e.preventDefault();const pwd=document.getElementById('authPassword').value;try{let r=await fetch('/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json','X-DBRemote-Request':'1'},body:JSON.stringify({password:pwd})});if(!r.ok){document.getElementById('authError').textContent=r.status===429?'محاولات كثيرة؛ حاول بعد 10 دقائق':'كلمة المرور غير صحيحة ('+r.status+')';return}document.getElementById('authPassword').value='';loggedIn=true;hideLogin();await refreshDevices();processPendingDevice();}catch{document.getElementById('authError').textContent='تعذر الاتصال بالسيرفر'}};
document.getElementById('logoutBtn').onclick=async()=>{await fetch('/api/auth/logout',{method:'POST',headers:{'X-DBRemote-Request':'1'}});loggedIn=false;ws?.close();showLogin()};
// User-interface preferences only; no authentication secrets stored here.
try { const pref=JSON.parse(localStorage.getItem('dbremote_ui_prefs')||'{}'); if(el('fps') && pref.fps) el('fps').value=String(pref.fps); if(el('autoRefresh') && typeof pref.autoRefresh==='boolean') el('autoRefresh').checked=pref.autoRefresh; } catch {}
function storeUiPrefs(){try{localStorage.setItem('dbremote_ui_prefs',JSON.stringify({fps:Number(el('fps')?.value||5),autoRefresh:!!el('autoRefresh')?.checked}))}catch{}}
el('fps')?.addEventListener('change',storeUiPrefs);el('autoRefresh')?.addEventListener('change',storeUiPrefs);
initializeAuth();

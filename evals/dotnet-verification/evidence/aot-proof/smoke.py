from pathlib import Path
import json,re,subprocess,time,urllib.request,urllib.error
root=Path(__file__).parent
exe=root/'bin/Release/net10.0/linux-x64/publish/AotProof'
assert exe.read_bytes()[:4]==b'\x7fELF', 'not native ELF'
checks=[]
log=root/'native.log'
with log.open('w') as stream:
 proc=subprocess.Popen([str(exe),'--urls','http://127.0.0.1:0'],stdout=stream,stderr=subprocess.STDOUT,cwd=root)
 try:
  deadline=time.monotonic()+20
  url=None
  while time.monotonic()<deadline:
   text=log.read_text()
   match=re.search(r'Now listening on: (http://127\.0\.0\.1:\d+)',text)
   if match:url=match.group(1);break
   if proc.poll() is not None:raise RuntimeError(text)
   time.sleep(.05)
  assert url,log.read_text()
  def call(path,auth=False,body=None):
   headers={'Authorization':'Proof allowed'} if auth else {}
   if body is not None:headers['Content-Type']='application/json'
   req=urllib.request.Request(url+path,headers=headers,data=json.dumps(body).encode() if body is not None else None)
   try:
    with urllib.request.urlopen(req,timeout=5) as res:return res.status,res.read(),res.headers
   except urllib.error.HTTPError as res:return res.code,res.read(),res.headers
  status,body,_=call('/api/orders/')
  assert status==401,(status,body)
  checks.append('RequireAuthorization rejects anonymous request: 401')
  status,body,_=call('/api/orders/',True)
  assert status==200 and json.loads(body)[0]['name']=='example',(status,body)
  checks.append('Mapped collection endpoint uses generated JSON metadata: 200')
  status,body,_=call('/api/orders/11111111-1111-1111-1111-111111111111',True)
  assert status==200 and json.loads(body)['name']=='example',(status,body)
  checks.append('Documented typed service-injected endpoint: 200')
  status,body,_=call('/api/orders/22222222-2222-2222-2222-222222222222',True)
  assert status==404,(status,body)
  checks.append('Documented typed not-found branch: 404')
  status,body,headers=call('/api/orders/',True,{'name':'native'})
  assert status==201 and json.loads(body)['name']=='native' and headers['Location'].startswith('/api/orders/'),(status,body)
  checks.append('Mapped create endpoint binds source-generated request and emits response: 201')
  assert 'JSON context camelCase check passed:' in log.read_text()
  checks.append('Original csharp JsonSerializerContext and ConfigureHttpJsonOptions work in native binary')
  print('\n'.join(checks))
  print(f'{len(checks)} native smoke assertions passed')
 finally:
  proc.terminate()
  try:proc.wait(timeout=10)
  except subprocess.TimeoutExpired:proc.kill();proc.wait();raise

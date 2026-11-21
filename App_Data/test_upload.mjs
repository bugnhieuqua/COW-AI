import fs from 'fs';

async function run() {
  const loginRes = await fetch('http://localhost:5223/Account/Login');
  const loginHtml = await loginRes.text();
  const cookies = loginRes.headers.get('set-cookie') || '';
  
  const tokenMatch = loginHtml.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  const token = tokenMatch ? tokenMatch[1] : '';
  console.log('Token:', !!token);

  const form = new URLSearchParams();
  form.append('Username', 'admin');
  form.append('Password', 'AdminPassword@123');
  form.append('__RequestVerificationToken', token);

  const authRes = await fetch('http://localhost:5223/Account/Login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Cookie': cookies
    },
    body: form.toString(),
    redirect: 'manual'
  });
  
  const authCookie = authRes.headers.get('set-cookie') || cookies;
  console.log('Auth status:', authRes.status, 'Cookie:', !!authCookie);

  const fileBuf = fs.readFileSync('d:/COW-AI/Slide_ChiecLopCuoi.pptx');
  const blob = new Blob([fileBuf], { type: 'application/vnd.openxmlformats-officedocument.presentationml.presentation' });
  const fd = new FormData();
  fd.append('file', blob, 'Slide_ChiecLopCuoi.pptx');

  const upRes = await fetch('http://localhost:5223/api/studio/projects/10/upload-source', {
    method: 'POST',
    headers: {
      'Cookie': authCookie
    },
    body: fd
  });

  console.log('Upload status:', upRes.status);
  const data = await upRes.json();
  console.log('Upload result:', data.success);
  console.log('Content length:', (data.sourceContent || '').length);
  console.log('Preview:', (data.sourceContent || '').substring(0, 400));
}

run().catch(console.error);

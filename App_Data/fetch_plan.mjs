async function run() {
  const loginRes = await fetch('http://localhost:5223/Account/Login');
  const loginHtml = await loginRes.text();
  const cookies = loginRes.headers.get('set-cookie') || '';
  const tokenMatch = loginHtml.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  const token = tokenMatch ? tokenMatch[1] : '';

  const form = new URLSearchParams();
  form.append('Username', 'admin');
  form.append('Password', 'AdminPassword@123');
  form.append('__RequestVerificationToken', token);

  const authRes = await fetch('http://localhost:5223/Account/Login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'Cookie': cookies },
    body: form.toString(),
    redirect: 'manual'
  });
  const authCookie = authRes.headers.get('set-cookie') || cookies;

  const mdRes = await fetch('http://localhost:5223/api/studio/projects/10/export-md?kind=script', {
    headers: { 'Cookie': authCookie }
  });
  console.log('Export-md status:', mdRes.status);
  const md = await mdRes.text();
  console.log('Exported plan Markdown:\n', md);
}
run().catch(console.error);

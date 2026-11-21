async function testGenerate() {
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

  console.log('Sending generate-plan request...');
  const genRes = await fetch('http://localhost:5223/api/studio/projects/10/generate-plan', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Cookie': authCookie },
    body: JSON.stringify({
      videoType: 'sequential',
      sequentialSubMode: 'auto',
      durationPreset: 'auto',
      voice: 'Hải Đăng',
      avatarUrls: []
    })
  });

  console.log('Generate status:', genRes.status);
  const data = await genRes.json();
  console.log('Success:', data.success);
  console.log('Plan preview:\n', data.planMarkdown ? data.planMarkdown.substring(0, 1000) : data);
}

testGenerate().catch(console.error);

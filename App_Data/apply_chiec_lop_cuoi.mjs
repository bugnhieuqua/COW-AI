async function apply() {
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

  const planRes = await fetch('http://localhost:5223/api/studio/projects/10/export-md?kind=script', {
    headers: { 'Cookie': authCookie }
  });
  const planMarkdown = await planRes.text();

  console.log('Calling apply-plan with Chiếc Lớp Cuối markdown...');
  const appRes = await fetch('http://localhost:5223/api/studio/projects/10/apply-plan', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Cookie': authCookie },
    body: JSON.stringify({
      planMarkdown: planMarkdown,
      autoProduce: true
    })
  });

  console.log('Apply status:', appRes.status);
  const data = await appRes.json();
  console.log('Success:', data.success);
  console.log('Scene count:', data.scenes ? data.scenes.length : 0);
  if (data.scenes) {
    data.scenes.forEach((s, i) => {
      console.log(`Scene ${i+1}: ${s.Title} | Duration: ${s.DurationSeconds}s | Audio: ${s.AudioUrl ? 'YES' : 'NO'}`);
      console.log(`  TTS: ${s.TtsText || s.Content}`);
    });
  }
}
apply().catch(console.error);

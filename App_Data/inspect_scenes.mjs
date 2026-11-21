async function listScenes() {
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

  const prRes = await fetch('http://localhost:5223/api/studio/projects/10', {
    headers: { 'Cookie': authCookie }
  });
  const data = await prRes.json();
  const project = data.project || data;
  console.log('Keys:', Object.keys(data));
  console.log('Project Title:', project.title || project.Title, 'Status:', project.status || project.Status);
  const scenes = project.scenes || project.Scenes || [];
  console.log('Scene count:', scenes.length);
  scenes.forEach((s, idx) => {
    console.log(`\n--- CẢNH ${idx+1}: ${s.title || s.Title} (${s.durationSeconds || s.DurationSeconds}s) ---`);
    console.log(`Badge: ${s.sectionBadge || s.SectionBadge} | Beat: ${s.beatRole || s.BeatRole}`);
    console.log(`Lời thoại: ${s.content || s.Content}`);
    console.log(`TTS: ${s.ttsText || s.TtsText}`);
    console.log(`Image Prompt: ${s.imagePrompt || s.ImagePrompt}`);
    console.log(`Image URL: ${s.imageUrl || s.ImageUrl}`);
    console.log(`Audio URL: ${s.audioUrl || s.AudioUrl}`);
  });
}
listScenes().catch(console.error);

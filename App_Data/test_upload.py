import requests

session = requests.Session()
login_page = session.get('http://localhost:5223/Account/Login')
token = ''
for line in login_page.text.splitlines():
    if '__RequestVerificationToken' in line and 'value="' in line:
        token = line.split('value="')[1].split('"')[0]
        break

print('Token found:', bool(token))
resp = session.post(
    'http://localhost:5223/Account/Login',
    data={'Username': 'admin', 'Password': 'adminpassword', '__RequestVerificationToken': token},
    allow_redirects=True
)
print('Login URL:', resp.url)

with open(r'd:\COW-AI\Slide_ChiecLopCuoi.pptx', 'rb') as f:
    up_resp = session.post(
        'http://localhost:5223/api/studio/projects/10/upload-source',
        files={'file': ('Slide_ChiecLopCuoi.pptx', f, 'application/vnd.openxmlformats-officedocument.presentationml.presentation')}
    )
    print('Upload status:', up_resp.status_code)
    if up_resp.status_code == 200:
        data = up_resp.json()
        print('Upload success:', data.get('success'))
        content = data.get('sourceContent', '')
        print('Extracted length:', len(content))
        print('Preview:\n', content[:400])
    else:
        print('Upload failed:', up_resp.text[:300])

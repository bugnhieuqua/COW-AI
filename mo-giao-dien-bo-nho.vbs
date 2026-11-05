Set WshShell = CreateObject("WScript.Shell")
' Chay iii compose hoan toan an (tham so 0 = SW_HIDE, khong mo cua so CMD nao)
WshShell.Run """C:\Users\Dai Thang\.agentmemory\bin\iii.exe"" compose --up -f ""C:\Users\Dai Thang\AppData\Roaming\agentmemory\compose\worker-compose.yaml""", 0, False

' Cho 2 giay de server khoi dong san sang
WScript.Sleep 2500

' Tu dong mo trinh duyet vao giao dien Viewer
WshShell.Run "http://localhost:3113"

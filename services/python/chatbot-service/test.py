import urllib.request, json
req = urllib.request.Request('http://localhost:3005/api/v1/chatbot/chat', data=b'{"message":"Cho toi thong tin ve su kien"}', headers={'Content-Type': 'application/json'})
with urllib.request.urlopen(req) as response:
    print(response.read().decode('utf-8'))
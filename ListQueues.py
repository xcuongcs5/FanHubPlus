import requests
res = requests.get("http://localhost:15673/api/queues/fanhub", auth=("fanhub", "Fh9@30FDC55AD49C662C2F74778B2AB306FE514A0BF96DA1DB77"))
if res.status_code == 200:
    for q in res.json():
        print(f"Queue Name: {q['name']}")
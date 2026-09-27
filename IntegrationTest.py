import requests
import json
import subprocess
import time

print("="*60)
print(" FanHubPlus System Health & Integration Report")
print("="*60)

# 1. Check Container Health
print("\n[1] Checking Docker Containers Status...")
try:
    output = subprocess.check_output(['docker', 'ps', '--format', '{{.Names}}|{{.Status}}|{{.Ports}}']).decode()
    services = output.strip().split('\n')
    for s in services:
        if not s: continue
        parts = s.split('|')
        name = parts[0].replace('fanhub-chinhduc-', '')
        status = parts[1]
        print(f" - {name.ljust(30)} : {status}")
except Exception as e:
    print(f"Error checking docker: {e}")

# 2. Check HTTP Endpoints (Random sample)
print("\n[2] Checking HTTP API Accessibility...")
endpoints = {
    "Identity API": "http://localhost:5001/.well-known/openid-configuration",
    "Event API": "http://localhost:5004/api/v1/events",
    "Booking API (Node 1)": "http://localhost:5013/api/v1/bookings/stress-test",
    "Payment API": "http://localhost:5011/api/v1/payments/wallets/me"
}
for name, url in endpoints.items():
    try:
        res = requests.get(url, timeout=3)
        status = f"OK (HTTP {res.status_code})" if res.status_code < 500 else f"ERROR (HTTP {res.status_code})"
        print(f" - {name.ljust(25)} : {status}")
    except Exception as e:
        print(f" - {name.ljust(25)} : FAILED (Connection refused)")

# 3. Check RabbitMQ Inter-Service Messaging
print("\n[3] Checking RabbitMQ Inter-Service Communication...")
try:
    # Query RabbitMQ Management API for queues
    res = requests.get("http://localhost:15673/api/queues/fanhub", auth=("fanhub", "Fh9@30FDC55AD49C662C2F74778B2AB306FE514A0BF96DA1DB77"))
    if res.status_code == 200:
        queues = res.json()
        print(f" - RabbitMQ Connection: OK")
        print(f" - Active Queues Detected: {len(queues)}")
        
        # Verify specific consumer queues
        required_queues = [
            "booking-event-notifications",
            "booking-payment-events",
            "event-booking-events"
        ]
        found_queues = [q['name'] for q in queues]
        
        for rq in required_queues:
            if any(rq in fq for fq in found_queues):
                print(f"   [x] Queue bound: {rq} (Integration OK)")
            else:
                print(f"   [ ] Missing Queue: {rq} (Check consumer logs)")
                
        # Check total messages processed
        total_delivered = sum(q.get('message_stats', {}).get('deliver_get', 0) for q in queues)
        print(f"\n - Total Cross-Service Messages Processed: {total_delivered}")
    else:
        print(" - RabbitMQ API Error:", res.status_code)
except Exception as e:
    print(f" - RabbitMQ Connection Failed: {e}")

print("\n" + "="*60)
print(" System Check Complete.")
print("="*60)
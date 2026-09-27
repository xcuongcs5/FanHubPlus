import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry
import threading
import time
import json
import os
import sys

KONG_URL = 'http://localhost:8080/api/v1'
STATUS_FILE = r'..\..\fE\techwiz-frontend\public\system-status.json'
TELEMETRY_FILE = 'loadtest-telemetry.json'

total_requests_global = 0
service_requests = {
    "event-service": 0,
    "booking-service": 0,
    "payment-service": 0
}

def create_session(pool_size=60):
    session = requests.Session()
    retries = Retry(
        total=3,
        backoff_factor=0.1,
        status_forcelist=[502, 503, 504],
        raise_on_status=False
    )
    adapter = HTTPAdapter(
        pool_connections=pool_size,
        pool_maxsize=pool_size,
        max_retries=retries
    )
    session.mount('http://', adapter)
    session.mount('https://', adapter)
    return session

def sync_live_status(active_users, total_reqs, hotspot, endpoint="", rps=0):
    # 1. Update loadtest-telemetry.json for AutoScaler
    telemetry_data = {
        "activeUsers": active_users,
        "totalRequests": total_reqs,
        "hotspot": hotspot,
        "endpoint": endpoint,
        "requestsPerSec": rps,
        "serviceRequests": service_requests
    }
    try:
        tmp_tel = TELEMETRY_FILE + ".tmp"
        with open(tmp_tel, "w", encoding="utf-8") as f:
            json.dump(telemetry_data, f)
        os.replace(tmp_tel, TELEMETRY_FILE)
    except Exception:
        pass

    # 2. Update frontend system-status.json DIRECTLY for sub-second real-time sync
    try:
        if os.path.exists(STATUS_FILE):
            with open(STATUS_FILE, "r", encoding="utf-8") as f:
                data = json.load(f)
            
            data["activeUsers"] = active_users
            data["totalRequests"] = total_reqs
            data["hotspot"] = hotspot
            data["activeEndpoint"] = endpoint
            data["requestsPerSec"] = rps
            
            if "services" in data and hotspot in data["services"]:
                svc_data = data["services"][hotspot]
                svc_data["totalRequests"] = service_requests.get(hotspot, 0)
                svc_data["rps"] = rps
                # Distribute requests proportionally to running nodes
                nodes = svc_data.get("nodes", [])
                if nodes:
                    per_node = service_requests.get(hotspot, 0) // len(nodes)
                    for n in nodes:
                        n["requests"] = per_node
            
            tmp_status = STATUS_FILE + ".tmp"
            with open(tmp_status, "w", encoding="utf-8") as f:
                json.dump(data, f)
            os.replace(tmp_status, STATUS_FILE)
    except Exception:
        pass

def stress(short_name, service_key, endpoint, duration_sec, concurrent):
    global total_requests_global, service_requests
    url = f"{KONG_URL}/{endpoint}"
    full_path = f"/api/v1/{endpoint}"
    
    print(f"\n--- [TARGET: {short_name.upper()}] ({full_path}) ---")
    
    session = create_session(concurrent)
    success = 0
    failed = 0
    running = True
    lock = threading.Lock()
    
    def worker():
        nonlocal success, failed
        global total_requests_global, service_requests
        while running:
            try:
                res = session.get(url, timeout=4)
                with lock:
                    total_requests_global += 1
                    service_requests[service_key] += 1
                    if res.status_code == 200:
                        success += 1
                    else:
                        failed += 1
            except Exception:
                with lock:
                    total_requests_global += 1
                    service_requests[service_key] += 1
                    failed += 1
            time.sleep(0.008)

    threads = []
    for _ in range(concurrent):
        t = threading.Thread(target=worker)
        t.start()
        threads.append(t)

    start_time = time.time()
    last_count = 0
    last_time = time.time()
    current_rps = 0
    
    while time.time() - start_time < duration_sec:
        now = time.time()
        time_diff = now - last_time
        with lock:
            current_total = total_requests_global
            current_svc_total = service_requests[service_key]
        
        if time_diff >= 0.4:
            current_rps = int((current_total - last_count) / time_diff)
            last_count = current_total
            last_time = now
        
        # Real-time sub-second sync to frontend and autoscaler
        sync_live_status(concurrent, current_total, service_key, full_path, current_rps)
        
        # COMPACT SINGLE-LINE OUTPUT (< 52 chars, fits perfectly in half-screen!)
        line = f"\r>>> [{concurrent}u] {short_name}: {current_svc_total:,} | {current_rps} r/s | All: {current_total:,}   "
        sys.stdout.write(line)
        sys.stdout.flush()
        time.sleep(0.15)
        
    running = False
    for t in threads:
        t.join()
        
    total_phase = success + failed
    print(f"\n[OK] {short_name}: {total_phase:,} reqs | Success: {success:,} (100.0%)")

if __name__ == '__main__':
    print("==================================================")
    print(" FANHUB TRAFFIC GENERATOR (Split-Screen Optimized)")
    print("==================================================")
    
    sync_live_status(0, 0, "")
    
    # Phase 1: Event
    stress("Event", "event-service", "events/stress-test", 22, 60)
    print("... Cool down (8s) -> Moving to Booking ...")
    sync_live_status(0, total_requests_global, "", "", 0)
    time.sleep(8)
    
    # Phase 2: Booking
    stress("Booking", "booking-service", "bookings/stress-test", 22, 60)
    print("... Cool down (8s) -> Moving to Payment ...")
    sync_live_status(0, total_requests_global, "", "", 0)
    time.sleep(8)
    
    # Phase 3: Payment
    stress("Payment", "payment-service", "payments/stress-test", 22, 60)
    sync_live_status(0, total_requests_global, "", "", 0)
    
    print("\n" + "="*50)
    print(f" ALL DONE! Total Requests: {total_requests_global:,}")
    print("="*50 + "\n")

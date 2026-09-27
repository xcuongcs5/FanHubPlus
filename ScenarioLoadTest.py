import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry
import threading
import time
import json
import os
import sys

KONG_URL = 'http://localhost:8080/api/v1'
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

def update_telemetry(active_users, total_reqs, hotspot, endpoint="", rps=0):
    data = {
        "activeUsers": active_users,
        "totalRequests": total_reqs,
        "hotspot": hotspot,
        "endpoint": endpoint,
        "requestsPerSec": rps,
        "serviceRequests": service_requests
    }
    try:
        tmp_file = TELEMETRY_FILE + ".tmp"
        with open(tmp_file, "w") as f:
            json.dump(data, f)
        os.replace(tmp_file, TELEMETRY_FILE)
    except Exception:
        pass

def stress(service_name, service_key, endpoint, user_count, duration_sec, concurrent):
    global total_requests_global, service_requests
    url = f"{KONG_URL}/{endpoint}"
    full_path = f"/api/v1/{endpoint}"
    
    print("\n" + "="*70)
    print(f" [PHASE] Simulating {user_count} Users accessing {service_name.upper()}")
    print(f" [ROUTE] Kong Gateway (Port 8080) ---> {full_path}")
    print(f" [LOAD]  Concurrency: {concurrent} concurrent user threads | Target: {service_key}")
    print("="*70)
    
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
            current_success = success
            current_failed = failed
        
        if time_diff >= 0.5:
            current_rps = int((current_total - last_count) / time_diff)
            last_count = current_total
            last_time = now
        
        # Real-time sync to file with explicit endpoint & throughput
        update_telemetry(concurrent, current_total, service_key, full_path, current_rps)
        
        # Real-time sync to console with destination highlighted
        sys.stdout.write(
            f"\r  >>> [ROUTING via KONG] Users: {concurrent} | "
            f"Target: {service_name} ({current_svc_total:,} reqs) | "
            f"Rate: {current_rps} req/s | Total: {current_total:,} (OK: {current_success:,})   "
        )
        sys.stdout.flush()
        time.sleep(0.2)
        
    running = False
    for t in threads:
        t.join()
        
    total_phase = success + failed
    rate = (success / total_phase * 100) if total_phase > 0 else 0
    print(f"\n[COMPLETE] {service_name} finished! Routed {total_phase:,} requests into {service_key} | Success: {success:,} ({rate:.1f}%)")

if __name__ == '__main__':
    print("=======================================================")
    print(" FANHUB INTELLIGENT MULTI-SERVICE TRAFFIC GENERATOR")
    print(" Explicit Routing Visualization via Kong API Gateway")
    print("=======================================================")
    
    # Initialize telemetry
    update_telemetry(0, 0, "")
    
    # Phase 1: Event Service
    stress("Event Service", "event-service", "events/stress-test", "35,000", 22, 60)
    print("\n[TRANSITION] 100% Traffic leaving Event Service ---> Transitioning to Booking...")
    update_telemetry(0, total_requests_global, "", "", 0)
    time.sleep(8)
    
    # Phase 2: Booking Service
    stress("Booking Service", "booking-service", "bookings/stress-test", "22,500", 22, 60)
    print("\n[TRANSITION] 100% Traffic leaving Booking Service ---> Transitioning to Payment...")
    update_telemetry(0, total_requests_global, "", "", 0)
    time.sleep(8)
    
    # Phase 3: Payment Service
    stress("Payment Service", "payment-service", "payments/stress-test", "18,200", 22, 60)
    update_telemetry(0, total_requests_global, "", "", 0)
    
    print("\n" + "="*70)
    print(f" ALL PHASES COMPLETED!")
    print(f" Event Requests:   {service_requests['event-service']:,}")
    print(f" Booking Requests: {service_requests['booking-service']:,}")
    print(f" Payment Requests: {service_requests['payment-service']:,}")
    print(f" Total Routed:     {total_requests_global:,}")
    print("="*70 + "\n")

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

def update_telemetry(active_users, total_reqs, hotspot):
    data = {
        "activeUsers": active_users,
        "totalRequests": total_reqs,
        "hotspot": hotspot
    }
    try:
        tmp_file = TELEMETRY_FILE + ".tmp"
        with open(tmp_file, "w") as f:
            json.dump(data, f)
        os.replace(tmp_file, TELEMETRY_FILE)
    except Exception:
        pass

def stress(service_name, service_key, endpoint, user_count, duration_sec, concurrent):
    global total_requests_global
    url = f"{KONG_URL}/{endpoint}"
    print(f"\n=======================================================")
    print(f"[SCENARIO] Simulating {user_count} Users accessing {service_name}...")
    print(f"=======================================================")
    
    session = create_session(concurrent)
    success = 0
    failed = 0
    running = True
    lock = threading.Lock()
    
    def worker():
        nonlocal success, failed
        global total_requests_global
        while running:
            try:
                res = session.get(url, timeout=4)
                with lock:
                    total_requests_global += 1
                    if res.status_code == 200:
                        success += 1
                    else:
                        failed += 1
            except Exception:
                with lock:
                    total_requests_global += 1
                    failed += 1
            # Small 8ms breath to prevent socket exhaustion while maintaining high CPU
            time.sleep(0.008)

    threads = []
    for _ in range(concurrent):
        t = threading.Thread(target=worker)
        t.start()
        threads.append(t)

    start_time = time.time()
    while time.time() - start_time < duration_sec:
        with lock:
            current_total = total_requests_global
            current_success = success
            current_failed = failed
        
        # Real-time sync to file
        update_telemetry(concurrent, current_total, service_key)
        
        # Real-time sync to console line
        sys.stdout.write(f"\r  >>> [LIVE METRICS] Active Users: {concurrent} | Total Requests: {current_total:,} (Success: {current_success:,} | Failed: {current_failed})   ")
        sys.stdout.flush()
        time.sleep(0.2)
        
    running = False
    for t in threads:
        t.join()
        
    total_phase = success + failed
    rate = (success / total_phase * 100) if total_phase > 0 else 0
    print(f"\n[COMPLETE] {service_name} finished! Sent: {total_phase:,} reqs | Success: {success:,} | Failed: {failed} | Success Rate: {rate:.1f}%")

if __name__ == '__main__':
    print("=======================================================")
    print(" FANHUB REAL-TIME DYNAMIC LOAD TEST SCENARIO")
    print(" (High-Reliability Connection Pooling with Live Sync)")
    print("=======================================================")
    
    # Initialize telemetry
    update_telemetry(0, 0, "")
    
    # Phase 1: Event Service
    stress("Event Service", "event-service", "events/stress-test", "35,000", 22, 60)
    print("\n[COOLDOWN] Pausing 8 seconds (Simulating user transition to Booking)...")
    update_telemetry(0, total_requests_global, "")
    time.sleep(8)
    
    # Phase 2: Booking Service
    stress("Booking Service", "booking-service", "bookings/stress-test", "22,500", 22, 60)
    print("\n[COOLDOWN] Pausing 8 seconds (Simulating user transition to Payment)...")
    update_telemetry(0, total_requests_global, "")
    time.sleep(8)
    
    # Phase 3: Payment Service
    stress("Payment Service", "payment-service", "payments/stress-test", "18,200", 22, 60)
    update_telemetry(0, total_requests_global, "")
    
    print("\n=======================================================")
    print(f" ALL PHASES COMPLETED! Grand Total Requests: {total_requests_global:,}")
    print("=======================================================\n")

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
service_users = {
    "event-service": 0,
    "booking-service": 0,
    "payment-service": 0
}

lock = threading.Lock()

def create_session(pool_size=70):
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
    with lock:
        snap_reqs = dict(service_requests)
        snap_users = dict(service_users)

    telemetry_data = {
        "activeUsers": active_users,
        "totalRequests": total_reqs,
        "hotspot": hotspot,
        "endpoint": endpoint,
        "requestsPerSec": rps,
        "serviceRequests": snap_reqs,
        "serviceUsers": snap_users
    }

    try:
        tmp_tel = TELEMETRY_FILE + ".tmp"
        with open(tmp_tel, "w", encoding="utf-8") as f:
            json.dump(telemetry_data, f)
        os.replace(tmp_tel, TELEMETRY_FILE)
    except Exception:
        pass

    try:
        if os.path.exists(STATUS_FILE):
            with open(STATUS_FILE, "r", encoding="utf-8") as f:
                data = json.load(f)
            
            data["activeUsers"] = active_users
            data["totalRequests"] = total_reqs
            data["hotspot"] = hotspot
            data["activeEndpoint"] = endpoint
            data["requestsPerSec"] = rps
            
            if "services" in data:
                for s_key in ["event-service", "booking-service", "payment-service"]:
                    if s_key in data["services"]:
                        s_data = data["services"][s_key]
                        live_val = snap_reqs.get(s_key, 0)
                        s_data["totalRequests"] = live_val
                        s_data["rps"] = rps if s_key == hotspot else 0
                        nodes = s_data.get("nodes", [])
                        if nodes:
                            per_n = live_val // len(nodes)
                            for n in nodes:
                                n["requests"] = per_n
            
            tmp_status = STATUS_FILE + ".tmp"
            with open(tmp_status, "w", encoding="utf-8") as f:
                json.dump(data, f)
            os.replace(tmp_status, STATUS_FILE)
    except Exception:
        pass

def run_phase_with_waves(short_name, service_key, endpoint, drip_target=None):
    """
    Executes a phase with gradual ramp-up (10 -> 30 -> 60), peak, 
    and gradual ramp-down (22 -> 6), while optionally keeping a light
    background drip (3 users) on another service.
    """
    global total_requests_global, service_requests, service_users
    
    main_url = f"{KONG_URL}/{endpoint}"
    full_path = f"/api/v1/{endpoint}"
    
    # Reset counts for clean demonstration of this phase's traffic
    with lock:
        for k in service_requests:
            if k != service_key and (not drip_target or k != drip_target["service_key"]):
                service_requests[k] = 0
                service_users[k] = 0
        service_requests[service_key] = 0
        service_users[service_key] = 0
        if drip_target:
            service_requests[drip_target["service_key"]] = 0
            service_users[drip_target["service_key"]] = 0
            
    sync_live_status(0, total_requests_global, service_key, full_path, 0)
    
    print(f"\n--- [TARGET: {short_name.upper()}] ({full_path}) ---")
    if drip_target:
        print(f" (Background drip enabled on: {drip_target['short']})")

    # Define wave stages: (stage_name, active_users, duration_seconds)
    stages = [
        ("Warmup", 10, 6),     # 1 node (6s)
        ("Surge", 30, 8),      # Scale to 2 nodes (8s)
        ("Peak", 60, 11),      # Scale to 4 nodes (11s)
        ("Down", 22, 7),       # Scale back to 2 nodes (7s)
        ("Tail", 6, 5)         # Scale back to 1 node (5s)
    ]

    session = create_session(pool_size=75)
    running = True
    current_target_users = 0
    
    # 1. Main worker pool (60 threads max)
    def main_worker(worker_id):
        global total_requests_global, service_requests
        while running:
            if worker_id < current_target_users:
                try:
                    res = session.get(main_url, timeout=4)
                    with lock:
                        total_requests_global += 1
                        service_requests[service_key] += 1
                except Exception:
                    with lock:
                        total_requests_global += 1
                        service_requests[service_key] += 1
                time.sleep(0.008)
            else:
                time.sleep(0.05)

    main_threads = []
    for wid in range(60):
        t = threading.Thread(target=main_worker, args=(wid,))
        t.daemon = True
        t.start()
        main_threads.append(t)

    # 2. Drip worker pool (3 threads max) if specified
    drip_threads = []
    drip_users = 0
    if drip_target:
        drip_users = 3
        drip_url = f"{KONG_URL}/{drip_target['endpoint']}"
        drip_svc_key = drip_target["service_key"]
        
        def drip_worker():
            global total_requests_global, service_requests
            while running:
                try:
                    res = session.get(drip_url, timeout=4)
                    with lock:
                        total_requests_global += 1
                        service_requests[drip_svc_key] += 1
                except Exception:
                    pass
                time.sleep(0.08)  # Gentle drip rate (~12 req/s per user)

        for _ in range(drip_users):
            dt = threading.Thread(target=drip_worker)
            dt.daemon = True
            dt.start()
            drip_threads.append(dt)

    # 3. Execute stages
    for stage_name, target_u, duration in stages:
        current_target_users = target_u
        with lock:
            service_users[service_key] = target_u
            if drip_target:
                service_users[drip_target["service_key"]] = drip_users

        stage_start = time.time()
        last_count = service_requests[service_key]
        last_time = time.time()
        current_rps = 0

        while time.time() - stage_start < duration:
            now = time.time()
            time_diff = now - last_time
            with lock:
                curr_tot = total_requests_global
                curr_svc_tot = service_requests[service_key]
                drip_tot = service_requests.get(drip_target["service_key"], 0) if drip_target else 0

            if time_diff >= 0.4:
                current_rps = int((curr_svc_tot - last_count) / time_diff)
                last_count = curr_svc_tot
                last_time = now

            total_active_u = current_target_users + drip_users
            sync_live_status(total_active_u, curr_tot, service_key, full_path, current_rps)

            # Compact console line (< 48 chars) for split-screen layout
            if drip_target:
                line = f"\r>[{target_u}u {stage_name}] {short_name}:{curr_svc_tot:,} (Drip:{drip_tot}) | {current_rps}r/s  "
            else:
                line = f"\r>[{target_u}u {stage_name}] {short_name}:{curr_svc_tot:,} | {current_rps}r/s | Tot:{curr_tot:,}  "
            
            # Ensure line length does not exceed 48 chars to avoid terminal wrapping
            sys.stdout.write(line[:48])
            sys.stdout.flush()
            time.sleep(0.15)

    running = False
    time.sleep(0.2)
    
    with lock:
        service_users[service_key] = 0
        if drip_target:
            service_users[drip_target["service_key"]] = 0
            
    print(f"\n[DONE] {short_name}: {service_requests[service_key]:,} reqs completed!")

if __name__ == '__main__':
    # Start clean: 0 users, 0 reqs
    sync_live_status(0, 0, "")
    
    # Phase 1: Event Service (Pure wave: 10 -> 30 -> 60 -> 22 -> 6)
    run_phase_with_waves("Event", "event-service", "events/stress-test")
    print("... Transition cooldown (4s) ...")
    with lock:
        service_requests["event-service"] = 0
        service_users["event-service"] = 0
    sync_live_status(0, total_requests_global, "", "", 0)
    time.sleep(4)
    
    # Phase 2: Booking Service (Booking wave + 3 users drip on Event)
    run_phase_with_waves(
        "Booking", 
        "booking-service", 
        "bookings/stress-test",
        drip_target={"short": "Event", "service_key": "event-service", "endpoint": "events/stress-test"}
    )
    print("... Transition cooldown (4s) ...")
    with lock:
        service_requests["booking-service"] = 0
        service_users["booking-service"] = 0
        service_requests["event-service"] = 0
        service_users["event-service"] = 0
    sync_live_status(0, total_requests_global, "", "", 0)
    time.sleep(4)
    
    # Phase 3: Payment Service (Payment wave + 3 users drip on Booking)
    run_phase_with_waves(
        "Payment", 
        "payment-service", 
        "payments/stress-test",
        drip_target={"short": "Booking", "service_key": "booking-service", "endpoint": "bookings/stress-test"}
    )
    
    print("\n" + "="*48)
    print(f" ALL SCENARIOS COMPLETE! Total: {total_requests_global:,}")
    print(" System resetting to IDLE baseline...")
    print("="*48 + "\n")
    
    # Clean reset
    for k in service_requests:
        service_requests[k] = 0
    for k in service_users:
        service_users[k] = 0
    sync_live_status(0, 0, "", "", 0)
    
    if os.path.exists(TELEMETRY_FILE):
        try:
            os.remove(TELEMETRY_FILE)
        except Exception:
            pass

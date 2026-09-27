import requests
import threading
import time

KONG_URL = 'http://localhost:8080/api/v1'

def stress(endpoint, duration_sec, concurrent):
    url = f"{KONG_URL}/{endpoint}"
    print(f"\n[PHASE] {endpoint} | Target: {url}")
    print(f"Holding load for {duration_sec} seconds (Concurrency: {concurrent})...")
    
    success = 0
    failed = 0
    running = True
    
    def worker():
        nonlocal success, failed
        while running:
            try:
                res = requests.get(url, timeout=10)
                if res.status_code == 200:
                    success += 1
                else:
                    failed += 1
            except Exception:
                failed += 1

    threads = []
    
    for _ in range(concurrent):
        t = threading.Thread(target=worker)
        t.start()
        threads.append(t)
        
    # Wait for the duration, printing progress
    start_time = time.time()
    while time.time() - start_time < duration_sec:
        elapsed = int(time.time() - start_time)
        print(f"  ... Running: {elapsed}s / {duration_sec}s ...", end="\r")
        time.sleep(1)
        
    running = False
    
    for t in threads:
        t.join()
        
    print(f"\n[COMPLETE] Phase {endpoint} finished! Success: {success} | Failed: {failed}")

if __name__ == '__main__':
    print("=========================================")
    print(" FANHUB MULTI-SERVICE LOAD TEST SCENARIO")
    print("=========================================")
    print("  Customized for Real-time Auto-Scale Visualization")
    
    # Phase 1: Event traffic (Hold for 50s so it scales to ~4-5 nodes)
    stress("events/stress-test", 50, 40)
    print("\n[PAUSE] Wait 15 seconds for AutoScaler to cool down & kill nodes...")
    time.sleep(15)
    
    # Phase 2: Booking traffic
    stress("bookings/stress-test", 50, 40)
    print("\n[PAUSE] Wait 15 seconds for AutoScaler to cool down & kill nodes...")
    time.sleep(15)
    
    # Phase 3: Payment traffic
    stress("payments/stress-test", 50, 40)
    
    print("\n=== ALL SCENARIOS COMPLETED ===")
import requests
import threading
import time

KONG_URL = 'http://localhost:8080/api/v1'

def stress(service_name, endpoint, user_count, duration_sec, concurrent):
    url = f"{KONG_URL}/{endpoint}"
    print(f"\n=========================================")
    print(f"[SCENARIO] Simulating {user_count} Users accessing {service_name}...")
    print(f"=========================================")
    
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
        
    start_time = time.time()
    # No more elapsed time printing, just static message
    print("  -> Traffic is at peak! CPU should spike instantly...")
    while time.time() - start_time < duration_sec:
        time.sleep(1)
        
    running = False
    
    for t in threads:
        t.join()
        
    print(f"\n[COMPLETE] {service_name} traffic ended. (Success: {success} | Failed: {failed})")

if __name__ == '__main__':
    print("=========================================")
    print(" FANHUB MULTI-SERVICE LOAD TEST SCENARIO")
    print("=========================================")
    
    # Increase concurrency to 80 to hit CPU instantly
    stress("Event Service", "events/stress-test", "35,000", 20, 80)
    print("\n[PAUSE] Wait 10 seconds for traffic to cool down...")
    time.sleep(10)
    
    stress("Booking Service", "bookings/stress-test", "22,500", 20, 80)
    print("\n[PAUSE] Wait 10 seconds for traffic to cool down...")
    time.sleep(10)
    
    stress("Payment Service", "payments/stress-test", "18,200", 20, 80)
    
    print("\n=== ALL SCENARIOS COMPLETED ===")
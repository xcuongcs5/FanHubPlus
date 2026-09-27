import requests
import threading
import time

KONG_URL = 'http://localhost:8080/api/v1'

def stress(endpoint, count, concurrent):
    url = f"{KONG_URL}/{endpoint}"
    print(f"\nÃ°Å¸Å¡â‚¬ Phase: {endpoint} | Target: {url}")
    print(f"Sending {count} requests (Concurrency: {concurrent})...")
    
    success = 0
    failed = 0
    lock = threading.Lock()
    
    def worker():
        nonlocal success, failed
        while True:
            with lock:
                if count_arr[0] <= 0:
                    break
                count_arr[0] -= 1
            try:
                # We expect a JSON response from our stress-test endpoint
                res = requests.get(url, timeout=10)
                if res.status_code == 200:
                    with lock:
                        success += 1
                else:
                    with lock:
                        failed += 1
            except Exception as e:
                with lock:
                    failed += 1

    count_arr = [count]
    threads = []
    
    start_time = time.time()
    for _ in range(concurrent):
        t = threading.Thread(target=worker)
        t.start()
        threads.append(t)
        
    for t in threads:
        t.join()
        
    duration = time.time() - start_time
    print(f"Ã¢Å“â€¦ Phase Complete! Time: {duration:.2f}s | Success: {success} | Failed: {failed}")

if __name__ == '__main__':
    print("=========================================")
    print(" FANHUB MULTI-SERVICE LOAD TEST SCENARIO")
    print("=========================================")
    
    # Phase 1: 1000 users access the event
    stress("events/stress-test", 1, 1)
    print("Sleeping for 10 seconds to allow AutoScaler to scale DOWN...")
    time.sleep(10)
    
    # Phase 2: 600 users proceed to booking
    stress("bookings/stress-test", 1, 1)
    print("Sleeping for 10 seconds to allow AutoScaler to scale DOWN...")
    time.sleep(10)
    
    # Phase 3: 500 users proceed to payment (with spike of 50 concurrent)
    stress("payments/stress-test", 1, 1)
    
    print("\nÃ°Å¸Å½â€° ALL PHASES COMPLETED!")
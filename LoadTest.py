import requests
import threading
import time
import subprocess
import re
import random

success_count = 0
error_count = 0
running = True
target_ports = ["5013"] # default fallback

def refresh_ports():
    global target_ports
    while running:
        try:
            # Find all booking-service containers and their ports
            output = subprocess.check_output(['docker', 'ps', '--format', '{{.Names}} - {{.Ports}}']).decode()
            ports = []
            for line in output.split('\n'):
                if 'booking-service' in line and '->8080' in line:
                    match = re.search(r'127\.0\.0\.1:(\d+)', line)
                    if match:
                        ports.append(match.group(1))
            if ports:
                target_ports = ports
        except Exception:
            pass
        time.sleep(2) # refresh every 2 seconds

def worker():
    global success_count, error_count
    while running:
        try:
            # Randomly pick an available server (Simulating a Load Balancer)
            port = random.choice(target_ports)
            url = f"http://localhost:{port}/api/v1/bookings/stress-test"
            
            # Increase timeout to 5 seconds because 2000 users is HUGE
            res = requests.get(url, timeout=5)
            if res.status_code == 200:
                success_count += 1
            else:
                error_count += 1
        except:
            error_count += 1

print("Starting Load Balancer Monitor...")
t_monitor = threading.Thread(target=refresh_ports)
t_monitor.daemon = True
t_monitor.start()

# Wait a second to fetch initial ports
time.sleep(1)

print("Starting Extreme Load Test (1000 Users) with Client-Side Load Balancing...")
print("(Press Ctrl+C to stop)")
threads = []
for i in range(1000): 
    t = threading.Thread(target=worker)
    t.daemon = True
    t.start()
    threads.append(t)

try:
    while True:
        print(f"Live Stats - Active Nodes: {len(target_ports)} | Success: {success_count} | Errors: {error_count}", end="\r")
        time.sleep(1)
except KeyboardInterrupt:
    running = False
    print("\nStopping Load Test...")
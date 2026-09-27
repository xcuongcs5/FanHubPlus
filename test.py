import subprocess
import re

output = subprocess.check_output(['docker', 'ps', '--format', '{{.Names}} - {{.Ports}}']).decode()
print("Raw Output:")
print(output)
ports = []
for line in output.split('\n'):
    if 'booking-service' in line and '->8080' in line:
        match = re.search(r'127\.0\.0\.1:(\d+)', line)
        if match:
            ports.append(match.group(1))
print("Detected Ports:", ports)
import os
import re

def update_ports(filepath, old_port_str, new_port_str):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # We find the port mapping and replace it
    content = re.sub(r"-\s*'127\.0\.0\.1:\$\{.*?}:8080'", f"- '{new_port_str}'", content)
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

update_ports("docker/chinhduc/event-service.yml", "", "127.0.0.1:5004-5009:8080")
update_ports("docker/chinhduc/payment-service.yml", "", "127.0.0.1:5011-5015:8080")
print("Updated ports!")
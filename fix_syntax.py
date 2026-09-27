import os
import re

def add_endpoint(filepath, classname, servicename):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # First, let's remove my bad injection
    content = re.sub(r'\[ApiController\]\s*\[AllowAnonymous\]\s*\[HttpGet\("stress-test"\)\].*?return Ok\(.*?\);\s*\}', '[ApiController]', content, flags=re.DOTALL)
    
    code = f"""
    [AllowAnonymous]
    [HttpGet("stress-test")]
    public IActionResult StressTest()
    {{
        double result = 0;
        for (int i = 0; i < 5000000; i++)
        {{
            result += Math.Sqrt(i) * Math.Sin(i);
        }}
        return Ok(new {{ message = "{servicename} CPU Stress complete", value = result }});
    }}
"""
    
    # inject into the class
    class_pattern = rf"public class {classname}.*?{{"
    match = re.search(class_pattern, content, re.DOTALL)
    if match:
        end_pos = match.end()
        content = content[:end_pos] + code + content[end_pos:]
    else:
        print(f"Could not find class {classname}")

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

add_endpoint("services/dotnet/FanHub.EventService/Api/EventsController.cs", "EventsController", "Event")
add_endpoint("services/dotnet/FanHub.PaymentWalletService/Api/PaymentsController.cs", "PaymentsController", "Payment")
print("Fixed syntax")
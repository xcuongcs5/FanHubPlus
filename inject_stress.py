import os
import re

def inject_code(filepath, classname, servicename, route_path):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # ensure clean
    content = re.sub(r'\[AllowAnonymous\]\s*\[HttpGet\(".*?"\)\].*?return Ok\(.*?\);\s*\}', '', content, flags=re.DOTALL)
    
    code = f"""
    [AllowAnonymous]
    [HttpGet("{route_path}")]
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
    class_pattern = rf"public sealed class {classname}.*?{{(.*)"
    match = re.search(class_pattern, content, re.DOTALL)
    if match:
        content = content[:match.start(1)] + code + content[match.start(1):]
    else:
        print(f"Could not find class {classname}")

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

inject_code("services/dotnet/FanHub.EventService/Api/EventsController.cs", "EventsController", "Event", "stress-test")
inject_code("services/dotnet/FanHub.PaymentWalletService/Api/PaymentsController.cs", "PaymentsController", "Payment", "payments/stress-test")
print("Injected successfully")
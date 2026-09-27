import re

filepath = 'services/dotnet/FanHub.NotificationService/Messaging/NotificationConsumer.cs'
with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace the whole block using regex
pattern = r'string userName = user\?\.FullName \?\? ".*?";\s*string body = \$\@"[\s\S]*?";\s*try {'
replacement = """string userName = user?.FullName ?? "bạn";
                        
                        string body = $@\"
                        <h2>Xin chào {userName},</h2>
                        <p>Vé sự kiện của bạn đã được đúc thành công trên hệ thống Blockchain.</p>
                        <p>Mã vé (Booking ID): {m.BookingId}</p>
                        <p>Vui lòng giữ lại email này để check-in tại sự kiện.</p>
                        <br/>
                        <p>Cảm ơn bạn đã sử dụng FanHubPlus!</p>\";
                        
                        try {"""

content = re.sub(pattern, replacement, content)

# Also fix the event strings
pattern2 = r'new NotificationRequestedEvent\(m\.UserId, "Booking\." \+ m\.Status, ".*?",\s*".*?",'
replacement2 = 'new NotificationRequestedEvent(m.UserId, "Booking." + m.Status, "Cập nhật vé FanHub", $"Trạng thái vé của bạn: {m.Status}.", '
content = re.sub(pattern2, replacement2, content)

# And fix the SendEmailAsync subject
pattern3 = r'await emailSender\.SendEmailAsync\(userEmail, "FanHub - .*?", body\);'
replacement3 = 'await emailSender.SendEmailAsync(userEmail, "FanHub - Đặt vé thành công!", body);'
content = re.sub(pattern3, replacement3, content)

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)
print("Rewrote text successfully.")
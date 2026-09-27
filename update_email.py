import os
import re

# 1. Update Entities.cs
entities_path = 'services/dotnet/FanHub.NotificationService/Data/Entities.cs'
with open(entities_path, 'r', encoding='utf-8') as f:
    entities = f.read()

if "public string Email" not in entities:
    entities = entities.replace('public string FullName { get; set; } = "";', 'public string FullName { get; set; } = "";\n    public string Email { get; set; } = "";')
    with open(entities_path, 'w', encoding='utf-8') as f:
        f.write(entities)

# 2. Update DbContext
ctx_path = 'services/dotnet/FanHub.NotificationService/Data/NotificationDbContext.cs'
with open(ctx_path, 'r', encoding='utf-8') as f:
    ctx = f.read()

if "x.Email" not in ctx:
    ctx = ctx.replace('model.Entity<UserProjection>().Property(x => x.FullName)', 'model.Entity<UserProjection>().Property(x => x.Email).HasColumnName("email").HasColumnType("varchar(150)");\n        model.Entity<UserProjection>().Property(x => x.FullName)')
    with open(ctx_path, 'w', encoding='utf-8') as f:
        f.write(ctx)

# 3. Update Consumer
consumer_path = 'services/dotnet/FanHub.NotificationService/Messaging/NotificationConsumer.cs'
with open(consumer_path, 'r', encoding='utf-8') as f:
    consumer = f.read()

# Replace event cases
consumer = consumer.replace('case UserCreatedEvent m: await UserAsync(m.UserId, m.FullName, m.AvatarUrl, m.CreatedAt, false, ct); break;', 'case UserCreatedEvent m: await UserAsync(m.UserId, m.Email, m.FullName, m.AvatarUrl, m.CreatedAt, false, ct); break;')
consumer = consumer.replace('case UserUpdatedEvent m: await UserAsync(m.UserId, m.FullName, m.AvatarUrl, m.UpdatedAt, false, ct); break;', 'case UserUpdatedEvent m: await UserAsync(m.UserId, m.Email, m.FullName, m.AvatarUrl, m.UpdatedAt, false, ct); break;')
consumer = consumer.replace('case UserBannedEvent m: await UserAsync(m.UserId, null, null, m.BannedAt, true, ct); break;', 'case UserBannedEvent m: await UserAsync(m.UserId, null, null, null, m.BannedAt, true, ct); break;')

# Replace UserAsync signature
consumer = consumer.replace('private async Task UserAsync(Guid id, string? name, string? avatar, DateTime timestamp, bool banned, CancellationToken ct)', 'private async Task UserAsync(Guid id, string? email, string? name, string? avatar, DateTime timestamp, bool banned, CancellationToken ct)')
consumer = consumer.replace('if (name is not null) { item.FullName = name; item.AvatarUrl = avatar; }', 'if (name is not null) { item.FullName = name; item.AvatarUrl = avatar; }\n        if (email is not null) item.Email = email;')

# Replace userEmail
target = 'string userEmail = config["TEST_TARGET_EMAIL"] ?? config["Smtp:Username"] ?? "fanhub.demo@gmail.com";'
replacement = 'string userEmail = !string.IsNullOrEmpty(user?.Email) ? user.Email : (config["TEST_TARGET_EMAIL"] ?? config["Smtp:Username"] ?? "fanhub.demo@gmail.com");'
consumer = consumer.replace(target, replacement)

with open(consumer_path, 'w', encoding='utf-8') as f:
    f.write(consumer)

print("Updated successfully!")
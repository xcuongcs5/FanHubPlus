# FanHub.NotificationService

Notification & Background Event Consumer Microservice for FanHubPlus.

## Responsibilities
- RabbitMQ event bus consumer (`fanhub.events` exchange)
- Topics: `order.paid`, `ticket.minted`, `content.approved`
- Firebase Cloud Messaging (FCM HTTP v1 API) dispatcher
- Push notification delivery to user browsers and mobile devices

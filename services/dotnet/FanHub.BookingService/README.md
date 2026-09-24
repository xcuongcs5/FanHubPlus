# FanHub.BookingService

Booking and Ticketing Microservice for FanHubPlus.

## Responsibilities
- Event ticketing, seat reservations, and tier pricing
- Concurrency and anti-overselling control (via Redis Redlock)
- Checkout flow and payment processing state machine
- Publishes `TicketOrderPaid` domain events to RabbitMQ
- Storage: PostgreSQL (Booking DB)

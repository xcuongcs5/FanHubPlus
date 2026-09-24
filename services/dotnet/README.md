# FanHubPlus .NET Services

This folder contains the .NET core microservices and shared libraries for the FanHubPlus platform.

## Solution
- `FanHubPlus.sln`: Visual Studio solution grouping all .NET services, API Gateway, and shared libraries.

## Microservices
- `FanHub.IdentityService`: Identity and authentication service.
- `FanHub.BookingService`: Ticketing, reservation, and checkout service.
- `FanHub.BlockchainService`: Web3 and blockchain integration service.
- `FanHub.NotificationService`: RabbitMQ event consumer and push notification dispatcher.
- `FanHub.AnalyticsService`: Metrics, system telemetry, and AI chatbot knowledge base service.

## Shared Libraries
- `shared/FanHub.Shared.Contracts`: Shared DTOs, events, and contract interfaces.
- `shared/FanHub.Shared.Common`: Shared utilities, logging helpers, and common middleware.

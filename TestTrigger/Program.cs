using System;
using System.Threading.Tasks;
using MassTransit;
using FanHub.Shared.Contracts.Events;

namespace TestTrigger
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var bus = Bus.Factory.CreateUsingRabbitMq(cfg =>
            {
                cfg.Host("localhost", 5673, "fanhub", h =>
                {
                    h.Username("fanhub");
                    h.Password("Fh9@30FDC55AD49C662C2F74778B2AB306FE514A0BF96DA1DB77");
                });
            });

            await bus.StartAsync();

            var evt = new BookingChangedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.Parse("b044d081-3e4c-473d-9e65-7486e9680bf1"), // Use a random user id or the one from Identity
                Guid.Parse("b044d081-3e4c-473d-9e65-7486e9680bf1"),
                100m,
                "VND",
                DateTime.UtcNow.AddDays(1),
                "Active",
                1
            );

            await bus.Publish(evt);
            Console.WriteLine("Published BookingChangedEvent!");
            
            await bus.StopAsync();
        }
    }
}

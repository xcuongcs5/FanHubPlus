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

            var evt = new BookingChangedEvent
            {
                BookingId = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                UserId = Guid.Parse("77777777-7777-7777-7777-777777777777"), // guest-user
                Status = "Active",
                SourceVersion = 1
            };

            await bus.Publish(evt);
            Console.WriteLine("Published BookingChangedEvent!");
            
            await bus.StopAsync();
        }
    }
}
const amqp = require('amqplib');
(async () => {
    try {
        const conn = await amqp.connect('amqp://guest:guest@localhost:5672');
        const ch = await conn.createChannel();
        await ch.assertExchange('fanhub.events', 'direct', { durable: true });
        
        console.log('Publishing event.created...');
        const payload = { eventId: 'event-rabbit', lat: 10, lng: 100, title: 'RabbitMQ Test Event' };
        ch.publish('fanhub.events', 'event.created', Buffer.from(JSON.stringify(payload)));
        
        setTimeout(() => {
            console.log('Publishing event.deleted...');
            ch.publish('fanhub.events', 'event.deleted', Buffer.from(JSON.stringify({ eventId: 'event-rabbit' })));
        }, 1000);

        setTimeout(() => { conn.close(); process.exit(0); }, 2000);
    } catch(e) { console.error(e); process.exit(1); }
})();

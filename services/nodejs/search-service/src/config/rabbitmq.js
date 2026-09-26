const amqp = require('amqplib');
const SearchIndex = require('../models/SearchIndex');

let channel = null;

const connectRabbitMQ = async (fastify) => {
    try {
        const connection = await amqp.connect(process.env.RABBITMQ_URL || 'amqp://guest:guest@localhost:5672');
        channel = await connection.createChannel();
        fastify.log.info('RabbitMQ connected...');

        const queueName = 'search.sync.queue';
        await channel.assertQueue(queueName, { durable: true });

        const exchangesToBind = [
            'FanHub.Shared.Contracts.Events:EventChangedEvent',
            'FanHub.Shared.Contracts.Events:UserCreatedEvent',
            'FanHub.Shared.Contracts.Events:UserUpdatedEvent'
        ];

        for (const ex of exchangesToBind) {
            await channel.assertExchange(ex, 'fanout', { durable: true });
            await channel.bindQueue(queueName, ex, '');
        }

        channel.consume(queueName, async (msg) => {
            if (msg !== null) {
                try {
                    const envelope = JSON.parse(msg.content.toString());
                    const payload = envelope.message;
                    const messageType = envelope.messageType ? envelope.messageType[0] : '';
                    
                    await handleSyncMessage(messageType, payload, fastify);
                    channel.ack(msg);
                } catch (error) {
                    fastify.log.error(`[RabbitMQ] Error: ${error.message}`);
                    channel.ack(msg); // Ack to drop malformed msg
                }
            }
        });
    } catch (error) {
        fastify.log.error('RabbitMQ connection failed:', error);
    }
};

const handleSyncMessage = async (messageType, data, fastify) => {
    if (!messageType || !data) return;

    if (messageType.includes('EventChangedEvent')) {
        await SearchIndex.updateOne(
            { entityId: data.eventId, entityType: 'Event' },
            {
                $set: {
                    title: data.title,
                    description: data.description,
                    metadata: {
                        organizerId: data.organizerId,
                        bannerUrl: data.bannerUrl,
                        status: data.status,
                        locationName: data.locationName,
                        capacity: data.capacity
                    }
                }
            },
            { upsert: true }
        );
        fastify.log.info(`[Sync] Synced Event: ${data.eventId}`);
    } 
    else if (messageType.includes('UserCreatedEvent') || messageType.includes('UserUpdatedEvent')) {
        await SearchIndex.updateOne(
            { entityId: data.userId, entityType: 'User' },
            {
                $set: {
                    title: data.fullName,
                    description: data.email,
                    metadata: { avatarUrl: data.avatarUrl }
                }
            },
            { upsert: true }
        );
        fastify.log.info(`[Sync] Synced User: ${data.userId}`);
    }
};

module.exports = { connectRabbitMQ };

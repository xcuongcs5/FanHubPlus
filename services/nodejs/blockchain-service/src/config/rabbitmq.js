const amqp = require('amqplib');
const { ethers } = require('ethers');
const Wallet = require('../models/Wallet');
const TicketNFT = require('../models/TicketNFT');

let channel = null;

// Mock Ethers wallet for testing since we don't have real keys
const getOrCreateWallet = async (userId) => {
    let wallet = await Wallet.findOne({ userId });
    if (!wallet) {
        const ethWallet = ethers.Wallet.createRandom();
        wallet = await Wallet.create({
            userId,
            address: ethWallet.address,
            encryptedPrivateKey: ethWallet.privateKey // In prod, encrypt this!
        });
    }
    return wallet;
};

const connectRabbitMQ = async (fastify) => {
    try {
        const connection = await amqp.connect(process.env.RABBITMQ_URL || 'amqp://guest:guest@localhost:5672');
        channel = await connection.createChannel();
        fastify.log.info('RabbitMQ connected...');

        const queueName = 'blockchain.sync.queue';
        await channel.assertQueue(queueName, { durable: true });

        const exchanges = [
            'FanHub.Shared.Contracts.Events:TicketMintRequestedEvent',
            'FanHub.Shared.Contracts.Events:TicketTransferRequestedEvent'
        ];

        for (const ex of exchanges) {
            await channel.assertExchange(ex, 'fanout', { durable: true });
            await channel.bindQueue(queueName, ex, '');
        }

        channel.consume(queueName, async (msg) => {
            if (msg !== null) {
                try {
                    const envelope = JSON.parse(msg.content.toString());
                    const payload = envelope.message;
                    const messageType = envelope.messageType ? envelope.messageType[0] : '';
                    
                    await handleMessage(messageType, payload, fastify);
                    channel.ack(msg);
                } catch (error) {
                    fastify.log.error(`[RabbitMQ] Error: ${error.message}`);
                    channel.ack(msg); // Drop on error for now
                }
            }
        });
    } catch (error) {
        fastify.log.error('RabbitMQ connection failed:', error);
    }
};

const publishEvent = (exchangeName, messageType, data, fastify) => {
    if (!channel) return;
    const envelope = {
        messageId: ethers.hexlify(ethers.randomBytes(16)),
        messageType: [messageType],
        message: data
    };
    channel.publish(exchangeName, '', Buffer.from(JSON.stringify(envelope)));
    fastify.log.info(`[RabbitMQ] Published ${messageType}`);
};

const handleMessage = async (messageType, data, fastify) => {
    if (messageType.includes('TicketMintRequestedEvent')) {
        fastify.log.info(`Processing Mint Request for Booking: ${data.bookingId}`);
        // Ensure user has wallet
        await getOrCreateWallet(data.userId);

        // Save to DB as pending
        const nft = await TicketNFT.create({
            bookingId: data.bookingId,
            eventId: data.eventId,
            ownerUserId: data.userId,
            status: 'PENDING'
        });

        // MOCK MINTING ON BLOCKCHAIN
        setTimeout(async () => {
            const fakeTxHash = ethers.hexlify(ethers.randomBytes(32));
            const fakeTokenId = Math.floor(Math.random() * 10000).toString();
            
            nft.status = 'MINTED';
            nft.txHash = fakeTxHash;
            nft.tokenId = fakeTokenId;
            await nft.save();

            // Notify BookingService
            publishEvent(
                'FanHub.Shared.Contracts.Events:TicketMintResultEvent',
                'urn:message:FanHub.Shared.Contracts.Events:TicketMintResultEvent',
                {
                    bookingId: data.bookingId,
                    succeeded: true,
                    nftTokenId: fakeTokenId,
                    transactionHash: fakeTxHash,
                    error: null
                },
                fastify
            );
        }, 3000); // Simulate network delay
    } 
    else if (messageType.includes('TicketTransferRequestedEvent')) {
        fastify.log.info(`Processing Transfer Request for Booking: ${data.bookingId}`);
        
        // MOCK TRANSFER
        setTimeout(async () => {
            const fakeTxHash = ethers.hexlify(ethers.randomBytes(32));
            
            await TicketNFT.updateOne(
                { bookingId: data.bookingId },
                { ownerUserId: data.toUserId, txHash: fakeTxHash }
            );

            publishEvent(
                'FanHub.Shared.Contracts.Events:TicketTransferResultEvent',
                'urn:message:FanHub.Shared.Contracts.Events:TicketTransferResultEvent',
                {
                    transferId: data.transferId,
                    bookingId: data.bookingId,
                    succeeded: true,
                    transactionHash: fakeTxHash,
                    error: null
                },
                fastify
            );
        }, 3000);
    }
};

module.exports = { connectRabbitMQ };

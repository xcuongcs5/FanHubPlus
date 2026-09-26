require('dotenv').config();
const fastify = require('fastify')({ logger: true });
const mongoose = require('mongoose');
const cors = require('@fastify/cors');

fastify.register(cors, { origin: '*' });

const routes = require('./src/routes/blockchain.routes');
const { connectRabbitMQ } = require('./src/config/rabbitmq');

mongoose.connect(process.env.MONGO_URI)
  .then(() => {
    fastify.log.info('MongoDB connected...');
    connectRabbitMQ(fastify);
  })
  .catch(err => fastify.log.error(err));

fastify.register(routes, { prefix: '/api/v1/blockchain' });

const start = async () => {
    try {
        const port = process.env.PORT || 3004;
        await fastify.listen({ port, host: '0.0.0.0' });
        fastify.log.info(`Blockchain service running on port ${port}`);
    } catch (err) {
        fastify.log.error(err);
        process.exit(1);
    }
};
start();

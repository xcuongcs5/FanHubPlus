require('dotenv').config();
const fastify = require('fastify')({ logger: true });
const mongoose = require('mongoose');
const cors = require('@fastify/cors');

fastify.register(cors, { origin: '*' });

// Swagger
fastify.register(require('@fastify/swagger'), {
  swagger: {
    info: { title: 'FanHub Search Service API', description: 'Search Service API for full-text searching', version: '1.0.0' },
    consumes: ['application/json'],
    produces: ['application/json'],
  }
});

fastify.register(require('@fastify/swagger-ui'), {
  routePrefix: '/swagger',
  uiConfig: { docExpansion: 'list', deepLinking: false }
});

// Routes
const searchRoutes = require('./src/routes/search.routes');
const { connectRabbitMQ } = require('./src/config/rabbitmq');

mongoose.connect(process.env.MONGO_URI)
  .then(() => {
    fastify.log.info('MongoDB connected...');
    connectRabbitMQ(fastify);
  })
  .catch(err => fastify.log.error(err));

fastify.register(searchRoutes, { prefix: '/api/v1/search' });

const start = async () => {
    try {
        const port = process.env.PORT || 3003;
        await fastify.listen({ port, host: '0.0.0.0' });
        await fastify.ready();
        fastify.log.info(`Search service is running on port ${port}`);
    } catch (err) {
        fastify.log.error(err);
        process.exit(1);
    }
};
start();

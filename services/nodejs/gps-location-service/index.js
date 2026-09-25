const fastify = require('fastify')({
  logger: true // Tích hợp sẵn pino logger cực nhanh
});
require('dotenv').config();

const { connectRabbitMQ } = require('./src/config/rabbitmq');
const { startEventConsumer } = require('./src/consumers/event.consumer');
const { verifyGatewayCall } = require('./src/middleware/auth.middleware');

// Đăng ký Rate Limiter (60 requests / 1 phút / IP)
fastify.register(require('@fastify/rate-limit'), {
  max: 60,
  timeWindow: '1 minute',
  errorResponseBuilder: function (request, context) {
    return {
      statusCode: 429,
      error: 'Too Many Requests',
      message: `Bạn chỉ được gửi tối đa ${context.max} request mỗi phút. Hãy thử lại sau.`
    };
  }
});

// Đăng ký plugins
fastify.register(require('@fastify/cors'), {
  origin: '*'
});

// Kiểm tra kết nối Redis khi boot
require('./src/config/redis');

// Khai báo hook để chặn request ngoài API Gateway
fastify.addHook('preHandler', verifyGatewayCall);

// Đăng ký routes
fastify.register(require('./src/routes/location.routes'), { prefix: '/api/v1/locations' });

// Hàm khởi động server
const start = async () => {
  try {
    const port = process.env.PORT || 3001;
    await fastify.listen({ port: port, host: '0.0.0.0' });
    fastify.log.info(`🚀 GPS Location Service đang chạy tại cổng ${port}`);

    // Khởi động RabbitMQ
    await connectRabbitMQ();
    await startEventConsumer();
  } catch (err) {
    fastify.log.error(err);
    process.exit(1);
  }
};

start();

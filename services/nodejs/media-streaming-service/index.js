const fastify = require('fastify')({ logger: true });
require('dotenv').config();

// Đăng ký plugins cơ bản
fastify.register(require('@fastify/cors'), { origin: '*' });

// Đăng ký Multipart để hỗ trợ tính năng upload file lớn
// limits: Giới hạn kích thước file, ví dụ 500MB
fastify.register(require('@fastify/multipart'), {
  limits: {
    fileSize: 500 * 1024 * 1024 // 500MB
  }
});

// Đăng ký Routes
fastify.register(require('./src/routes/media.routes'), { prefix: '/api/v1/media' });

// Hàm boot
const start = async () => {
  try {
    const port = process.env.PORT || 3002;
    await fastify.listen({ port: port, host: '0.0.0.0' });
    fastify.log.info(`🎬 Media Streaming Service đang chạy tại cổng ${port}`);
  } catch (err) {
    fastify.log.error(err);
    process.exit(1);
  }
};

start();

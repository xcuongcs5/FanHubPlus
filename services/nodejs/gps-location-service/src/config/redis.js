const Redis = require('ioredis');
require('dotenv').config();

const redis = new Redis({
  host: process.env.REDIS_HOST || '127.0.0.1',
  port: process.env.REDIS_PORT || 6379,
  // password: process.env.REDIS_PASSWORD || '',
});

redis.on('connect', () => {
  console.log('kết nối Redis thành công');
});

redis.on('error', (err) => {
  console.error('Lỗi kết nối Redis:', err);
});

module.exports = redis;

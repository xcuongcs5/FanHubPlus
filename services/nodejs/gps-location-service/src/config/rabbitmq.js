const amqp = require('amqplib');
require('dotenv').config();

let connection = null;
let channel = null;

const connectRabbitMQ = async () => {
  try {
    const rabbitMqUrl = process.env.RABBITMQ_URL || 'amqp://guest:guest@localhost:5672';
    connection = await amqp.connect(rabbitMqUrl);
    channel = await connection.createChannel();
    
    console.log('✅ Kết nối RabbitMQ thành công');
    return channel;
  } catch (error) {
    console.error('❌ Lỗi kết nối RabbitMQ:', error.message);
    // Tự động kết nối lại sau 5 giây nếu lỗi
    setTimeout(connectRabbitMQ, 5000);
  }
};

const getChannel = () => channel;

module.exports = {
  connectRabbitMQ,
  getChannel
};

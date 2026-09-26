const { getChannel } = require('../config/rabbitmq');
const locationService = require('../services/location.service');

const EXCHANGE_NAME = 'fanhub.events';
const QUEUE_NAME = 'fanhub.events.location.sync';
const ROUTING_KEY = 'event.created';

const startEventConsumer = async () => {
  const channel = getChannel();
  if (!channel) {
    console.warn('RabbitMQ Channel chưa sẵn sàng. Thử lại sau...');
    setTimeout(startEventConsumer, 3000);
    return;
  }

  try {
    // Khai báo Exchange (kiểu direct)
    await channel.assertExchange(EXCHANGE_NAME, 'direct', { durable: true });
    
    // Khai báo Queue
    await channel.assertQueue(QUEUE_NAME, { durable: true });
    
    // Binding Queue vào Exchange bằng Routing Key (bắt cả created và deleted)
    await channel.bindQueue(QUEUE_NAME, EXCHANGE_NAME, 'event.created');
    await channel.bindQueue(QUEUE_NAME, EXCHANGE_NAME, 'event.deleted');

    console.log(`🎧 Đang lắng nghe queue [${QUEUE_NAME}]...`);

    channel.consume(QUEUE_NAME, async (msg) => {
      if (msg !== null) {
        try {
          const routingKey = msg.fields.routingKey;
          const payload = JSON.parse(msg.content.toString());
          
          if (routingKey === 'event.created') {
            if (payload.eventId && payload.lat && payload.lng) {
              await locationService.addEventLocation(payload);
              console.log(`✅ Đã đồng bộ tọa độ sự kiện [${payload.eventId}] vào Redis`);
            }
          } else if (routingKey === 'event.deleted') {
            if (payload.eventId) {
              await locationService.removeEventLocation(payload.eventId);
              console.log(`🗑️ Đã xóa tọa độ sự kiện [${payload.eventId}] khỏi Redis`);
            }
          }

          // Xác nhận đã xử lý xong
          channel.ack(msg);
        } catch (error) {
          console.error('❌ Lỗi xử lý message:', error);
          // Đẩy message lại queue nếu có lỗi để xử lý lại (tuỳ chiến lược)
          channel.nack(msg, false, false);
        }
      }
    });
  } catch (err) {
    console.error('❌ Lỗi thiết lập RabbitMQ Consumer:', err);
  }
};

module.exports = { startEventConsumer };

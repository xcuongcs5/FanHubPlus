const redis = require('../config/redis');

// Tên của khóa (Key) lưu trữ tọa độ trong Redis
const GEO_KEY = 'fanhub:events:locations';
const METADATA_PREFIX = 'fanhub:events:meta:';

class LocationService {
  /**
   * Thêm hoặc cập nhật tọa độ và metadata của một sự kiện
   * @param {Object} payload 
   */
  async addEventLocation(payload) {
    const { eventId, lat, lng, title, coverImage, startTime } = payload;
    
    // Khởi tạo một pipeline để thực hiện nhiều lệnh Redis trong 1 lần (tối ưu tốc độ)
    const pipeline = redis.pipeline();

    // 1. Lưu tọa độ (Redis dùng kinh độ trước, vĩ độ sau)
    pipeline.geoadd(GEO_KEY, parseFloat(lng), parseFloat(lat), eventId);

    // 2. Lưu Metadata dưới dạng Hash nếu có truyền lên
    if (title || coverImage || startTime) {
      pipeline.hset(`${METADATA_PREFIX}${eventId}`, {
        title: title || 'No Title',
        coverImage: coverImage || '',
        startTime: startTime || ''
      });
    }

    // Thực thi pipeline
    await pipeline.exec();
    return { success: true, message: 'Đã lưu tọa độ và dữ liệu sự kiện thành công' };
  }

  /**
   * Tìm kiếm các sự kiện lân cận và lấy metadata
   */
  async getNearbyEvents(longitude, latitude, radius = 10, unit = 'km') {
    // 1. Quét tìm các sự kiện trong bán kính (chỉ lấy ID, Khoảng cách và Tọa độ gốc)
    const nearby = await redis.georadius(
      GEO_KEY, 
      longitude, 
      latitude, 
      radius, 
      unit, 
      'WITHDIST', 
      'WITHCOORD', 
      'ASC' 
    );

    if (nearby.length === 0) return [];

    // 2. Lấy Metadata của từng sự kiện bằng Pipeline
    const pipeline = redis.pipeline();
    nearby.forEach(item => {
      const eventId = item[0];
      pipeline.hgetall(`${METADATA_PREFIX}${eventId}`);
    });

    const metadataResults = await pipeline.exec();

    // 3. Ráp dữ liệu Tọa độ và Metadata lại với nhau
    return nearby.map((item, index) => {
      const metadata = metadataResults[index][1]; // index 1 chứa data từ hgetall
      return {
        eventId: item[0],
        distance: parseFloat(item[1]),
        unit: unit,
        coordinates: {
          longitude: parseFloat(item[2][0]),
          latitude: parseFloat(item[2][1])
        },
        // Spread các trường metadata vào object (nếu có)
        ...(metadata || {})
      };
    });
  }

  async getDistanceToEvent(eventId, longitude, latitude, unit = 'km') {
    const coords = await redis.geopos(GEO_KEY, eventId);
    if (!coords || !coords[0]) return null;
    
    const tempUserKey = `temp_user_${Date.now()}`;
    await redis.geoadd(GEO_KEY, longitude, latitude, tempUserKey);
    const distance = await redis.geodist(GEO_KEY, tempUserKey, eventId, unit);
    await redis.zrem(GEO_KEY, tempUserKey);

    return distance ? parseFloat(distance) : null;
  }

  async removeEventLocation(eventId) {
    const pipeline = redis.pipeline();
    pipeline.zrem(GEO_KEY, eventId);
    pipeline.del(`${METADATA_PREFIX}${eventId}`);
    await pipeline.exec();
    return { success: true, message: 'Đã xóa dữ liệu không gian của sự kiện' };
  }
}

module.exports = new LocationService();

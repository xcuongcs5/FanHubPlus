const locationService = require('../services/location.service');

const getNearbyEvents = async (request, reply) => {
  try {
    const { lat, lng, radius, unit } = request.query;
    if (!lat || !lng) return reply.code(400).send({ error: 'Bad Request', message: 'Thiếu tọa độ lat và lng' });

    const events = await locationService.getNearbyEvents(parseFloat(lng), parseFloat(lat), radius ? parseFloat(radius) : 10, unit || 'km');
    return reply.send({ success: true, data: events });
  } catch (error) {
    request.log.error(error);
    return reply.code(500).send({ error: 'Internal Server Error', message: error.message });
  }
};

const syncEventLocation = async (request, reply) => {
  try {
    const payload = request.body;
    if (!payload.eventId || !payload.lat || !payload.lng) return reply.code(400).send({ error: 'Bad Request', message: 'Thiếu thông tin' });

    const result = await locationService.addEventLocation(payload);
    return reply.code(201).send(result);
  } catch (error) {
    request.log.error(error);
    return reply.code(500).send({ error: 'Internal Server Error', message: error.message });
  }
};

const getDistanceToEvent = async (request, reply) => {
  try {
    const { eventId } = request.params;
    const { lat, lng, unit } = request.query;
    if (!lat || !lng) return reply.code(400).send({ error: 'Bad Request', message: 'Thiếu tọa độ lat/lng' });

    const distance = await locationService.getDistanceToEvent(eventId, parseFloat(lng), parseFloat(lat), unit || 'km');
    if (distance === null) return reply.code(404).send({ error: 'Not Found', message: 'Sự kiện không tồn tại' });
    return reply.send({ success: true, eventId, distance, unit: unit || 'km' });
  } catch (error) {
    request.log.error(error);
    return reply.code(500).send({ error: 'Internal Server Error', message: error.message });
  }
};

const removeEventLocation = async (request, reply) => {
  try {
    const { eventId } = request.params;
    const result = await locationService.removeEventLocation(eventId);
    return reply.send(result);
  } catch (error) {
    request.log.error(error);
    return reply.code(500).send({ error: 'Internal Server Error', message: error.message });
  }
};

module.exports = { getNearbyEvents, syncEventLocation, getDistanceToEvent, removeEventLocation };

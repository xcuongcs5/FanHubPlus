const locationController = require('../controllers/location.controller');

async function locationRoutes(fastify, options) {
  // Schema validation giúp Fastify nhanh hơn nhiều so với Express
  
  fastify.get('/nearby', {
    schema: {
      querystring: {
        type: 'object',
        required: ['lat', 'lng'],
        properties: {
          lat: { type: 'number' },
          lng: { type: 'number' },
          radius: { type: 'number' },
          unit: { type: 'string', enum: ['km', 'm', 'mi', 'ft'] }
        }
      }
    }
  }, locationController.getNearbyEvents);

  fastify.post('/sync', {
    schema: {
      body: {
        type: 'object',
        required: ['eventId', 'lat', 'lng'],
        properties: {
          eventId: { type: 'string' },
          lat: { type: 'number' },
          lng: { type: 'number' }
        }
      }
    }
  }, locationController.syncEventLocation);

  fastify.get('/distance/:eventId', locationController.getDistanceToEvent);
  fastify.delete('/:eventId', locationController.removeEventLocation);
}

module.exports = locationRoutes;

const mediaController = require('../controllers/media.controller');

async function mediaRoutes(fastify, options) {
  
  // 1. Upload Video/Audio
  fastify.post('/upload', mediaController.uploadMedia);

  // 2. Stream Media
  fastify.get('/stream/:filename', mediaController.streamMedia);

  // 3. Info Media
  fastify.get('/info/:filename', mediaController.getMediaInfo);

  // 4. Delete Media
  fastify.delete('/:filename', mediaController.deleteMedia);

}

module.exports = mediaRoutes;

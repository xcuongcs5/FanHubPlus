const searchController = require('../controllers/search.controller');

async function searchRoutes(fastify, options) {
    const searchSchema = {
        schema: {
            description: 'Search across all indexed entities',
            tags: ['Search'],
            querystring: {
                type: 'object',
                properties: {
                    q: { type: 'string', description: 'Search keyword' },
                    type: { type: 'string', description: 'Filter by entity type', enum: ['User', 'Event', 'Post', 'Merchandise'] },
                    page: { type: 'integer', default: 1 },
                    limit: { type: 'integer', default: 10 }
                },
                required: ['q']
            }
        }
    };

    fastify.get('/global', searchSchema, searchController.searchGlobal);
    fastify.get('/users', searchController.searchUsers);
    fastify.get('/events', searchController.searchEvents);
    fastify.get('/posts', searchController.searchPosts);
    fastify.get('/merchandise', searchController.searchMerchandise);
    
    fastify.get('/autocomplete', searchController.autocomplete);

    fastify.post('/internal/sync', searchController.syncData);
    fastify.delete('/internal/index/:indexName', searchController.clearIndex);
}

module.exports = searchRoutes;

const SearchIndex = require('../models/SearchIndex');

// === CORE SEARCH APIs ===

const buildFilter = (q, type) => {
    const filter = {};
    if (q) filter.$text = { $search: q };
    if (type) filter.entityType = type;
    return filter;
};

const executeSearch = async (request, reply, type = null) => {
    try {
        const { q, page = 1, limit = 10 } = request.query;
        // if (!q) return reply.code(400).send({ message: 'Missing query parameter (q)' });
        
        const filter = buildFilter(q, type);
        const skip = (page - 1) * limit;

        let queryObj = SearchIndex.find(filter);
        if (q) {
            queryObj = queryObj.select({ score: { $meta: 'textScore' } })
                               .sort({ score: { $meta: 'textScore' } });
        } else {
            queryObj = queryObj.sort({ createdAt: -1 });
        }

        const results = await queryObj.skip(Number(skip)).limit(Number(limit));
        const total = await SearchIndex.countDocuments(filter);

        return reply.send({
            data: results,
            meta: { total, page: Number(page), limit: Number(limit) }
        });
    } catch (error) {
        request.log.error(error);
        return reply.code(500).send({ message: 'Internal Server Error' });
    }
};

exports.searchGlobal = (request, reply) => executeSearch(request, reply);
exports.searchUsers = (request, reply) => executeSearch(request, reply, 'User');
exports.searchEvents = (request, reply) => executeSearch(request, reply, 'Event');
exports.searchPosts = (request, reply) => executeSearch(request, reply, 'Post');
exports.searchMerchandise = (request, reply) => executeSearch(request, reply, 'Merchandise');

// === AUTOCOMPLETE API ===

exports.autocomplete = async (request, reply) => {
    try {
        const { q, type } = request.query;
        if (!q || q.length < 2) return reply.send({ data: [] });

        const regex = new RegExp(q, 'i');
        const filter = { $or: [{ title: regex }, { tags: regex }] };
        if (type) filter.entityType = type;

        const results = await SearchIndex.find(filter)
            .limit(5)
            .select('title entityType');

        return reply.send({ data: results });
    } catch (error) {
        request.log.error(error);
        return reply.code(500).send({ message: 'Internal Server Error' });
    }
};

// === INTERNAL SYNC APIs ===

exports.syncData = async (request, reply) => {
    try {
        // Trong thuc te, api nay se ban message qua RabbitMQ hoac lay tu API khac.
        // O day minh nhan raw body de test.
        const items = request.body;
        if (!Array.isArray(items)) {
            return reply.code(400).send({ message: 'Body must be an array of search items.' });
        }

        const operations = items.map(item => ({
            updateOne: {
                filter: { entityId: item.entityId, entityType: item.entityType },
                update: { $set: item },
                upsert: true
            }
        }));

        await SearchIndex.bulkWrite(operations);
        return reply.send({ message: 'Synced successfully', count: items.length });
    } catch (error) {
        request.log.error(error);
        return reply.code(500).send({ message: 'Internal Server Error' });
    }
};

exports.clearIndex = async (request, reply) => {
    try {
        const { indexName } = request.params; // indexName o day tuong duong entityType
        if (!['User', 'Event', 'Post', 'Merchandise', 'All'].includes(indexName)) {
            return reply.code(400).send({ message: 'Invalid index name' });
        }

        const filter = indexName === 'All' ? {} : { entityType: indexName };
        const result = await SearchIndex.deleteMany(filter);

        return reply.send({ message: Cleared index: , deletedCount: result.deletedCount });
    } catch (error) {
        request.log.error(error);
        return reply.code(500).send({ message: 'Internal Server Error' });
    }
};

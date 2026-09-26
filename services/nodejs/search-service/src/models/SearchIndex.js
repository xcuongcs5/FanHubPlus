const mongoose = require('mongoose');

const SearchIndexSchema = new mongoose.Schema({
    entityId: { type: String, required: true },
    entityType: { type: String, enum: ['User', 'Event', 'Post', 'Merchandise'], required: true },
    title: { type: String, required: true },
    description: { type: String },
    tags: [{ type: String }],
    metadata: { type: mongoose.Schema.Types.Mixed }, 
    createdAt: { type: Date, default: Date.now },
});

SearchIndexSchema.index({ title: 'text', description: 'text', tags: 'text' });
SearchIndexSchema.index({ entityType: 1 });

module.exports = mongoose.model('SearchIndex', SearchIndexSchema);

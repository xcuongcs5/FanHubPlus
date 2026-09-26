const mongoose = require('mongoose');

const TicketNFTSchema = new mongoose.Schema({
    bookingId: { type: String, required: true, unique: true },
    eventId: { type: String, required: true },
    ownerUserId: { type: String, required: true },
    tokenId: { type: String }, // Assign after minting
    txHash: { type: String },
    status: { type: String, enum: ['PENDING', 'MINTED', 'FAILED'], default: 'PENDING' },
    createdAt: { type: Date, default: Date.now }
});

module.exports = mongoose.model('TicketNFT', TicketNFTSchema);

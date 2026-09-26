const mongoose = require('mongoose');

const WalletSchema = new mongoose.Schema({
    userId: { type: String, required: true, unique: true },
    address: { type: String, required: true, unique: true },
    encryptedPrivateKey: { type: String, required: true },
    createdAt: { type: Date, default: Date.now }
});

module.exports = mongoose.model('Wallet', WalletSchema);

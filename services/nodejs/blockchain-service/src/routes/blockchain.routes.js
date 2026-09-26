const controller = require('../controllers/blockchain.controller');

async function blockchainRoutes(fastify, options) {
    fastify.get('/wallets/me', controller.getMyWallet);
    fastify.get('/nfts/me', controller.getMyNFTs);
}

module.exports = blockchainRoutes;

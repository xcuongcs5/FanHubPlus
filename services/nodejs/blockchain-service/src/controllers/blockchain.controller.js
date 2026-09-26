const Wallet = require("../models/Wallet");
const TicketNFT = require("../models/TicketNFT");

exports.getMyWallet = async (request, reply) => {
  try {
    const userId = request.query.userId;
    if (!userId) return reply.code(400).send({ message: "Missing userId" });

    const wallet = await Wallet.findOne({ userId }).select(
      "-encryptedPrivateKey",
    );
    if (!wallet) return reply.code(404).send({ message: "Wallet not found" });

    return reply.send({ data: wallet });
  } catch (error) {
    request.log.error(error);
    return reply.code(500).send({ message: "Internal Server Error" });
  }
};

exports.getMyNFTs = async (request, reply) => {
  try {
    const userId = request.query.userId;
    if (!userId) return reply.code(400).send({ message: "Missing userId" });

    const nfts = await TicketNFT.find({ ownerUserId: userId });
    return reply.send({ data: nfts });
  } catch (error) {
    request.log.error(error);
    return reply.code(500).send({ message: "Internal Server Error" });
  }
};

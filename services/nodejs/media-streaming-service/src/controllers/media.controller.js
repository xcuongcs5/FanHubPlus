const streamService = require('../services/stream.service');

const uploadMedia = async (req, reply) => {
  try {
    const result = await streamService.uploadFile(req);
    return reply.code(201).send({
      success: true,
      message: 'Upload thành công',
      data: result
    });
  } catch (error) {
    req.log.error(error);
    return reply.code(500).send({ error: 'Lỗi upload', message: error.message });
  }
};

const streamMedia = async (req, reply) => {
  const { filename } = req.params;
  try {
    // Lưu ý: streamFile sẽ sử dụng reply.raw để pump luồng trực tiếp nên ta ko gọi reply.send ở đây nữa
    streamService.streamFile(req, reply, filename);
  } catch (error) {
    req.log.error(error);
    return reply.code(500).send({ error: 'Lỗi stream', message: error.message });
  }
};

const getMediaInfo = async (req, reply) => {
  const { filename } = req.params;
  try {
    const info = streamService.getFileInfo(filename);
    return reply.send({ success: true, data: info });
  } catch (error) {
    return reply.code(404).send({ error: 'Not Found', message: error.message });
  }
};

const deleteMedia = async (req, reply) => {
  const { filename } = req.params;
  try {
    const result = streamService.deleteFile(filename);
    return reply.send(result);
  } catch (error) {
    return reply.code(404).send({ error: 'Not Found', message: error.message });
  }
};

module.exports = {
  uploadMedia,
  streamMedia,
  getMediaInfo,
  deleteMedia
};

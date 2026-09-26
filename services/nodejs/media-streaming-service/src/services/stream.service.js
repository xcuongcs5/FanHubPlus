const fs = require('fs');
const path = require('path');
const { pipeline } = require('stream');
const util = require('util');
const pump = util.promisify(pipeline);

const STORAGE_PATH = path.join(__dirname, '../storage');

// Đảm bảo thư mục storage luôn tồn tại
if (!fs.existsSync(STORAGE_PATH)) {
  fs.mkdirSync(STORAGE_PATH, { recursive: true });
}

class StreamService {
  
  // 1. Upload File (Lưu file chunk by chunk để không tốn RAM)
  async uploadFile(req) {
    const data = await req.file();
    if (!data) throw new Error('Không tìm thấy file trong request');

    const fileName = `${Date.now()}-${data.filename.replace(/[^a-zA-Z0-9.-]/g, '_')}`;
    const filePath = path.join(STORAGE_PATH, fileName);

    // Lưu stream vào đĩa
    await pump(data.file, fs.createWriteStream(filePath));
    
    return { 
      fileName, 
      mimetype: data.mimetype,
      url: `/api/v1/media/stream/${fileName}`
    };
  }

  // 2. Stream File (HTTP 206 Partial Content)
  streamFile(req, reply, fileName) {
    const filePath = path.join(STORAGE_PATH, fileName);

    // Kiểm tra file tồn tại
    if (!fs.existsSync(filePath)) {
      return reply.code(404).send({ error: 'File không tồn tại' });
    }

    const stat = fs.statSync(filePath);
    const fileSize = stat.size;
    const range = req.headers.range;

    // Xác định MimeType cơ bản (Nên dùng thư viện mime-types nếu làm Production)
    let contentType = 'application/octet-stream';
    if (fileName.endsWith('.mp4')) contentType = 'video/mp4';
    if (fileName.endsWith('.mp3')) contentType = 'audio/mpeg';

    if (range) {
      // Xử lý Byte-Range
      const parts = range.replace(/bytes=/, "").split("-");
      const start = parseInt(parts[0], 10);
      
      // Nếu end ko có, lấy đến hết file
      const end = parts[1] ? parseInt(parts[1], 10) : fileSize - 1;
      
      // Kích thước của đoạn (chunk)
      const chunksize = (end - start) + 1;
      const fileStream = fs.createReadStream(filePath, { start, end });

      reply.raw.writeHead(206, {
        'Content-Range': `bytes ${start}-${end}/${fileSize}`,
        'Accept-Ranges': 'bytes',
        'Content-Length': chunksize,
        'Content-Type': contentType,
      });

      // Bơm stream trực tiếp vào raw HTTP response
      pump(fileStream, reply.raw);
    } else {
      // Nếu Client ko yêu cầu Range (như tải file bth)
      reply.header('Content-Length', fileSize);
      reply.header('Content-Type', contentType);
      const fileStream = fs.createReadStream(filePath);
      return reply.send(fileStream);
    }
  }

  // 3. Lấy thông tin Metadata
  getFileInfo(fileName) {
    const filePath = path.join(STORAGE_PATH, fileName);
    if (!fs.existsSync(filePath)) {
      throw new Error('File không tồn tại');
    }
    const stat = fs.statSync(filePath);
    return {
      fileName,
      sizeBytes: stat.size,
      sizeMB: (stat.size / (1024 * 1024)).toFixed(2),
      createdAt: stat.birthtime
    };
  }

  // 4. Xóa File
  deleteFile(fileName) {
    const filePath = path.join(STORAGE_PATH, fileName);
    if (!fs.existsSync(filePath)) {
      throw new Error('File không tồn tại');
    }
    fs.unlinkSync(filePath);
    return { success: true, message: 'Đã xóa file thành công' };
  }
}

module.exports = new StreamService();

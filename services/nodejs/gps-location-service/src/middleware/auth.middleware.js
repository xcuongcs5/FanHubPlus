const verifyGatewayCall = (request, reply, done) => {
  // Đọc header X-Gateway-Secret từ yêu cầu
  const gatewaySecret = request.headers['x-gateway-secret'];
  
  // Bạn có thể thiết lập GATEWAY_SECRET trong .env
  // Ở môi trường dev nếu không có GATEWAY_SECRET, tạm bỏ qua để dễ test
  const expectedSecret = process.env.GATEWAY_SECRET;

  if (expectedSecret && gatewaySecret !== expectedSecret) {
    return reply.code(403).send({
      error: 'Forbidden',
      message: 'Chỉ chấp nhận request từ API Gateway'
    });
  }

  // Tùy chọn: Lấy X-User-Id nếu bạn cần biết User nào đang tìm kiếm để ghi Log/Analytics
  const userId = request.headers['x-user-id'];
  if (userId) {
    request.user = { id: userId };
  }

  done();
};

module.exports = {
  verifyGatewayCall
};

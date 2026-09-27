<?php

namespace Tests\Feature;

use App\Models\Faq;
use App\Models\Feedback;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class AdminFeedbackAndFaqApiTest extends TestCase
{
    use RefreshDatabase;

    private function generateAdminJwt(array $claims = []): string
    {
        $header = base64_encode(json_encode(['typ' => 'JWT', 'alg' => 'none']));
        $header = str_replace(['+', '/', '='], ['-', '_', ''], $header);

        $defaultClaims = [
            'sub' => 'admin-root',
            'role' => 'admin',
            'exp' => time() + 3600,
        ];

        $payload = base64_encode(json_encode(array_merge($defaultClaims, $claims)));
        $payload = str_replace(['+', '/', '='], ['-', '_', ''], $payload);

        return "{$header}.{$payload}.testsignature";
    }

    public function test_feedbacks_require_admin_jwt(): void
    {
        $response = $this->getJson('/api/v1/admin/feedbacks');
        $response->assertStatus(401);
    }

    public function test_get_feedbacks_sample_when_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/feedbacks?type=bug&status=Open&page=1&limit=20');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'fb_xxx',
                        'user' => 'User C',
                        'type' => 'bug',
                        'title' => 'Lỗi thanh toán MoMo',
                        'status' => 'Open',
                        'created_at' => '2026-09-26',
                    ],
                ],
            ]);
    }

    public function test_get_feedbacks_from_database(): void
    {
        $token = $this->generateAdminJwt();

        Feedback::create([
            'id' => 'fb_001',
            'user_name' => 'Nguyen Van X',
            'type' => 'suggestion',
            'title' => 'Gợi ý thêm tính năng darkmode',
            'status' => 'Open',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/feedbacks?type=suggestion&status=Open&page=1&limit=20');

        $response->assertStatus(200);
        $this->assertCount(1, $response->json('data'));
        $this->assertEquals('fb_001', $response->json('data.0.id'));
        $this->assertEquals('Nguyen Van X', $response->json('data.0.user'));
        $this->assertEquals('suggestion', $response->json('data.0.type'));
    }

    public function test_get_feedback_detail_sample_mock(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/feedbacks/fb_xxx');

        $response->assertStatus(200)
            ->assertJson([
                'id' => 'fb_xxx',
                'user' => [
                    'id' => 'usr_xxx',
                    'name' => 'User C',
                    'email' => 'c@gmail.com',
                ],
                'type' => 'bug',
                'content' => 'Chi tiết lỗi...',
                'screenshot_url' => 'https://...',
                'status' => 'Open',
            ]);
    }

    public function test_get_feedback_detail_from_database(): void
    {
        $token = $this->generateAdminJwt();

        $fb = Feedback::create([
            'id' => 'fb_123',
            'user_id' => 'usr_007',
            'user_name' => 'Le Van Y',
            'user_email' => 'y@gmail.com',
            'type' => 'bug',
            'title' => 'Không load được ảnh vé',
            'content' => 'Ảnh vé hiển thị màn hình trắng khi tải',
            'screenshot_url' => 'https://image.com/shot.png',
            'status' => 'Open',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson("/api/v1/admin/feedbacks/{$fb->id}");

        $response->assertStatus(200)
            ->assertJson([
                'id' => 'fb_123',
                'user' => [
                    'id' => 'usr_007',
                    'name' => 'Le Van Y',
                    'email' => 'y@gmail.com',
                ],
                'type' => 'bug',
                'content' => 'Ảnh vé hiển thị màn hình trắng khi tải',
                'screenshot_url' => 'https://image.com/shot.png',
                'status' => 'Open',
            ]);
    }

    public function test_update_feedback_status_mock_and_db(): void
    {
        $token = $this->generateAdminJwt();

        // Test with mock fb_xxx
        $mockResp = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/feedbacks/fb_xxx/status', [
                'status' => 'Resolved',
                'response_note' => 'Lỗi đã được đội kỹ thuật khắc phục',
            ]);

        $mockResp->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật tiến độ xử lý và gửi phản hồi thành công',
            ]);

        // Test with real DB feedback
        $fb = Feedback::create([
            'id' => 'fb_999',
            'title' => 'Lỗi thanh toán Momo',
            'status' => 'In_Progress',
        ]);

        $realResp = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson("/api/v1/admin/feedbacks/{$fb->id}/status", [
                'status' => 'Closed',
                'response_note' => 'Đã khắc phục hoàn toàn',
            ]);

        $realResp->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật tiến độ xử lý và gửi phản hồi thành công',
            ]);

        $fb->refresh();
        $this->assertEquals('Closed', $fb->status);
        $this->assertEquals('Đã khắc phục hoàn toàn', $fb->response_note);
    }

    public function test_chatbot_faqs_require_admin_jwt(): void
    {
        $response = $this->getJson('/api/v1/admin/chatbot/faqs');
        $response->assertStatus(401);
    }

    public function test_get_chatbot_faqs_sample_when_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/chatbot/faqs?search=ve&page=1&limit=20');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'faq_xxx',
                        'question' => 'Làm thế nào để lấy vé NFT?',
                        'answer' => 'Sau khi thanh toán...',
                        'is_active' => true,
                    ],
                ],
            ]);
    }

    public function test_create_chatbot_faq(): void
    {
        $token = $this->generateAdminJwt();

        $payload = [
            'question' => 'Quy định hoàn tiền vé?',
            'answer' => 'Bạn có thể gửi yêu cầu trước 48h...',
            'category' => 'Booking',
            'is_active' => true,
        ];

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/chatbot/faqs', $payload);

        $response->assertStatus(201)
            ->assertJsonStructure([
                'id',
                'message',
            ]);

        $this->assertEquals('Thêm câu hỏi FAQ thành công', $response->json('message'));
        $this->assertDatabaseHas('cms_faqs', [
            'question' => 'Quy định hoàn tiền vé?',
            'category' => 'Booking',
        ]);
    }

    public function test_update_chatbot_faq(): void
    {
        $token = $this->generateAdminJwt();

        // Update mock
        $mockResp = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/chatbot/faqs/faq_xxx', [
                'question' => 'Quy định hoàn tiền vé mới nhất',
                'answer' => 'Nội dung cập nhật...',
                'is_active' => true,
            ]);

        $mockResp->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật câu hỏi FAQ thành công',
            ]);

        // Update real db record
        $faq = Faq::create([
            'id' => 'faq_001',
            'question' => 'Câu hỏi cũ',
            'answer' => 'Trả lời cũ',
            'is_active' => false,
        ]);

        $realResp = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson("/api/v1/admin/chatbot/faqs/{$faq->id}", [
                'question' => 'Quy định hoàn tiền vé mới nhất',
                'answer' => 'Nội dung cập nhật...',
                'is_active' => true,
            ]);

        $realResp->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật câu hỏi FAQ thành công',
            ]);

        $faq->refresh();
        $this->assertEquals('Quy định hoàn tiền vé mới nhất', $faq->question);
        $this->assertEquals('Nội dung cập nhật...', $faq->answer);
        $this->assertTrue($faq->is_active);
    }

    public function test_delete_chatbot_faq(): void
    {
        $token = $this->generateAdminJwt();

        // Delete mock
        $mockResp = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/chatbot/faqs/faq_xxx');

        $mockResp->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa câu hỏi khỏi kho tri thức',
            ]);

        // Delete real record
        $faq = Faq::create([
            'id' => 'faq_002',
            'question' => 'Câu hỏi cần xóa',
            'answer' => 'Trả lời cần xóa',
            'is_active' => true,
        ]);

        $realResp = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson("/api/v1/admin/chatbot/faqs/{$faq->id}");

        $realResp->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa câu hỏi khỏi kho tri thức',
            ]);

        $this->assertDatabaseMissing('cms_faqs', ['id' => 'faq_002']);
    }
}

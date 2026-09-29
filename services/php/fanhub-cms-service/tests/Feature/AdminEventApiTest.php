<?php

namespace Tests\Feature;

use App\Models\Event;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class AdminEventApiTest extends TestCase
{
    use RefreshDatabase;

    private function generateJwt(array $claims = []): string
    {
        $header = base64_encode(json_encode(['typ' => 'JWT', 'alg' => 'none']));
        $header = str_replace(['+', '/', '='], ['-', '_', ''], $header);

        $payload = base64_encode(json_encode($claims));
        $payload = str_replace(['+', '/', '='], ['-', '_', ''], $payload);

        return "{$header}.{$payload}.testsignature";
    }

    private function generateAdminJwt(array $claims = []): string
    {
        $defaultClaims = [
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'admin-evt-123',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'Admin',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    private function generateUserJwt(array $claims = []): string
    {
        $defaultClaims = [
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'user-evt-456',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'User',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    public function test_can_get_events_sample_when_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/events?status=Pending&page=1&limit=20');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'evt_xxx',
                        'title' => 'Cosplay Expo 2026',
                        'organizer' => 'Otaku Club',
                        'status' => 'Pending',
                        'start_time' => '2026-11-01',
                    ],
                ],
                'meta' => [
                    'total' => 12,
                ],
            ]);
    }

    public function test_can_get_events_from_database(): void
    {
        $token = $this->generateAdminJwt();

        $event = Event::create([
            'id' => 'evt_cosplay_01',
            'title' => 'Cosplay Expo 2026',
            'organizer' => 'Otaku Club',
            'status' => 'Pending',
            'start_time' => '2026-11-01',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/events?status=Pending&page=1&limit=20');

        $response->assertStatus(200);
        $data = $response->json('data');

        $this->assertCount(1, $data);
        $this->assertEquals('evt_cosplay_01', $data[0]['id']);
        $this->assertEquals('Cosplay Expo 2026', $data[0]['title']);
        $this->assertEquals('Otaku Club', $data[0]['organizer']);
        $this->assertEquals('Pending', $data[0]['status']);
    }

    public function test_can_get_single_event_details(): void
    {
        $token = $this->generateAdminJwt();

        $event = Event::create([
            'id' => 'evt_cosplay_detail',
            'title' => 'Cosplay Expo 2026',
            'organizer' => 'Otaku Club',
            'organizer_email' => 'contact@club.vn',
            'location' => 'SECC Q7',
            'ticket_types_json' => json_encode([
                [
                    'name' => 'VIP',
                    'price' => 500000,
                    'total' => 200,
                ],
            ]),
            'status' => 'Pending',
            'ai_risk_score' => 0.05,
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/events/' . $event->id);

        $response->assertStatus(200)
            ->assertJson([
                'id' => 'evt_cosplay_detail',
                'title' => 'Cosplay Expo 2026',
                'organizer' => [
                    'name' => 'Otaku Club',
                    'email' => 'contact@club.vn',
                ],
                'location' => 'SECC Q7',
                'ticket_types' => [
                    [
                        'name' => 'VIP',
                        'price' => 500000,
                        'total' => 200,
                    ],
                ],
                'status' => 'Pending',
                'ai_risk_score' => 0.05,
            ]);
    }

    public function test_can_get_mock_single_event(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/events/evt_xxx');

        $response->assertStatus(200)
            ->assertJson([
                'id' => 'evt_xxx',
                'title' => 'Cosplay Expo 2026',
                'organizer' => [
                    'name' => 'Otaku Club',
                    'email' => 'contact@club.vn',
                ],
                'location' => 'SECC Q7',
                'ticket_types' => [
                    [
                        'name' => 'VIP',
                        'price' => 500000,
                        'total' => 200,
                    ],
                ],
                'status' => 'Pending',
                'ai_risk_score' => 0.05,
            ]);
    }

    public function test_can_review_event(): void
    {
        $token = $this->generateAdminJwt();

        $event = Event::create([
            'title' => 'To Review',
            'status' => 'Pending',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/events/' . $event->id . '/review', [
                'status' => 'Approved',
                'admin_note' => 'Hồ sơ giấy phép địa điểm hợp lệ',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã duyệt/từ chối sự kiện thành công',
            ]);

        $this->assertEquals('Approved', $event->fresh()->status);
        $this->assertEquals('Hồ sơ giấy phép địa điểm hợp lệ', $event->fresh()->admin_note);
    }

    public function test_can_review_mock_event(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/events/evt_xxx/review', [
                'status' => 'Approved',
                'admin_note' => 'Hợp lệ',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã duyệt/từ chối sự kiện thành công',
            ]);
    }

    public function test_can_delete_event(): void
    {
        $token = $this->generateAdminJwt();

        $event = Event::create([
            'title' => 'Violating Event',
            'status' => 'Flagged',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/events/' . $event->id);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã gỡ sự kiện vi phạm khỏi hệ thống',
            ]);

        $this->assertDatabaseMissing('events', [
            'id' => $event->id,
        ]);
    }

    public function test_can_delete_mock_event(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/events/evt_xxx');

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã gỡ sự kiện vi phạm khỏi hệ thống',
            ]);
    }

    public function test_events_require_admin_jwt(): void
    {
        $userToken = $this->generateUserJwt();

        // No token
        $this->withHeader('Authorization', '')
            ->getJson('/api/v1/admin/events')
            ->assertStatus(401);

        $this->withHeader('Authorization', '')
            ->putJson('/api/v1/admin/events/evt_123/review', ['status' => 'Approved'])
            ->assertStatus(401);

        $this->withHeader('Authorization', '')
            ->deleteJson('/api/v1/admin/events/evt_123')
            ->assertStatus(401);

        // Non-admin token
        $this->withHeader('Authorization', 'Bearer ' . $userToken)
            ->getJson('/api/v1/admin/events')
            ->assertStatus(403);
    }

    public function test_can_create_event(): void
    {
        $token = $this->generateAdminJwt();

        $payload = [
            'title' => 'Cosplay Festival 2026',
            'organizer' => [
                'name' => 'Otaku Club',
                'email' => 'contact@club.vn',
            ],
            'location' => 'SECC Q7 HCM',
            'start_time' => '2026-10-15',
            'description' => 'Lễ hội cosplay thường niên',
            'ticket_types' => [
                ['name' => 'Vé tiêu chuẩn', 'price' => 150000, 'total' => 1000],
                ['name' => 'Vé VIP', 'price' => 500000, 'total' => 100],
            ],
        ];

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/events', $payload);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Tạo sự kiện thành công',
                'data' => [
                    'title' => 'Cosplay Festival 2026',
                    'location' => 'SECC Q7 HCM',
                    'start_time' => '2026-10-15',
                    'status' => 'Pending',
                ],
            ]);

        $this->assertDatabaseHas('events', [
            'title' => 'Cosplay Festival 2026',
            'organizer' => 'Otaku Club',
            'organizer_email' => 'contact@club.vn',
        ]);
    }

    public function test_can_update_event(): void
    {
        $token = $this->generateAdminJwt();

        $event = Event::create([
            'id' => 'evt_update_target',
            'title' => 'Old Title',
            'organizer' => 'Old Organizer',
            'status' => 'Pending',
        ]);

        $payload = [
            'title' => 'xin chào',
            'organizer' => [
                'name' => 'adsfdsgsd',
                'email' => '',
            ],
            'location' => 'Quận 1',
            'start_time' => '2026-09-29',
            'end_time' => '',
            'banner_url' => '',
            'description' => 'Mô tả cập nhật',
            'ticket_types' => [
                ['name' => 'Standard', 'price' => 150000, 'total' => 500],
            ],
        ];

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/events/' . $event->id, $payload);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật sự kiện thành công',
                'data' => [
                    'id' => $event->id,
                    'title' => 'xin chào',
                    'location' => 'Quận 1',
                    'start_time' => '2026-09-29',
                ],
            ]);

        $this->assertEquals('xin chào', $event->fresh()->title);
        $this->assertEquals('adsfdsgsd', $event->fresh()->organizer);
    }
}



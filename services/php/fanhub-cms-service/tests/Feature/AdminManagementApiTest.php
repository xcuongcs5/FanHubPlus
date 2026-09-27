<?php

namespace Tests\Feature;

use App\Models\Event;
use App\Models\FinancialReport;
use App\Models\UserProjection;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class AdminManagementApiTest extends TestCase
{
    use RefreshDatabase;

    private function generateAdminJwt(): string
    {
        $header = base64_encode(json_encode(['typ' => 'JWT', 'alg' => 'none']));
        $header = str_replace(['+', '/', '='], ['-', '_', ''], $header);

        $payload = base64_encode(json_encode([
            'sub' => 'admin-root',
            'role' => 'admin',
            'exp' => time() + 3600,
        ]));
        $payload = str_replace(['+', '/', '='], ['-', '_', ''], $payload);

        return "{$header}.{$payload}.testsignature";
    }

    public function test_can_get_dashboard_overview(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/overview?page=1&limit=20&sort=newest');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'data' => [
                    '*' => [
                        'id',
                        'title',
                    ],
                ],
                'meta' => [
                    'total',
                    'page',
                    'limit',
                ],
            ]);
    }

    public function test_can_get_admin_users(): void
    {
        $token = $this->generateAdminJwt();

        UserProjection::create([
            'id' => 'usr_001',
            'full_name' => 'Quản lý người dùng',
            'email' => 'user@test.com',
            'status' => 'active',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/users?page=1&limit=20&sort=newest');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'data' => [
                    '*' => [
                        'id',
                        'title',
                    ],
                ],
                'meta' => [
                    'total',
                    'page',
                    'limit',
                ],
            ]);
    }

    public function test_can_ban_user(): void
    {
        $token = $this->generateAdminJwt();

        $user = UserProjection::create([
            'id' => 'usr_target',
            'full_name' => 'Target User',
            'status' => 'active',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/users/' . $user->id . '/ban', [
                'title' => 'Cập nhật dữ liệu',
                'status' => 'updated',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật thành công',
            ]);

        $this->assertDatabaseHas('users_projection', [
            'id' => $user->id,
            'status' => 'updated',
        ]);
    }

    public function test_can_get_pending_events(): void
    {
        $token = $this->generateAdminJwt();

        Event::create([
            'id' => 'evt_1',
            'title' => 'Sự kiện chờ duyệt',
            'status' => 'pending',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/events/pending?page=1&limit=20&sort=newest');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'data' => [
                    '*' => [
                        'id',
                        'title',
                    ],
                ],
                'meta' => [
                    'total',
                    'page',
                    'limit',
                ],
            ]);
    }

    public function test_can_approve_event(): void
    {
        $token = $this->generateAdminJwt();

        $event = Event::create([
            'id' => 'evt_approve_target',
            'title' => 'Sự kiện mở bán',
            'status' => 'pending',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/events/' . $event->id . '/approve', [
                'title' => 'Dữ liệu mẫu',
                'description' => 'Chi tiết Duyệt sự kiện mở bán',
                'status' => 'active',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Duyệt sự kiện mở bán thành công',
            ])
            ->assertJsonStructure([
                'id',
                'message',
            ]);

        $this->assertDatabaseHas('events', [
            'id' => $event->id,
            'status' => 'active',
        ]);
    }

    public function test_can_get_financial_reports(): void
    {
        $token = $this->generateAdminJwt();

        FinancialReport::create([
            'id' => 'fin_1',
            'title' => 'Báo cáo doanh thu',
            'revenue' => 50000000,
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/financial/reports?page=1&limit=20&sort=newest');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'data' => [
                    '*' => [
                        'id',
                        'title',
                    ],
                ],
                'meta' => [
                    'total',
                    'page',
                    'limit',
                ],
            ]);
    }
}

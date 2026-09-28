<?php

namespace Tests\Feature;

use App\Models\Event;
use App\Models\FinancialReport;
use App\Models\Post;
use App\Models\UserProjection;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Tests\TestCase;

class AdminManagementApiTest extends TestCase
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

    public function test_can_get_dashboard_overview_with_spec_defaults(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/overview?period=month');

        $response->assertStatus(200)
            ->assertExactJson([
                'total_users' => 15200,
                'active_users' => 3400,
                'total_events' => 48,
                'pending_events' => 5,
                'total_revenue' => 150000000,
                'total_posts' => 1250,
            ]);
    }

    public function test_can_get_dashboard_overview_with_database_data(): void
    {
        $token = $this->generateAdminJwt();

        // Tạo dữ liệu thực tế
        UserProjection::create([
            'id' => 'u1',
            'full_name' => 'Active User 1',
            'status' => 'active',
        ]);
        UserProjection::create([
            'id' => 'u2',
            'full_name' => 'Active User 2',
            'status' => 'active',
        ]);
        UserProjection::create([
            'id' => 'u3',
            'full_name' => 'Banned User',
            'status' => 'banned',
        ]);

        Event::create([
            'id' => 'e1',
            'title' => 'Event 1',
            'status' => 'pending',
        ]);
        Event::create([
            'id' => 'e2',
            'title' => 'Event 2',
            'status' => 'active',
        ]);

        FinancialReport::create([
            'id' => 'f1',
            'title' => 'Report 1',
            'revenue' => 5000000,
        ]);

        Post::create([
            'id' => 'p1',
            'user_id' => 'u1',
            'title' => 'Post 1',
            'body' => 'Body 1',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/overview');

        $response->assertStatus(200)
            ->assertExactJson([
                'total_users' => 3,
                'active_users' => 2,
                'total_events' => 2,
                'pending_events' => 1,
                'total_revenue' => 5000000,
                'total_posts' => 1,
            ]);
    }

    public function test_admin_auth_via_dotnet_role_claim(): void
    {
        // Token dùng claim của ASP.NET Identity (ClaimTypes.Role)
        $token = $this->generateAdminJwt([
            'sub' => 'dotnet-user-guid',
            'role' => null,
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'Admin',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/overview');

        $response->assertStatus(200);
    }

    public function test_admin_auth_via_admin_role_guid(): void
    {
        // Token chứa RoleId dạng GUID của Admin
        $token = $this->generateAdminJwt([
            'sub' => 'guid-user-id',
            'role' => '11111111-1111-1111-1111-111111111111',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/overview');

        $response->assertStatus(200);
    }

    public function test_non_admin_user_is_forbidden(): void
    {
        $userId = 'acc-normal-user-456';

        $token = $this->generateAdminJwt([
            'sub' => $userId,
            'role' => 'User',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/overview');

        $response->assertStatus(403)
            ->assertJson([
                'message' => 'Bạn không có quyền quản trị viên (Admin role required).',
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

    public function test_can_get_single_user(): void
    {
        $token = $this->generateAdminJwt();

        $user = UserProjection::create([
            'id' => 'usr_xxx',
            'full_name' => 'Nguyen Van A',
            'email' => 'a@gmail.com',
            'role' => 'EventOwner',
            'status' => 'active',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/users/' . $user->id);

        $response->assertStatus(200)
            ->assertJson([
                'id' => 'usr_xxx',
                'full_name' => 'Nguyen Van A',
                'email' => 'a@gmail.com',
                'roles' => ['EventOwner'],
                'status' => 'Active',
            ])
            ->assertJsonStructure([
                'id',
                'full_name',
                'email',
                'roles',
                'status',
                'stats' => [
                    'total_orders',
                    'total_posts',
                ],
            ]);
    }

    public function test_can_create_admin_user(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/users', [
                'email' => 'mod@fanhub.com',
                'password' => 'SecurePassword123@',
                'full_name' => 'Moderator Name',
                'role' => 'Moderator',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Tạo tài khoản quản trị thành công',
            ])
            ->assertJsonStructure([
                'id',
                'message',
            ]);
    }

    public function test_can_update_user_status(): void
    {
        $token = $this->generateAdminJwt();

        $user = UserProjection::create([
            'id' => 'usr_status_test',
            'full_name' => 'Test User',
            'status' => 'active',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/users/' . $user->id . '/status', [
                'status' => 'Banned',
                'reason' => 'Vi phạm tiêu chuẩn cộng đồng, spam liên kết',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật trạng thái tài khoản thành công',
            ]);

        $this->assertDatabaseHas('users_projection', [
            'id' => $user->id,
            'status' => 'Banned',
        ]);
    }

    public function test_can_update_user_roles(): void
    {
        $token = $this->generateAdminJwt();

        $user = UserProjection::create([
            'id' => 'usr_role_test',
            'full_name' => 'Role Test User',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/users/' . $user->id . '/roles', [
                'role' => 'EventOwner',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Phân quyền người dùng thành công',
            ]);
    }

    public function test_can_delete_user(): void
    {
        $token = $this->generateAdminJwt();

        $user = UserProjection::create([
            'id' => 'usr_delete_test',
            'full_name' => 'Delete Test User',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/users/' . $user->id);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa vĩnh viễn người dùng khỏi hệ thống',
            ]);

        $this->assertDatabaseMissing('users_projection', [
            'id' => $user->id,
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

    public function test_can_get_dashboard_stats_users(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/stats/users?from=2026-01-01&to=2026-09-30');

        $response->assertStatus(200)
            ->assertExactJson([
                'growth_rate' => '15%',
                'chart_data' => [
                    [
                        'date' => '2026-09-01',
                        'new_users' => 120,
                        'active_users' => 850,
                    ],
                ],
            ]);
    }

    public function test_can_get_dashboard_stats_revenue(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/stats/revenue?group_by=month');

        $response->assertStatus(200)
            ->assertExactJson([
                'total_gmv' => 50000000,
                'total_commission' => 25000000,
                'series' => [
                    [
                        'month' => '2026-08',
                        'revenue' => 180000000,
                    ],
                ],
            ]);
    }

    public function test_can_get_dashboard_stats_categories(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/stats/categories?limit=10&sort=popularity');

        $response->assertStatus(200)
            ->assertExactJson([
                'data' => [
                    [
                        'category' => 'Esports',
                        'views' => 45000,
                        'posts' => 320,
                    ],
                    [
                        'category' => 'Anime',
                        'views' => 38000,
                        'posts' => 290,
                    ],
                ],
            ]);
    }

    public function test_dashboard_stats_requires_admin_role(): void
    {
        $token = $this->generateAdminJwt([
            'sub' => 'normal-user',
            'role' => 'User',
        ]);

        $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/stats/users')
            ->assertStatus(403);

        $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/stats/revenue')
            ->assertStatus(403);

        $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/dashboard/stats/categories')
            ->assertStatus(403);
    }
}

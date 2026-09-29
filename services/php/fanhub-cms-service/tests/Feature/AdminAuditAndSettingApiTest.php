<?php

namespace Tests\Feature;

use App\Models\AuditLog;
use App\Models\SystemSetting;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class AdminAuditAndSettingApiTest extends TestCase
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

    public function test_audit_logs_require_admin_jwt(): void
    {
        $response = $this->getJson('/api/v1/admin/audit-logs');
        $response->assertStatus(401);
    }

    public function test_get_audit_logs_empty_when_no_records(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/audit-logs?action=Ban_User&page=1&limit=20');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [],
            ]);
    }

    public function test_get_audit_logs_from_database(): void
    {
        $token = $this->generateAdminJwt();

        AuditLog::create([
            'id' => 'log_111',
            'actor_id' => 'adm_01',
            'actor' => 'Super Admin',
            'action' => 'Ban_User',
            'target' => 'usr_999',
            'ip' => '127.0.0.1',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/audit-logs?action=Ban_User&page=1&limit=20');

        $response->assertStatus(200);
        $this->assertCount(1, $response->json('data'));
        $this->assertEquals('log_111', $response->json('data.0.id'));
        $this->assertEquals('Super Admin', $response->json('data.0.actor'));
        $this->assertEquals('Ban_User', $response->json('data.0.action'));
        $this->assertEquals('usr_999', $response->json('data.0.target'));
    }

    public function test_settings_require_admin_jwt(): void
    {
        $response = $this->getJson('/api/v1/admin/settings');
        $response->assertStatus(401);
    }

    public function test_get_settings_default(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/settings');

        $response->assertStatus(200)
            ->assertJson([
                'maintenance_mode' => false,
                'platform_commission_fee' => 5.0,
                'max_upload_size_mb' => 25,
            ]);
    }

    public function test_update_settings(): void
    {
        $token = $this->generateAdminJwt();

        $updatePayload = [
            'maintenance_mode' => true,
            'platform_commission_fee' => 7.5,
            'max_upload_size_mb' => 50,
        ];

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/settings', $updatePayload);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã cập nhật cấu hình hệ thống',
            ]);

        // Verify update persisted
        $getResp = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/settings');

        $getResp->assertStatus(200)
            ->assertJson([
                'maintenance_mode' => true,
                'platform_commission_fee' => 7.5,
                'max_upload_size_mb' => 50,
            ]);
    }

    public function test_admin_mutating_actions_automatically_generate_audit_log(): void
    {
        $token = $this->generateAdminJwt();

        $updatePayload = [
            'maintenance_mode' => true,
            'platform_commission_fee' => 8.0,
            'max_upload_size_mb' => 30,
        ];

        // Perform an admin mutating action (PUT /api/v1/admin/settings)
        $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/settings', $updatePayload)
            ->assertStatus(200);

        // Verify that AuditLog was automatically created
        $this->assertDatabaseHas('cms_audit_logs', [
            'action' => 'Update_Settings',
            'target' => 'api/v1/admin/settings',
        ]);

        // Query /api/v1/admin/audit-logs and verify it returns the log
        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/audit-logs?action=Update_Settings&page=1&limit=20');

        $response->assertStatus(200);
        $this->assertCount(1, $response->json('data'));
        $this->assertEquals('Update_Settings', $response->json('data.0.action'));
    }
}


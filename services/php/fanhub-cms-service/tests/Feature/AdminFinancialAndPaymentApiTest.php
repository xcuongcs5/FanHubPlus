<?php

namespace Tests\Feature;

use App\Models\PaymentRefund;
use App\Models\PaymentTransaction;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class AdminFinancialAndPaymentApiTest extends TestCase
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

    public function test_financial_reports_with_breakdown_requires_admin_jwt(): void
    {
        $response = $this->getJson('/api/v1/admin/financial/reports?from=2026-01-01&to=2026-09-30&group_by=event');
        $response->assertStatus(401);
    }

    public function test_financial_reports_returns_correct_structure(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/financial/reports?from=2026-01-01&to=2026-09-30&group_by=event');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'total_volume',
                'commission_earned',
                'breakdown' => [
                    '*' => [
                        'event_id',
                        'event_title',
                        'tickets_sold',
                        'revenue',
                    ],
                ],
            ]);

        $this->assertEquals(450000000, $response->json('total_volume'));
        $this->assertEquals(22500000, $response->json('commission_earned'));
    }

    public function test_get_transactions_returns_mock_when_table_is_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/transactions?provider=VNPay&status=Success&page=1&limit=20');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'tx_xxx',
                        'user' => 'Nguyen Van A',
                        'amount' => 250000,
                        'provider' => 'VNPay',
                        'merchant_ref' => 'ORD_12345',
                        'status' => 'Success',
                        'created_at' => '2026-09-26',
                    ],
                ],
            ]);
    }

    public function test_get_transactions_returns_db_records(): void
    {
        $token = $this->generateAdminJwt();

        PaymentTransaction::create([
            'id' => 'tx_001',
            'user_name' => 'Tran Thi B',
            'amount' => 350000,
            'provider' => 'VNPay',
            'merchant_ref' => 'ORD_99999',
            'status' => 'Success',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/transactions?provider=VNPay&status=Success&page=1&limit=20');

        $response->assertStatus(200);
        $this->assertCount(1, $response->json('data'));
        $this->assertEquals('tx_001', $response->json('data.0.id'));
        $this->assertEquals('Tran Thi B', $response->json('data.0.user'));
        $this->assertEquals(350000, $response->json('data.0.amount'));
    }

    public function test_get_refunds_returns_mock_when_table_is_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/refunds?status=Pending&page=1&limit=20');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'ref_xxx',
                        'booking_id' => 'bk_xxx',
                        'user' => 'Nguyen Van B',
                        'amount' => 500000,
                        'reason' => 'Sự kiện dời ngày',
                        'status' => 'Pending',
                    ],
                ],
            ]);
    }

    public function test_get_refunds_returns_db_records(): void
    {
        $token = $this->generateAdminJwt();

        PaymentRefund::create([
            'id' => 'ref_101',
            'booking_id' => 'bk_555',
            'user_name' => 'Le Van C',
            'amount' => 750000,
            'reason' => 'Ban tổ chức dời lịch',
            'status' => 'Pending',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/refunds?status=Pending&page=1&limit=20');

        $response->assertStatus(200);
        $this->assertCount(1, $response->json('data'));
        $this->assertEquals('ref_101', $response->json('data.0.id'));
        $this->assertEquals('bk_555', $response->json('data.0.booking_id'));
        $this->assertEquals('Le Van C', $response->json('data.0.user'));
        $this->assertEquals(750000, $response->json('data.0.amount'));
    }

    public function test_process_refund_with_mock_id(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/refunds/ref_xxx/process', [
                'action' => 'Approve',
                'note' => 'Đồng ý hoàn tiền do sự kiện hủy',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Lệnh hoàn tiền đã được xử lý thành công',
            ]);
    }

    public function test_process_refund_with_existing_db_record(): void
    {
        $token = $this->generateAdminJwt();

        $refund = PaymentRefund::create([
            'id' => 'ref_202',
            'booking_id' => 'bk_777',
            'user_name' => 'Pham Thi D',
            'amount' => 600000,
            'reason' => 'Lý do cá nhân',
            'status' => 'Pending',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson("/api/v1/admin/refunds/{$refund->id}/process", [
                'action' => 'Approve',
                'note' => 'Đồng ý hoàn tiền theo chính sách',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Lệnh hoàn tiền đã được xử lý thành công',
            ]);

        $refund->refresh();
        $this->assertEquals('Approved', $refund->status);
        $this->assertEquals('Đồng ý hoàn tiền theo chính sách', $refund->note);
    }
}

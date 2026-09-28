<?php

namespace Tests\Feature;

use App\Models\Category;
use App\Models\Merchandise;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class AdminMerchandiseApiTest extends TestCase
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
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'admin-mrc-123',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'Admin',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    private function generateUserJwt(array $claims = []): string
    {
        $defaultClaims = [
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'user-mrc-456',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'User',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    public function test_can_get_merchandises_sample_when_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/merchandises?category_id=cat_xxx&page=1&limit=20');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'mrc_xxx',
                        'name' => 'Figure Goku Ultra Instinct',
                        'price' => 1200000,
                        'category' => 'Anime',
                        'tag' => 'Limited Edition',
                    ],
                ],
            ]);
    }

    public function test_can_get_merchandises_from_database(): void
    {
        $token = $this->generateAdminJwt();

        $cat = Category::create([
            'name' => 'Anime Figure',
            'slug' => 'anime-figure',
        ]);

        $item = Merchandise::create([
            'id' => 'mrc_goku_01',
            'category_id' => $cat->id,
            'name' => 'Figure Goku Ultra Instinct',
            'price' => 1200000,
            'tag' => 'Limited Edition',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/merchandises?category_id=' . $cat->id . '&page=1&limit=20');

        $response->assertStatus(200);
        $data = $response->json('data');

        $this->assertCount(1, $data);
        $this->assertEquals('mrc_goku_01', $data[0]['id']);
        $this->assertEquals('Figure Goku Ultra Instinct', $data[0]['name']);
        $this->assertEquals(1200000, $data[0]['price']);
        $this->assertEquals('Anime Figure', $data[0]['category']);
        $this->assertEquals('Limited Edition', $data[0]['tag']);
    }

    public function test_can_create_merchandise(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/merchandises', [
                'name' => 'Figure Goku Ultra Instinct',
                'category_id' => 'cat_xxx',
                'price' => 1200000,
                'description' => 'Tỷ lệ 1/6...',
                'image_url' => 'https://example.com/goku.png',
                'tag' => 'Limited Edition',
            ]);

        $response->assertStatus(201)
            ->assertJsonStructure(['id', 'message'])
            ->assertJson([
                'message' => 'Thêm vật phẩm thành công',
            ]);

        $this->assertDatabaseHas('merchandises', [
            'name' => 'Figure Goku Ultra Instinct',
            'price' => 1200000,
        ]);
    }

    public function test_create_validation_fails_when_name_missing(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/merchandises', [
                'price' => 1200000,
            ]);

        $response->assertStatus(422)
            ->assertJsonValidationErrors(['name']);
    }

    public function test_can_update_merchandise(): void
    {
        $token = $this->generateAdminJwt();

        $item = Merchandise::create([
            'name' => 'Figure Goku Ultra Instinct',
            'price' => 1200000,
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/merchandises/' . $item->id, [
                'name' => 'Figure Goku Ultra Instinct (Bản kỷ niệm)',
                'price' => 1350000,
                'description' => 'Cập nhật...',
                'tag' => 'Limited Edition',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật vật phẩm thành công',
            ]);

        $this->assertDatabaseHas('merchandises', [
            'id' => $item->id,
            'name' => 'Figure Goku Ultra Instinct (Bản kỷ niệm)',
            'price' => 1350000,
            'tag' => 'Limited Edition',
        ]);
    }

    public function test_can_update_mock_merchandise(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/merchandises/mrc_xxx', [
                'name' => 'Figure Goku Ultra Instinct (Bản kỷ niệm)',
                'price' => 1350000,
                'description' => 'Cập nhật...',
                'tag' => 'Limited Edition',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật vật phẩm thành công',
            ]);
    }

    public function test_can_delete_merchandise(): void
    {
        $token = $this->generateAdminJwt();

        $item = Merchandise::create([
            'name' => 'To Delete',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/merchandises/' . $item->id);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa vật phẩm khỏi danh mục',
            ]);

        $this->assertDatabaseMissing('merchandises', [
            'id' => $item->id,
        ]);
    }

    public function test_can_delete_mock_merchandise(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/merchandises/mrc_xxx');

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa vật phẩm khỏi danh mục',
            ]);
    }

    public function test_returns_404_when_merchandise_not_found(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/merchandises/non-existing-uuid');

        $response->assertStatus(404)
            ->assertJson([
                'message' => 'Không tìm thấy vật phẩm.',
            ]);
    }

    public function test_merchandises_require_admin_jwt(): void
    {
        $userToken = $this->generateUserJwt();

        // No token
        $this->withHeader('Authorization', '')
            ->getJson('/api/v1/admin/merchandises')
            ->assertStatus(401);

        $this->withHeader('Authorization', '')
            ->postJson('/api/v1/admin/merchandises', ['name' => 'Item'])
            ->assertStatus(401);

        $this->withHeader('Authorization', '')
            ->deleteJson('/api/v1/admin/merchandises/mrc_123')
            ->assertStatus(401);

        // Non-admin token
        $this->withHeader('Authorization', 'Bearer ' . $userToken)
            ->getJson('/api/v1/admin/merchandises')
            ->assertStatus(403);
    }
}

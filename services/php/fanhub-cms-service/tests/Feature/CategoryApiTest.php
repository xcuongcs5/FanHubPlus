<?php

namespace Tests\Feature;

use App\Models\Category;
use App\Models\Post;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class CategoryApiTest extends TestCase
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
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'admin-123',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'Admin',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    private function generateUserJwt(array $claims = []): string
    {
        $defaultClaims = [
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'user-456',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'User',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    public function test_cannot_access_without_token(): void
    {
        $response = $this->withHeader('Authorization', '')
            ->postJson('/api/v1/admin/categories', [
                'name' => 'Esports',
                'description' => 'Thể thao điện tử',
            ]);

        $response->assertStatus(401);
    }

    public function test_non_admin_cannot_access(): void
    {
        $userToken = $this->generateUserJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $userToken)
            ->getJson('/api/v1/admin/categories');

        $response->assertStatus(403);
    }

    public function test_can_list_categories_sample_when_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/categories?include_children=true');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'cat_1',
                        'name' => 'Gaming',
                        'slug' => 'gaming',
                        'children' => [
                            [
                                'id' => 'cat_2',
                                'name' => 'Esports',
                                'slug' => 'esports',
                            ],
                        ],
                    ],
                ],
            ]);
    }

    public function test_can_list_categories_from_database(): void
    {
        $token = $this->generateAdminJwt();

        $parent = Category::create([
            'name' => 'Gaming',
            'slug' => 'gaming',
        ]);

        $child = Category::create([
            'name' => 'Esports',
            'slug' => 'esports',
            'parent_id' => $parent->id,
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/categories?include_children=true');

        $response->assertStatus(200);
        $data = $response->json('data');

        $this->assertNotEmpty($data);
        $this->assertEquals($parent->id, $data[0]['id']);
        $this->assertEquals('Gaming', $data[0]['name']);
        $this->assertCount(1, $data[0]['children']);
        $this->assertEquals($child->id, $data[0]['children'][0]['id']);
        $this->assertEquals('Esports', $data[0]['children'][0]['name']);
    }

    public function test_can_get_single_category_mock(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/categories/cat_xxx');

        $response->assertStatus(200)
            ->assertJson([
                'id' => 'cat_xxx',
                'name' => 'Esports',
                'slug' => 'esports',
                'parent_id' => 'cat_1',
                'stats' => [
                    'events_count' => 12,
                    'posts_count' => 340,
                ],
            ]);
    }

    public function test_can_get_single_category_from_db(): void
    {
        $token = $this->generateAdminJwt();

        $parent = Category::create([
            'name' => 'Parent Cat',
            'slug' => 'parent-cat',
        ]);

        $category = Category::create([
            'name' => 'Mobile Games',
            'slug' => 'mobile-games',
            'parent_id' => $parent->id,
        ]);

        Post::create([
            'id' => 'cnt_test_cat_post',
            'user_id' => 'usr_001',
            'category_id' => $category->id,
            'title' => 'Post in cat',
            'body' => 'Body text',
            'status' => 'active',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/categories/' . $category->id);

        $response->assertStatus(200)
            ->assertJson([
                'id' => $category->id,
                'name' => 'Mobile Games',
                'slug' => 'mobile-games',
                'parent_id' => $parent->id,
                'stats' => [
                    'posts_count' => 1,
                ],
            ]);
    }

    public function test_returns_404_when_category_not_found(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/categories/00000000-0000-0000-0000-000000000000');

        $response->assertStatus(404)
            ->assertJson([
                'message' => 'Không tìm thấy danh mục.',
            ]);
    }

    public function test_can_create_category(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/categories', [
                'name' => 'Esports',
                'slug' => 'esports',
                'parent_id' => 'cat_1',
            ]);

        $response->assertStatus(201)
            ->assertJsonStructure(['id', 'message'])
            ->assertJson([
                'message' => 'Đã tạo danh mục mới',
            ]);

        $this->assertDatabaseHas('categories', [
            'name' => 'Esports',
            'slug' => 'esports',
        ]);
    }

    public function test_validation_fails_when_name_is_missing(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/categories', [
                'description' => 'Thiếu tên danh mục',
            ]);

        $response->assertStatus(422)
            ->assertJsonValidationErrors(['name']);
    }

    public function test_can_update_category(): void
    {
        $token = $this->generateAdminJwt();

        $category = Category::create([
            'name' => 'Esports',
            'slug' => 'esports',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/categories/' . $category->id, [
                'name' => 'Esports 2026',
                'slug' => 'esports-2026',
                'parent_id' => null,
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã cập nhật danh mục',
            ]);

        $this->assertDatabaseHas('categories', [
            'id' => $category->id,
            'name' => 'Esports 2026',
            'slug' => 'esports-2026',
            'parent_id' => null,
        ]);
    }

    public function test_can_delete_category(): void
    {
        $token = $this->generateAdminJwt();

        $category = Category::create([
            'name' => 'Esports',
            'slug' => 'esports',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/categories/' . $category->id);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa danh mục thành công',
            ]);

        $this->assertDatabaseMissing('categories', [
            'id' => $category->id,
        ]);
    }

    public function test_can_delete_mock_category(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/categories/cat_xxx');

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa danh mục thành công',
            ]);
    }
}

<?php

namespace Tests\Feature;

use App\Models\Category;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class CategoryApiTest extends TestCase
{
    use RefreshDatabase;

    private function generateAdminJwt(): string
    {
        $header = base64_encode(json_encode(['typ' => 'JWT', 'alg' => 'none']));
        $header = str_replace(['+', '/', '='], ['-', '_', ''], $header);

        $payload = base64_encode(json_encode([
            'sub' => 'admin-123',
            'role' => 'admin',
            'exp' => time() + 3600,
        ]));
        $payload = str_replace(['+', '/', '='], ['-', '_', ''], $payload);

        return "{$header}.{$payload}.testsignature";
    }

    public function test_cannot_access_without_token(): void
    {
        $response = $this->postJson('/api/v1/admin/categories', [
            'name' => 'Esports',
            'description' => 'Thể thao điện tử',
        ]);

        $response->assertStatus(401);
    }

    public function test_can_create_category_with_admin_jwt(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/categories', [
                'name' => 'Esports',
                'description' => 'Thể thao điện tử',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Đã tạo danh mục',
            ]);

        $this->assertDatabaseHas('categories', [
            'name' => 'Esports',
            'slug' => 'esports',
            'description' => 'Thể thao điện tử',
        ]);
    }

    public function test_can_update_category_with_admin_jwt(): void
    {
        $token = $this->generateAdminJwt();

        $category = Category::create([
            'name' => 'Esports',
            'description' => 'Thể thao điện tử',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/categories/' . $category->id, [
                'name' => 'Esports 2026',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã cập nhật danh mục',
            ]);

        $this->assertDatabaseHas('categories', [
            'id' => $category->id,
            'name' => 'Esports 2026',
            'slug' => 'esports-2026',
        ]);
    }

    public function test_can_delete_category_with_admin_jwt(): void
    {
        $token = $this->generateAdminJwt();

        $category = Category::create([
            'name' => 'Esports',
            'description' => 'Thể thao điện tử',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/categories/' . $category->id);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa danh mục',
            ]);

        $this->assertDatabaseMissing('categories', [
            'id' => $category->id,
        ]);
    }

    public function test_can_list_categories(): void
    {
        $token = $this->generateAdminJwt();

        Category::create(['name' => 'Cat 1']);
        Category::create(['name' => 'Cat 2']);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/categories');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'status',
                'data',
            ]);
    }

    public function test_can_create_category_with_parent(): void
    {
        $token = $this->generateAdminJwt();

        $parent = Category::create([
            'name' => 'Gaming',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/categories', [
                'name' => 'League of Legends',
                'parent_id' => $parent->id,
                'description' => 'Game MOBA phổ biến',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Đã tạo danh mục',
            ]);

        $this->assertDatabaseHas('categories', [
            'name' => 'League of Legends',
            'parent_id' => $parent->id,
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

    public function test_can_get_single_category(): void
    {
        $token = $this->generateAdminJwt();

        $category = Category::create([
            'name' => 'Mobile Games',
            'description' => 'Game trên di động',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/categories/' . $category->id);

        $response->assertStatus(200)
            ->assertJson([
                'status' => 'success',
                'data' => [
                    'id' => $category->id,
                    'name' => 'Mobile Games',
                ],
            ]);
    }

    public function test_returns_404_when_category_not_found(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/categories/non-existing-uuid');

        $response->assertStatus(404)
            ->assertJson([
                'message' => 'Không tìm thấy danh mục.',
            ]);
    }
}

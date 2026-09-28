<?php

namespace Tests\Feature;

use App\Models\Category;
use App\Models\CharacterProfile;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class AdminCharacterApiTest extends TestCase
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
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'admin-chr-123',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'Admin',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    private function generateUserJwt(array $claims = []): string
    {
        $defaultClaims = [
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'user-chr-456',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'User',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    public function test_can_get_characters_sample_when_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/characters?category_id=cat_xxx&search=naruto&page=1&limit=20');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'chr_xxx',
                        'name' => 'Naruto Uzumaki',
                        'category' => 'Anime',
                        'avatar_url' => 'https://fanhub.com/naruto.jpg',
                    ],
                ],
            ]);
    }

    public function test_can_get_characters_from_database(): void
    {
        $token = $this->generateAdminJwt();

        $cat = Category::create([
            'name' => 'Anime Manga',
            'slug' => 'anime-manga',
        ]);

        $chr = CharacterProfile::create([
            'id' => 'chr_naruto_01',
            'category_id' => $cat->id,
            'name' => 'Naruto Uzumaki',
            'biography' => 'Hokage Đệ Thất làng Lá',
            'avatar_url' => 'https://example.com/naruto.png',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/characters?search=naruto&page=1&limit=20');

        $response->assertStatus(200);
        $data = $response->json('data');

        $this->assertCount(1, $data);
        $this->assertEquals('chr_naruto_01', $data[0]['id']);
        $this->assertEquals('Naruto Uzumaki', $data[0]['name']);
        $this->assertEquals('Anime Manga', $data[0]['category']);
        $this->assertEquals('https://example.com/naruto.png', $data[0]['avatar_url']);
    }

    public function test_can_create_character(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/characters', [
                'name' => 'Naruto Uzumaki',
                'category_id' => 'cat_xxx',
                'biography' => 'Hokage Đệ Thất...',
                'avatar_url' => 'https://example.com/naruto.png',
            ]);

        $response->assertStatus(201)
            ->assertJsonStructure(['id', 'message'])
            ->assertJson([
                'message' => 'Tạo hồ sơ nhân vật thành công',
            ]);

        $this->assertDatabaseHas('cms_character_profiles', [
            'name' => 'Naruto Uzumaki',
            'category_id' => 'cat_xxx',
        ]);
    }

    public function test_create_validation_fails_when_name_missing(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/characters', [
                'biography' => 'No name provided',
            ]);

        $response->assertStatus(422)
            ->assertJsonValidationErrors(['name']);
    }

    public function test_can_update_character(): void
    {
        $token = $this->generateAdminJwt();

        $chr = CharacterProfile::create([
            'name' => 'Naruto',
            'biography' => 'Initial bio',
            'avatar_url' => 'https://example.com/old.png',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/characters/' . $chr->id, [
                'name' => 'Naruto Uzumaki',
                'biography' => 'Tiểu sử cập nhật...',
                'avatar_url' => 'https://example.com/new.png',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật hồ sơ nhân vật thành công',
            ]);

        $this->assertDatabaseHas('cms_character_profiles', [
            'id' => $chr->id,
            'name' => 'Naruto Uzumaki',
            'biography' => 'Tiểu sử cập nhật...',
            'avatar_url' => 'https://example.com/new.png',
        ]);
    }

    public function test_can_update_mock_character(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/characters/chr_xxx', [
                'name' => 'Naruto Uzumaki',
                'biography' => 'Tiểu sử cập nhật...',
                'avatar_url' => 'https://example.com/new.png',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật hồ sơ nhân vật thành công',
            ]);
    }

    public function test_can_delete_character(): void
    {
        $token = $this->generateAdminJwt();

        $chr = CharacterProfile::create([
            'name' => 'To Delete',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/characters/' . $chr->id);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa hồ sơ nhân vật',
            ]);

        $this->assertDatabaseMissing('cms_character_profiles', [
            'id' => $chr->id,
        ]);
    }

    public function test_can_delete_mock_character(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/characters/chr_xxx');

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa hồ sơ nhân vật',
            ]);
    }

    public function test_returns_404_when_character_not_found(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/characters/non-existing-uuid');

        $response->assertStatus(404)
            ->assertJson([
                'message' => 'Không tìm thấy hồ sơ nhân vật.',
            ]);
    }

    public function test_characters_require_admin_jwt(): void
    {
        $userToken = $this->generateUserJwt();

        // No token
        $this->withHeader('Authorization', '')
            ->getJson('/api/v1/admin/characters')
            ->assertStatus(401);

        $this->withHeader('Authorization', '')
            ->postJson('/api/v1/admin/characters', ['name' => 'Test'])
            ->assertStatus(401);

        $this->withHeader('Authorization', '')
            ->deleteJson('/api/v1/admin/characters/chr_123')
            ->assertStatus(401);

        // Non-admin token
        $this->withHeader('Authorization', 'Bearer ' . $userToken)
            ->getJson('/api/v1/admin/characters')
            ->assertStatus(403);
    }
}

<?php

namespace Tests\Feature;

use App\Models\Post;
use App\Models\Tag;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Tests\TestCase;

class AdminTagApiTest extends TestCase
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
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'admin-tag-123',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'Admin',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    private function generateUserJwt(array $claims = []): string
    {
        $defaultClaims = [
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'user-tag-456',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'User',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    public function test_can_get_tags_sample_when_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/tags?search=moba&page=1&limit=50');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'tag_xxx',
                        'name' => 'MOBA',
                        'used_count' => 45,
                    ],
                ],
            ]);
    }

    public function test_can_get_tags_from_database(): void
    {
        $token = $this->generateAdminJwt();

        $tag1 = Tag::create(['name' => 'MOBA']);
        $tag2 = Tag::create(['name' => 'FPS']);

        $post = Post::create([
            'id' => 'cnt_tag_test',
            'user_id' => 'usr_001',
            'title' => 'Tag Test',
            'body' => 'Body text',
            'status' => 'active',
        ]);

        DB::table('cms_content_tags')->insert([
            'content_id' => $post->id,
            'tag_id' => $tag1->id,
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/tags?search=moba&page=1&limit=50');

        $response->assertStatus(200);
        $data = $response->json('data');

        $this->assertCount(1, $data);
        $this->assertEquals($tag1->id, $data[0]['id']);
        $this->assertEquals('MOBA', $data[0]['name']);
        $this->assertEquals(1, $data[0]['used_count']);
    }

    public function test_can_create_tag(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/tags', [
                'name' => 'Limited Edition',
            ]);

        $response->assertStatus(201)
            ->assertJsonStructure(['id', 'message'])
            ->assertJson([
                'message' => 'Đã tạo thẻ thành công',
            ]);

        $this->assertDatabaseHas('cms_tags', [
            'name' => 'Limited Edition',
        ]);
    }

    public function test_validation_fails_when_name_missing(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/tags', []);

        $response->assertStatus(422)
            ->assertJsonValidationErrors(['name']);
    }

    public function test_can_delete_tag(): void
    {
        $token = $this->generateAdminJwt();

        $tag = Tag::create(['name' => 'To Delete']);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/tags/' . $tag->id);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa thẻ',
            ]);

        $this->assertDatabaseMissing('cms_tags', [
            'id' => $tag->id,
        ]);
    }

    public function test_can_delete_mock_tag_id(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/tags/tag_xxx');

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa thẻ',
            ]);
    }

    public function test_returns_404_when_tag_not_found(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/tags/non-existing-tag');

        $response->assertStatus(404)
            ->assertJson([
                'message' => 'Không tìm thấy thẻ.',
            ]);
    }

    public function test_tags_require_admin_jwt(): void
    {
        $userToken = $this->generateUserJwt();

        // No token
        $this->withHeader('Authorization', '')
            ->getJson('/api/v1/admin/tags')
            ->assertStatus(401);

        $this->withHeader('Authorization', '')
            ->postJson('/api/v1/admin/tags', ['name' => 'Test'])
            ->assertStatus(401);

        $this->withHeader('Authorization', '')
            ->deleteJson('/api/v1/admin/tags/tag_123')
            ->assertStatus(401);

        // Non-admin token
        $this->withHeader('Authorization', 'Bearer ' . $userToken)
            ->getJson('/api/v1/admin/tags')
            ->assertStatus(403);
    }
}

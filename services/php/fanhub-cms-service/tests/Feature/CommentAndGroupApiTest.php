<?php

namespace Tests\Feature;

use App\Models\FandomGroup;
use App\Models\Post;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class CommentAndGroupApiTest extends TestCase
{
    use RefreshDatabase;

    private function generateBearerJwt(string $userId = 'user-001'): string
    {
        $header = base64_encode(json_encode(['typ' => 'JWT', 'alg' => 'none']));
        $header = str_replace(['+', '/', '='], ['-', '_', ''], $header);

        $payload = base64_encode(json_encode([
            'sub' => $userId,
            'role' => 'user',
            'exp' => time() + 3600,
        ]));
        $payload = str_replace(['+', '/', '='], ['-', '_', ''], $payload);

        return "{$header}.{$payload}.testsignature";
    }

    public function test_can_create_comment(): void
    {
        $token = $this->generateBearerJwt();

        $post = Post::create([
            'user_id' => 'user-001',
            'title' => 'Bài viết thảo luận',
            'body' => 'Nội dung',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/posts/' . $post->id . '/comments', [
                'title' => 'Dữ liệu mẫu',
                'description' => 'Chi tiết Bình luận',
                'status' => 'active',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Bình luận thành công',
            ])
            ->assertJsonStructure([
                'id',
                'message',
            ]);

        $this->assertDatabaseHas('comments', [
            'target_id' => $post->id,
            'title' => 'Dữ liệu mẫu',
            'body' => 'Chi tiết Bình luận',
            'status' => 'active',
        ]);

        $this->assertEquals(1, $post->fresh()->comments_count);
    }

    public function test_can_get_comments(): void
    {
        $token = $this->generateBearerJwt();

        $post = Post::create([
            'user_id' => 'user-001',
            'title' => 'Bài viết thảo luận',
            'body' => 'Nội dung',
        ]);

        $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/posts/' . $post->id . '/comments', [
                'title' => 'Bình luận 1',
                'description' => 'Nội dung bình luận 1',
            ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/posts/' . $post->id . '/comments');

        $response->assertStatus(200)
            ->assertJson([
                'id' => $post->id,
                'name' => 'Danh sách bình luận',
            ])
            ->assertJsonStructure([
                'id',
                'name',
                'created_at',
                'details',
            ]);
    }

    public function test_can_create_fandom_group(): void
    {
        $token = $this->generateBearerJwt('creator-user');

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/groups', [
                'title' => 'Dữ liệu mẫu',
                'description' => 'Chi tiết Tạo Fandom Group',
                'status' => 'active',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Tạo Fandom Group thành công',
            ])
            ->assertJsonStructure([
                'id',
                'message',
            ]);

        $this->assertDatabaseHas('fandom_groups', [
            'name' => 'Dữ liệu mẫu',
            'description' => 'Chi tiết Tạo Fandom Group',
            'status' => 'active',
            'created_by' => 'creator-user',
        ]);
    }

    public function test_can_join_fandom_group(): void
    {
        $token = $this->generateBearerJwt('joiner-user');

        $group = FandomGroup::create([
            'name' => 'Faker Fanclub',
            'description' => 'Nhóm fan hâm mộ',
            'created_by' => 'creator-user',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/groups/' . $group->id . '/join', [
                'title' => 'Dữ liệu mẫu',
                'description' => 'Chi tiết Tham gia Group',
                'status' => 'active',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Tham gia Group thành công',
            ])
            ->assertJsonStructure([
                'id',
                'message',
            ]);

        $this->assertDatabaseHas('fandom_group_members', [
            'group_id' => $group->id,
            'user_id' => 'joiner-user',
            'role' => 'member',
        ]);
    }
}

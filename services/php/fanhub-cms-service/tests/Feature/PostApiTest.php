<?php

namespace Tests\Feature;

use App\Models\Category;
use App\Models\Post;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class PostApiTest extends TestCase
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

    public function test_cannot_create_post_without_bearer_jwt(): void
    {
        $response = $this->postJson('/api/v1/posts', [
            'title' => 'Dữ liệu mẫu',
            'description' => 'Chi tiết Đăng bài thảo luận',
            'status' => 'active',
        ]);

        $response->assertStatus(401);
    }

    public function test_can_create_post_with_bearer_jwt(): void
    {
        $token = $this->generateBearerJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/posts', [
                'title' => 'Dữ liệu mẫu',
                'description' => 'Chi tiết Đăng bài thảo luận',
                'status' => 'active',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Đăng bài thảo luận thành công',
            ])
            ->assertJsonStructure([
                'id',
                'message',
            ]);

        $this->assertDatabaseHas('contents', [
            'title' => 'Dữ liệu mẫu',
            'body' => 'Chi tiết Đăng bài thảo luận',
            'status' => 'active',
        ]);
    }

    public function test_can_list_posts_with_pagination(): void
    {
        $token = $this->generateBearerJwt();

        Post::create([
            'user_id' => 'user-001',
            'title' => 'Newsfeed bài viết 1',
            'body' => 'Nội dung 1',
        ]);

        Post::create([
            'user_id' => 'user-002',
            'title' => 'Newsfeed bài viết 2',
            'body' => 'Nội dung 2',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/posts?page=1&limit=20&sort=newest');

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

    public function test_can_get_single_post(): void
    {
        $token = $this->generateBearerJwt();

        $post = Post::create([
            'user_id' => 'user-001',
            'title' => 'Xem bài viết',
            'body' => 'Nội dung bài viết chi tiết',
            'status' => 'active',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/posts/' . $post->id);

        $response->assertStatus(200)
            ->assertJson([
                'id' => $post->id,
                'name' => 'Xem bài viết',
                'details' => [
                    'title' => 'Xem bài viết',
                    'description' => 'Nội dung bài viết chi tiết',
                ],
            ])
            ->assertJsonStructure([
                'id',
                'name',
                'created_at',
                'details',
            ]);
    }

    public function test_can_update_post(): void
    {
        $token = $this->generateBearerJwt();

        $post = Post::create([
            'user_id' => 'user-001',
            'title' => 'Dữ liệu cũ',
            'body' => 'Nội dung cũ',
            'status' => 'active',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/posts/' . $post->id, [
                'title' => 'Cập nhật dữ liệu',
                'status' => 'updated',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật thành công',
            ]);

        $this->assertDatabaseHas('contents', [
            'id' => $post->id,
            'title' => 'Cập nhật dữ liệu',
            'status' => 'updated',
        ]);
    }

    public function test_can_delete_post(): void
    {
        $token = $this->generateBearerJwt();

        $post = Post::create([
            'user_id' => 'user-001',
            'title' => 'Bài viết cần xóa',
            'body' => 'Nội dung',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/posts/' . $post->id);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Xóa thành công',
            ]);

        $this->assertDatabaseMissing('contents', [
            'id' => $post->id,
        ]);
    }

    public function test_can_like_post(): void
    {
        $token = $this->generateBearerJwt('user-007');

        $post = Post::create([
            'user_id' => 'user-001',
            'title' => 'Bài viết thảo luận',
            'body' => 'Nội dung',
            'likes_count' => 0,
        ]);

        $likeResponse = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/posts/' . $post->id . '/like', [
                'title' => 'Dữ liệu mẫu',
                'description' => 'Chi tiết Thích bài viết',
                'status' => 'active',
            ]);

        $likeResponse->assertStatus(201)
            ->assertJson([
                'message' => 'Thích bài viết thành công',
            ])
            ->assertJsonStructure([
                'id',
                'message',
            ]);

        $this->assertDatabaseHas('reaction_bookmarks', [
            'user_id' => 'user-007',
            'target_type' => 'content',
            'target_id' => $post->id,
            'action_type' => 'like',
        ]);
    }
}

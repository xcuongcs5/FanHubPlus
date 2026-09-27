<?php

namespace Tests\Feature;

use App\Models\Comment;
use App\Models\Post;
use App\Models\UserProjection;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class AdminCommentApiTest extends TestCase
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
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'admin-uuid-1234',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'Admin',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    private function generateUserJwt(array $claims = []): string
    {
        $defaultClaims = [
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' => 'user-uuid-5678',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' => 'User',
            'exp' => time() + 3600,
        ];

        return $this->generateJwt(array_merge($defaultClaims, $claims));
    }

    public function test_can_get_flagged_comments_sample_when_empty(): void
    {
        $adminToken = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $adminToken)
            ->getJson('/api/v1/admin/comments/flagged?page=1&limit=20&sort=reports_count');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'data' => [
                    '*' => [
                        'id',
                        'post_id',
                        'user',
                        'body',
                        'reports_count',
                    ],
                ],
            ])
            ->assertJson([
                'data' => [
                    [
                        'id' => 'cmt_xxx',
                        'post_id' => 'cnt_xxx',
                        'user' => 'Spammer',
                        'body' => 'Bình luận spam...',
                        'reports_count' => 5,
                    ],
                ],
            ]);
    }

    public function test_can_get_flagged_comments_from_database(): void
    {
        $adminToken = $this->generateAdminJwt();

        $user = UserProjection::create([
            'id' => 'usr_bad_guy',
            'full_name' => 'Bad User',
            'email' => 'bad@example.com',
            'status' => 'active',
        ]);

        $post = Post::create([
            'id' => 'cnt_post_001',
            'user_id' => $user->id,
            'title' => 'Sample Post',
            'body' => 'Sample body',
            'status' => 'active',
            'comments_count' => 2,
        ]);

        $cmt1 = Comment::create([
            'id' => 'cmt_flagged_01',
            'target_id' => $post->id,
            'user_id' => $user->id,
            'body' => 'Spam comment here',
            'status' => 'flagged',
            'reports_count' => 10,
        ]);

        $cmt2 = Comment::create([
            'id' => 'cmt_flagged_02',
            'target_id' => $post->id,
            'user_id' => $user->id,
            'body' => 'Another reported comment',
            'status' => 'active',
            'reports_count' => 3,
        ]);

        // Regular comment, not flagged
        Comment::create([
            'id' => 'cmt_clean_03',
            'target_id' => $post->id,
            'user_id' => $user->id,
            'body' => 'Nice post!',
            'status' => 'active',
            'reports_count' => 0,
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $adminToken)
            ->getJson('/api/v1/admin/comments/flagged?page=1&limit=20&sort=reports_count');

        $response->assertStatus(200);
        $data = $response->json('data');

        $this->assertCount(2, $data);
        $this->assertEquals('cmt_flagged_01', $data[0]['id']);
        $this->assertEquals('cnt_post_001', $data[0]['post_id']);
        $this->assertEquals('Bad User', $data[0]['user']);
        $this->assertEquals(10, $data[0]['reports_count']);

        $this->assertEquals('cmt_flagged_02', $data[1]['id']);
        $this->assertEquals(3, $data[1]['reports_count']);
    }

    public function test_can_delete_comment(): void
    {
        $adminToken = $this->generateAdminJwt();

        $post = Post::create([
            'id' => 'cnt_test_post',
            'user_id' => 'usr_001',
            'title' => 'Test Post',
            'body' => 'Post content',
            'status' => 'active',
            'comments_count' => 1,
        ]);

        $cmt = Comment::create([
            'id' => 'cmt_to_delete',
            'target_id' => $post->id,
            'user_id' => 'usr_spammer',
            'body' => 'Toxic comment',
            'status' => 'flagged',
            'reports_count' => 5,
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $adminToken)
            ->deleteJson("/api/v1/admin/comments/{$cmt->id}");

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa bình luận vi phạm',
            ]);

        $this->assertDatabaseMissing('comments', ['id' => 'cmt_to_delete']);
        $this->assertEquals(0, $post->fresh()->comments_count);
    }

    public function test_can_delete_mock_comment_id(): void
    {
        $adminToken = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $adminToken)
            ->deleteJson('/api/v1/admin/comments/cmt_xxx');

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã xóa bình luận vi phạm',
            ]);
    }

    public function test_comments_endpoints_require_admin_jwt(): void
    {
        $userToken = $this->generateUserJwt();

        // Without token
        $this->withHeader('Authorization', '')
            ->getJson('/api/v1/admin/comments/flagged')
            ->assertStatus(401);

        $this->withHeader('Authorization', '')
            ->deleteJson('/api/v1/admin/comments/cmt_123')
            ->assertStatus(401);

        // With non-admin token
        $this->withHeader('Authorization', 'Bearer ' . $userToken)
            ->getJson('/api/v1/admin/comments/flagged')
            ->assertStatus(403);
    }
}

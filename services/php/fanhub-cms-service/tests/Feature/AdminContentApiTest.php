<?php

namespace Tests\Feature;

use App\Models\Category;
use App\Models\MediaAsset;
use App\Models\Post;
use App\Models\UserProjection;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\TestCase;

class AdminContentApiTest extends TestCase
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

    public function test_can_get_contents_list_sample_when_empty(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/contents?page=1&limit=20');

        $response->assertStatus(200)
            ->assertJson([
                'data' => [
                    [
                        'id' => 'cnt_xxx',
                        'title' => 'Review Anime Mùa Thu',
                        'author' => 'User B',
                        'status' => 'Pending',
                        'created_at' => '2026-09-25',
                    ],
                ],
                'meta' => [
                    'total' => 25,
                    'page' => 1,
                ],
            ]);
    }

    public function test_can_get_contents_list_from_db(): void
    {
        $token = $this->generateAdminJwt();

        $author = UserProjection::create([
            'id' => 'usr_author',
            'full_name' => 'Author Name',
            'status' => 'active',
        ]);

        $post = Post::create([
            'id' => 'cnt_test_1',
            'user_id' => $author->id,
            'title' => 'Test Content',
            'body' => 'Test Body',
            'status' => 'Published',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/contents?status=Published&page=1&limit=20');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'data' => [
                    '*' => [
                        'id',
                        'title',
                        'author',
                        'status',
                        'created_at',
                    ],
                ],
                'meta' => [
                    'total',
                    'page',
                ],
            ]);
    }

    public function test_can_get_single_content(): void
    {
        $token = $this->generateAdminJwt();

        $post = Post::create([
            'id' => 'cnt_single',
            'user_id' => 'usr_author',
            'title' => 'Single Content Title',
            'body' => 'Single Content Body',
            'status' => 'Pending',
        ]);

        MediaAsset::create([
            'id' => 'med_1',
            'content_id' => $post->id,
            'file_url' => 'https://fanhub.com/media/test.png',
            'file_type' => 'Image',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->getJson('/api/v1/admin/contents/' . $post->id);

        $response->assertStatus(200)
            ->assertJson([
                'id' => $post->id,
                'title' => 'Single Content Title',
                'body' => 'Single Content Body',
                'status' => 'Pending',
            ])
            ->assertJsonStructure([
                'id',
                'title',
                'body',
                'media' => [
                    '*' => [
                        'url',
                        'type',
                    ],
                ],
                'author' => [
                    'id',
                    'name',
                ],
                'status',
            ]);
    }

    public function test_can_review_content_approve(): void
    {
        $token = $this->generateAdminJwt();

        $post = Post::create([
            'id' => 'cnt_review_target',
            'user_id' => 'usr_author',
            'title' => 'Review Target',
            'body' => 'Body',
            'status' => 'Pending',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/contents/' . $post->id . '/review', [
                'action' => 'Approve',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã duyệt/từ chối bài viết thành công',
            ]);

        $this->assertDatabaseHas('contents', [
            'id' => $post->id,
            'status' => 'Published',
        ]);
    }

    public function test_can_review_content_reject(): void
    {
        $token = $this->generateAdminJwt();

        $post = Post::create([
            'id' => 'cnt_reject_target',
            'user_id' => 'usr_author',
            'title' => 'Reject Target',
            'body' => 'Body',
            'status' => 'Pending',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/contents/' . $post->id . '/review', [
                'action' => 'Reject',
                'reject_reason' => 'Nội dung vi phạm bản quyền hình ảnh',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã duyệt/từ chối bài viết thành công',
            ]);

        $this->assertDatabaseHas('contents', [
            'id' => $post->id,
            'status' => 'Rejected',
            'reject_reason' => 'Nội dung vi phạm bản quyền hình ảnh',
        ]);
    }

    public function test_can_create_featured_content(): void
    {
        $token = $this->generateAdminJwt();

        $category = Category::create([
            'name' => 'Esports',
            'description' => 'Category Esports',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/contents', [
                'title' => 'Thông báo Sự kiện Chung kết Thế Giới',
                'body' => 'Nội dung chi tiết...',
                'category_id' => $category->id,
                'is_featured' => true,
                'media_urls' => [
                    'https://fanhub.com/media/banner.jpg',
                ],
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Đăng bài viết Featured/Thông báo thành công',
            ])
            ->assertJsonStructure([
                'id',
                'message',
            ]);

        $createdId = $response->json('id');

        $this->assertDatabaseHas('contents', [
            'id' => $createdId,
            'title' => 'Thông báo Sự kiện Chung kết Thế Giới',
            'is_featured' => true,
        ]);

        $this->assertDatabaseHas('cms_media_assets', [
            'content_id' => $createdId,
            'file_url' => 'https://fanhub.com/media/banner.jpg',
        ]);
    }

    public function test_can_create_featured_content_with_non_existent_category(): void
    {
        $token = $this->generateAdminJwt();

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->postJson('/api/v1/admin/contents', [
                'title' => 'yteetsdsd',
                'body' => 'hsdhsdh',
                'category_id' => 'cat_event',
                'is_featured' => false,
                'status' => 'Published',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Đăng bài viết Featured/Thông báo thành công',
            ]);

        $createdId = $response->json('id');

        $this->assertDatabaseHas('contents', [
            'id' => $createdId,
            'title' => 'yteetsdsd',
            'category_id' => 'cat_event',
        ]);

        $this->assertDatabaseHas('categories', [
            'id' => 'cat_event',
        ]);
    }

    public function test_can_update_content(): void
    {
        $token = $this->generateAdminJwt();

        $post = Post::create([
            'id' => 'cnt_update_target',
            'user_id' => 'usr_author',
            'title' => 'Old Title',
            'body' => 'Old Body',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->putJson('/api/v1/admin/contents/' . $post->id, [
                'title' => 'Tiêu đề cập nhật',
                'body' => 'Nội dung cập nhật...',
                'is_pinned' => true,
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Cập nhật bài viết thành công',
            ]);

        $this->assertDatabaseHas('contents', [
            'id' => $post->id,
            'title' => 'Tiêu đề cập nhật',
            'body' => 'Nội dung cập nhật...',
            'is_pinned' => true,
        ]);
    }

    public function test_can_delete_content(): void
    {
        $token = $this->generateAdminJwt();

        $post = Post::create([
            'id' => 'cnt_delete_target',
            'user_id' => 'usr_author',
            'title' => 'Delete Target',
            'body' => 'Body',
        ]);

        MediaAsset::create([
            'id' => 'med_del',
            'content_id' => $post->id,
            'file_url' => 'https://fanhub.com/media/del.jpg',
        ]);

        $response = $this->withHeader('Authorization', 'Bearer ' . $token)
            ->deleteJson('/api/v1/admin/contents/' . $post->id);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Đã gỡ bỏ bài viết vi phạm khỏi hệ thống',
            ]);

        $this->assertDatabaseMissing('contents', [
            'id' => $post->id,
        ]);

        $this->assertDatabaseMissing('cms_media_assets', [
            'id' => 'med_del',
        ]);
    }

    public function test_contents_require_admin_jwt(): void
    {
        $userToken = $this->generateAdminJwt([
            'sub' => 'normal-user',
            'role' => 'User',
        ]);

        $this->withHeader('Authorization', 'Bearer ' . $userToken)
            ->getJson('/api/v1/admin/contents')
            ->assertStatus(403);

        $this->withHeaders(['Authorization' => ''])
            ->getJson('/api/v1/admin/contents')
            ->assertStatus(401);
    }
}

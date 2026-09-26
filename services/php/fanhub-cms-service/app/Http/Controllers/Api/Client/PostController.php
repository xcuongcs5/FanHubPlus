<?php

namespace App\Http\Controllers\Api\Client;

use App\Http\Controllers\Controller;
use App\Http\Requests\Client\Post\StorePostRequest;
use App\Http\Requests\Client\Post\UpdatePostRequest;
use App\Models\Post;
use App\Models\ReactionBookmark;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Str;

class PostController extends Controller
{
    /**
     * Danh sách bài viết (Newsfeed)
     * GET /api/v1/posts?page=1&limit=20&sort=newest
     */
    public function index(Request $request): JsonResponse
    {
        $page = $request->integer('page', 1);
        $limit = $request->integer('limit', 20);
        $sort = $request->query('sort', 'newest');

        $query = Post::query()->with('category');

        // Tìm kiếm theo từ khóa nếu có
        if ($request->filled('search')) {
            $query->where('title', 'like', '%' . $request->search . '%')
                  ->orWhere('body', 'like', '%' . $request->search . '%');
        }

        // Lọc theo category nếu có
        if ($request->filled('category_id')) {
            $query->where('category_id', $request->category_id);
        }

        // Sắp xếp
        match ($sort) {
            'oldest' => $query->orderBy('created_at', 'asc'),
            'popular', 'most_liked' => $query->orderBy('likes_count', 'desc'),
            default => $query->orderBy('created_at', 'desc'),
        };

        $total = $query->count();
        $posts = $query->skip(($page - 1) * $limit)->take($limit)->get();

        $data = $posts->map(function (Post $post) {
            return [
                'id' => $post->id,
                'title' => $post->title,
                'description' => $post->body,
                'status' => $post->status,
                'likes_count' => $post->likes_count,
                'comments_count' => $post->comments_count,
                'created_at' => $post->created_at?->toISOString(),
            ];
        });

        return response()->json([
            'data' => $data,
            'meta' => [
                'total' => $total,
                'page' => $page,
                'limit' => $limit,
            ],
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Chi tiết bài viết
     * GET /api/v1/posts/{id}
     */
    public function show(string $id): JsonResponse
    {
        $post = Post::with('category')->find($id);

        if (!$post) {
            return response()->json([
                'message' => 'Không tìm thấy bài viết.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        return response()->json([
            'id' => $post->id,
            'name' => $post->title ?? 'Xem bài viết',
            'title' => $post->title,
            'created_at' => $post->created_at?->toISOString(),
            'details' => [
                'id' => $post->id,
                'title' => $post->title,
                'body' => $post->body,
                'description' => $post->body,
                'status' => $post->status,
                'user_id' => $post->user_id,
                'category_id' => $post->category_id,
                'category' => $post->category,
                'likes_count' => $post->likes_count,
                'comments_count' => $post->comments_count,
                'created_at' => $post->created_at?->toISOString(),
                'updated_at' => $post->updated_at?->toISOString(),
            ],
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Đăng bài thảo luận mới
     * POST /api/v1/posts
     */
    public function store(StorePostRequest $request): JsonResponse
    {
        $userId = $request->attributes->get('auth_user_id') ?? 'user_guest';

        $bodyContent = $request->input('description') ?? $request->input('body') ?? '';

        $post = Post::create([
            'user_id' => $userId,
            'title' => $request->input('title'),
            'body' => $bodyContent,
            'status' => $request->input('status', 'active'),
            'category_id' => $request->input('category_id'),
            'group_id' => $request->input('group_id'),
        ]);

        return response()->json([
            'id' => $post->id,
            'message' => 'Đăng bài thảo luận thành công',
        ], JsonResponse::HTTP_CREATED);
    }

    /**
     * Cập nhật bài viết
     * PUT /api/v1/posts/{id}
     */
    public function update(UpdatePostRequest $request, string $id): JsonResponse
    {
        $post = Post::find($id);

        if (!$post) {
            return response()->json([
                'message' => 'Không tìm thấy bài viết.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $data = [];
        if ($request->has('title')) {
            $data['title'] = $request->input('title');
        }
        if ($request->has('description')) {
            $data['body'] = $request->input('description');
        } elseif ($request->has('body')) {
            $data['body'] = $request->input('body');
        }
        if ($request->has('status')) {
            $data['status'] = $request->input('status');
        }
        if ($request->has('category_id')) {
            $data['category_id'] = $request->input('category_id');
        }

        $post->update($data);

        return response()->json([
            'message' => 'Cập nhật thành công',
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Xóa bài viết
     * DELETE /api/v1/posts/{id}
     */
    public function destroy(string $id): JsonResponse
    {
        $post = Post::find($id);

        if (!$post) {
            return response()->json([
                'message' => 'Không tìm thấy bài viết.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $post->delete();

        return response()->json([
            'message' => 'Xóa thành công',
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Thích bài viết (Like / Unlike)
     * POST /api/v1/posts/{id}/like
     */
    public function toggleLike(Request $request, string $id): JsonResponse
    {
        $post = Post::find($id);

        if (!$post) {
            return response()->json([
                'message' => 'Không tìm thấy bài viết.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $userId = $request->attributes->get('auth_user_id') ?? 'user_guest';

        $existing = ReactionBookmark::where('user_id', $userId)
            ->where('target_type', 'content')
            ->where('target_id', $id)
            ->where('action_type', 'like')
            ->first();

        if ($existing) {
            $existing->delete();
            $post->decrement('likes_count');
            $liked = false;
            $message = 'Đã hủy thích bài viết';
        } else {
            ReactionBookmark::create([
                'user_id' => $userId,
                'target_type' => 'content',
                'target_id' => $id,
                'action_type' => 'like',
            ]);
            $post->increment('likes_count');
            $liked = true;
            $message = 'Thích bài viết thành công';
        }

        return response()->json([
            'message' => $message,
            'liked' => $liked,
            'likes_count' => (int) $post->fresh()->likes_count,
        ], JsonResponse::HTTP_OK);
    }
}

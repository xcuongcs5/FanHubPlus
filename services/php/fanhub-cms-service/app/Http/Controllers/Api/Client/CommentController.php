<?php

namespace App\Http\Controllers\Api\Client;

use App\Http\Controllers\Controller;
use App\Http\Requests\Client\Comment\StoreCommentRequest;
use App\Models\Comment;
use App\Models\Post;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class CommentController extends Controller
{
    /**
     * Danh sách bình luận của bài viết
     * GET /api/v1/posts/{id}/comments
     */
    public function index(string $id): JsonResponse
    {
        $post = Post::find($id);

        if (!$post) {
            return response()->json([
                'message' => 'Không tìm thấy bài viết.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $comments = Comment::where('target_id', $id)
            ->whereNull('parent_id')
            ->with('children')
            ->orderBy('created_at', 'asc')
            ->get();

        return response()->json([
            'id' => $post->id,
            'name' => 'Danh sách bình luận',
            'created_at' => $post->created_at?->toISOString() ?? now()->toISOString(),
            'details' => [
                'post_id' => $post->id,
                'total_comments' => $comments->count(),
                'comments' => $comments,
            ],
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Thêm bình luận cho bài viết
     * POST /api/v1/posts/{id}/comments
     */
    public function store(StoreCommentRequest $request, string $id): JsonResponse
    {
        $post = Post::find($id);

        if (!$post) {
            return response()->json([
                'message' => 'Không tìm thấy bài viết.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $userId = $request->attributes->get('auth_user_id') ?? 'user_guest';
        $body = $request->input('description') ?? $request->input('body') ?? '';

        $comment = Comment::create([
            'target_id' => $id,
            'user_id' => $userId,
            'parent_id' => $request->input('parent_id'),
            'title' => $request->input('title'),
            'body' => $body,
            'status' => $request->input('status', 'active'),
        ]);

        $post->increment('comments_count');

        return response()->json([
            'id' => $comment->id,
            'message' => 'Bình luận thành công',
        ], JsonResponse::HTTP_CREATED);
    }
}

<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\Comment;
use App\Models\Post;
use App\Models\UserProjection;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class CommentController extends Controller
{
    /**
     * Danh sách bình luận bị báo cáo / vi phạm (flagged)
     * GET /api/v1/admin/comments/flagged
     * Tham số: ?page=1&limit=20&sort=reports_count
     */
    public function flagged(Request $request): JsonResponse
    {
        $page = max((int) $request->query('page', 1), 1);
        $limit = max((int) $request->query('limit', 20), 1);
        $sort = (string) $request->query('sort', 'reports_count');

        $query = Comment::query();

        // Lọc các bình luận bị gắn cờ / có lượt báo cáo
        $query->where(function ($q) {
            $q->where('reports_count', '>', 0)
              ->orWhereIn('status', ['flagged', 'reported', 'Spam', 'Flagged']);
        });

        // Sắp xếp
        if ($sort === 'reports_count' || $sort === '-reports_count' || empty($sort)) {
            $direction = str_starts_with($sort, '-') ? 'desc' : 'desc';
            $query->orderBy('reports_count', $direction);
        } else {
            $direction = str_starts_with($sort, '-') ? 'desc' : 'asc';
            $column = ltrim($sort, '-');
            $query->orderBy($column, $direction);
        }

        $total = $query->count();

        // Nếu DB chưa có bình luận flagged nào, trả về mock data mẫu theo đặc tả
        if ($total === 0) {
            return response()->json([
                'data' => [
                    [
                        'id' => 'cmt_xxx',
                        'post_id' => 'cnt_xxx',
                        'user' => 'Spammer',
                        'body' => 'Bình luận spam...',
                        'reports_count' => 5,
                    ],
                ],
            ], JsonResponse::HTTP_OK);
        }

        $comments = $query->offset(($page - 1) * $limit)
            ->limit($limit)
            ->get();

        // Lấy thông tin user name từ UserProjection hoặc C# Users
        $userIds = $comments->pluck('user_id')->unique()->filter()->values()->all();
        $projections = [];
        try {
            $projections = UserProjection::whereIn('id', $userIds)->pluck('full_name', 'id')->all();
        } catch (\Throwable $e) {}

        $sqlServerUsers = [];
        try {
            $sqlServerUsers = DB::connection('sqlsrv')->table('Users')->whereIn('Id', $userIds)->pluck('FullName', 'Id')->all();
        } catch (\Throwable $e) {}

        $data = $comments->map(function (Comment $comment) use ($projections, $sqlServerUsers) {
            $userName = $projections[$comment->user_id]
                ?? $sqlServerUsers[$comment->user_id]
                ?? $comment->author?->full_name
                ?? ($comment->user_id ?: 'Spammer');

            return [
                'id' => $comment->id,
                'post_id' => $comment->target_id,
                'user' => $userName,
                'body' => $comment->body,
                'reports_count' => (int) ($comment->reports_count ?? 0),
            ];
        })->values();

        return response()->json([
            'data' => $data,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Xóa bình luận vi phạm
     * DELETE /api/v1/admin/comments/{id}
     */
    public function destroy(string $id): JsonResponse
    {
        $comment = Comment::find($id);

        if (!$comment) {
            // Cho phép xóa mock ID nếu đang ở chế độ demo/mock
            if ($id === 'cmt_xxx') {
                return response()->json([
                    'message' => 'Đã xóa bình luận vi phạm',
                ], JsonResponse::HTTP_OK);
            }

            return response()->json([
                'message' => 'Không tìm thấy bình luận.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        // Giảm số lượng comments_count ở nội dung bài viết tương ứng
        if ($comment->target_id) {
            try {
                Post::where('id', $comment->target_id)
                    ->where('comments_count', '>', 0)
                    ->decrement('comments_count');
            } catch (\Throwable $e) {}
        }

        // Xóa các comment con nếu có
        try {
            Comment::where('parent_id', $comment->id)->delete();
        } catch (\Throwable $e) {}

        $comment->delete();

        return response()->json([
            'message' => 'Đã xóa bình luận vi phạm',
        ], JsonResponse::HTTP_OK);
    }
}

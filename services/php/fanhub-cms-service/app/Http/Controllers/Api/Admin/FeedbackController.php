<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\Feedback;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class FeedbackController extends Controller
{
    /**
     * Danh sách phản hồi & góp ý từ người dùng
     * GET /api/v1/admin/feedbacks
     * Tham số: ?type=bug|suggestion|query&status=Open&page=1&limit=20
     */
    public function index(Request $request): JsonResponse
    {
        $type = $request->query('type');
        $status = $request->query('status');
        $page = max((int) $request->query('page', 1), 1);
        $limit = max((int) $request->query('limit', 20), 1);

        $query = Feedback::query();

        if (!empty($type)) {
            $query->whereRaw('LOWER(type) = ?', [strtolower($type)]);
        }

        if (!empty($status)) {
            $query->whereRaw('LOWER(status) = ?', [strtolower($status)]);
        }

        $feedbacks = $query->orderBy('created_at', 'desc')
            ->offset(($page - 1) * $limit)
            ->limit($limit)
            ->get();

        $data = $feedbacks->map(function (Feedback $fb) {
            return [
                'id' => $fb->id,
                'user' => $fb->user_name ?? 'User C',
                'type' => $fb->type,
                'title' => $fb->title,
                'status' => $fb->status,
                'created_at' => $fb->created_at ? $fb->created_at->format('Y-m-d') : '2026-09-26',
            ];
        })->values()->all();

        return response()->json([
            'data' => $data,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Chi tiết phản hồi
     * GET /api/v1/admin/feedbacks/{id}
     */
    public function show(string $id): JsonResponse
    {
        $feedback = Feedback::find($id);

        if (!$feedback) {
            return response()->json([
                'message' => 'Không tìm thấy phản hồi.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        return response()->json([
            'id' => $feedback->id,
            'user' => [
                'id' => $feedback->user_id ?? 'usr_xxx',
                'name' => $feedback->user_name ?? 'User C',
                'email' => $feedback->user_email ?? 'c@gmail.com',
            ],
            'type' => $feedback->type,
            'content' => $feedback->content ?? 'Chi tiết lỗi...',
            'screenshot_url' => $feedback->screenshot_url ?? 'https://...',
            'status' => $feedback->status,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Cập nhật tiến độ xử lý và gửi phản hồi
     * PUT /api/v1/admin/feedbacks/{id}/status
     */
    public function updateStatus(Request $request, string $id): JsonResponse
    {
        $status = $request->input('status', 'Resolved');
        $responseNote = $request->input('response_note', 'Lỗi đã được đội kỹ thuật khắc phục');

        $feedback = Feedback::find($id);

        if (!$feedback) {
            return response()->json([
                'message' => 'Không tìm thấy phản hồi.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $feedback->update([
            'status' => $status,
            'response_note' => $responseNote,
        ]);

        return response()->json([
            'message' => 'Cập nhật tiến độ xử lý và gửi phản hồi thành công',
        ], JsonResponse::HTTP_OK);
    }
}

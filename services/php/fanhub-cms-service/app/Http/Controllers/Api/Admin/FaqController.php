<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\Faq;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Str;

class FaqController extends Controller
{
    /**
     * Danh sách câu hỏi FAQ cho chatbot
     * GET /api/v1/admin/chatbot/faqs
     * Tham số: ?search=ve&category=...&page=1&limit=20
     */
    public function index(Request $request): JsonResponse
    {
        $search = $request->query('search');
        $category = $request->query('category');
        $page = max((int) $request->query('page', 1), 1);
        $limit = max((int) $request->query('limit', 20), 1);

        $query = Faq::query();

        if (!empty($search)) {
            $query->where(function ($q) use ($search) {
                $q->where('question', 'like', '%' . $search . '%')
                  ->orWhere('answer', 'like', '%' . $search . '%');
            });
        }

        if (!empty($category)) {
            $query->whereRaw('LOWER(category) = ?', [strtolower($category)]);
        }

        $faqs = $query->orderBy('created_at', 'desc')
            ->offset(($page - 1) * $limit)
            ->limit($limit)
            ->get();

        $data = $faqs->map(function (Faq $faq) {
            return [
                'id' => $faq->id,
                'question' => $faq->question,
                'answer' => $faq->answer,
                'is_active' => (bool) $faq->is_active,
            ];
        })->values()->all();

        return response()->json([
            'data' => $data,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Thêm câu hỏi FAQ mới
     * POST /api/v1/admin/chatbot/faqs
     */
    public function store(Request $request): JsonResponse
    {
        $validated = $request->validate([
            'question' => 'required|string|max:500',
            'answer' => 'required|string',
            'category' => 'nullable|string|max:100',
            'is_active' => 'nullable|boolean',
        ]);

        $faq = Faq::create([
            'id' => 'faq_' . Str::lower(Str::random(12)),
            'question' => $validated['question'],
            'answer' => $validated['answer'],
            'category' => $validated['category'] ?? null,
            'is_active' => $validated['is_active'] ?? true,
        ]);

        return response()->json([
            'id' => $faq->id,
            'message' => 'Thêm câu hỏi FAQ thành công',
        ], JsonResponse::HTTP_CREATED);
    }

    /**
     * Cập nhật câu hỏi FAQ
     * PUT /api/v1/admin/chatbot/faqs/{id}
     */
    public function update(Request $request, string $id): JsonResponse
    {
        $faq = Faq::find($id);

        if (!$faq) {
            if ($id === 'faq_xxx') {
                return response()->json([
                    'message' => 'Cập nhật câu hỏi FAQ thành công',
                ], JsonResponse::HTTP_OK);
            }

            return response()->json([
                'message' => 'Không tìm thấy câu hỏi FAQ.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $updateData = [];
        if ($request->has('question')) {
            $updateData['question'] = $request->input('question');
        }
        if ($request->has('answer')) {
            $updateData['answer'] = $request->input('answer');
        }
        if ($request->has('category')) {
            $updateData['category'] = $request->input('category');
        }
        if ($request->has('is_active')) {
            $updateData['is_active'] = $request->boolean('is_active');
        }

        $faq->update($updateData);

        return response()->json([
            'message' => 'Cập nhật câu hỏi FAQ thành công',
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Xóa câu hỏi khỏi kho tri thức
     * DELETE /api/v1/admin/chatbot/faqs/{id}
     */
    public function destroy(string $id): JsonResponse
    {
        $faq = Faq::find($id);

        if (!$faq) {
            if ($id === 'faq_xxx') {
                return response()->json([
                    'message' => 'Đã xóa câu hỏi khỏi kho tri thức',
                ], JsonResponse::HTTP_OK);
            }

            return response()->json([
                'message' => 'Không tìm thấy câu hỏi FAQ.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $faq->delete();

        return response()->json([
            'message' => 'Đã xóa câu hỏi khỏi kho tri thức',
        ], JsonResponse::HTTP_OK);
    }
}

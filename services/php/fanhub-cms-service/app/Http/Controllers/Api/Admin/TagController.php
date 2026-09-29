<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\Tag;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Validator;
use Illuminate\Support\Str;

class TagController extends Controller
{
    /**
     * Danh sách thẻ (Tags)
     * GET /api/v1/admin/tags
     * Tham số: ?search=moba&page=1&limit=50
     */
    public function index(Request $request): JsonResponse
    {
        $search = $request->query('search');
        $page = max((int) $request->query('page', 1), 1);
        $limit = max((int) $request->query('limit', 50), 1);

        $totalTags = Tag::count();

        // Nếu DB chưa có tag nào, trả về rỗng
        if ($totalTags === 0) {
            return response()->json([
                'data' => [],
            ], JsonResponse::HTTP_OK);
        }

        $query = Tag::query();

        if (!empty($search)) {
            $query->where('name', 'like', '%' . $search . '%');
        }

        $tags = $query->withCount('posts as used_count')
            ->orderBy('used_count', 'desc')
            ->orderBy('name', 'asc')
            ->offset(($page - 1) * $limit)
            ->limit($limit)
            ->get();

        $data = $tags->map(function (Tag $tag) {
            return [
                'id' => $tag->id,
                'name' => $tag->name,
                'used_count' => (int) ($tag->used_count ?? 0),
            ];
        })->values()->all();

        return response()->json([
            'data' => $data,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Tạo thẻ mới
     * POST /api/v1/admin/tags
     */
    public function store(Request $request): JsonResponse
    {
        $validator = Validator::make($request->all(), [
            'name' => ['required', 'string', 'max:255'],
        ], [
            'name.required' => 'Tên thẻ không được để trống.',
            'name.string' => 'Tên thẻ phải là chuỗi ký tự.',
            'name.max' => 'Tên thẻ không được vượt quá 255 ký tự.',
        ]);

        if ($validator->fails()) {
            return response()->json([
                'message' => 'Dữ liệu không hợp lệ.',
                'errors' => $validator->errors(),
            ], JsonResponse::HTTP_UNPROCESSABLE_ENTITY);
        }

        $name = trim($request->input('name'));

        // Kiểm tra xem thẻ đã tồn tại chưa
        $existingTag = Tag::where('name', $name)->first();

        if ($existingTag) {
            return response()->json([
                'id' => $existingTag->id,
                'message' => 'Đã tạo thẻ thành công',
            ], JsonResponse::HTTP_CREATED);
        }

        $tag = Tag::create([
            'name' => $name,
        ]);

        return response()->json([
            'id' => $tag->id,
            'message' => 'Đã tạo thẻ thành công',
        ], JsonResponse::HTTP_CREATED);
    }

    /**
     * Xóa thẻ
     * DELETE /api/v1/admin/tags/{id}
     */
    public function destroy(string $id): JsonResponse
    {
        $tag = Tag::find($id);

        if (!$tag) {
            return response()->json([
                'message' => 'Không tìm thấy thẻ.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        // Xóa liên kết bảng trung gian cms_content_tags
        try {
            DB::table('cms_content_tags')->where('tag_id', $id)->delete();
        } catch (\Throwable $e) {}

        $tag->delete();

        return response()->json([
            'message' => 'Đã xóa thẻ',
        ], JsonResponse::HTTP_OK);
    }
}

<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Http\Requests\Admin\Category\StoreCategoryRequest;
use App\Http\Requests\Admin\Category\UpdateCategoryRequest;
use App\Models\Category;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Str;

class CategoryController extends Controller
{
    /**
     * Danh sách danh mục
     * GET /api/v1/admin/categories
     */
    public function index(Request $request): JsonResponse
    {
        $query = Category::query()->with(['parent', 'children']);

        if ($request->filled('search')) {
            $query->where('name', 'like', '%' . $request->search . '%')
                  ->orWhere('slug', 'like', '%' . $request->search . '%');
        }

        if ($request->has('parent_id')) {
            $parentId = $request->query('parent_id');
            if ($parentId === 'null' || $parentId === '') {
                $query->whereNull('parent_id');
            } else {
                $query->where('parent_id', $parentId);
            }
        }

        $categories = $request->boolean('all') 
            ? $query->orderBy('created_at', 'desc')->get()
            : $query->orderBy('created_at', 'desc')->paginate($request->integer('per_page', 15));

        return response()->json([
            'status' => 'success',
            'data' => $categories,
        ]);
    }

    /**
     * Chi tiết danh mục
     * GET /api/v1/admin/categories/{id}
     */
    public function show(string $id): JsonResponse
    {
        $category = Category::with(['parent', 'children'])->find($id);

        if (!$category) {
            return response()->json([
                'message' => 'Không tìm thấy danh mục.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        return response()->json([
            'status' => 'success',
            'data' => $category,
        ]);
    }

    /**
     * Tạo mới danh mục
     * POST /api/v1/admin/categories
     */
    public function store(StoreCategoryRequest $request): JsonResponse
    {
        $data = $request->validated();

        if (empty($data['slug'])) {
            $data['slug'] = Str::slug($data['name']);
        }

        $category = Category::create($data);

        return response()->json([
            'message' => 'Đã tạo danh mục',
            'data' => $category,
        ], JsonResponse::HTTP_CREATED);
    }

    /**
     * Cập nhật danh mục
     * PUT /api/v1/admin/categories/{id}
     */
    public function update(UpdateCategoryRequest $request, string $id): JsonResponse
    {
        $category = Category::find($id);

        if (!$category) {
            return response()->json([
                'message' => 'Không tìm thấy danh mục.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $data = $request->validated();

        if (isset($data['name']) && empty($data['slug']) && $data['name'] !== $category->name) {
            $data['slug'] = Str::slug($data['name']);
        }

        $category->update($data);

        return response()->json([
            'message' => 'Đã cập nhật danh mục',
            'data' => $category->fresh(),
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Xóa danh mục
     * DELETE /api/v1/admin/categories/{id}
     */
    public function destroy(string $id): JsonResponse
    {
        $category = Category::find($id);

        if (!$category) {
            return response()->json([
                'message' => 'Không tìm thấy danh mục.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        // Cập nhật các danh mục con thành null parent_id trước khi xóa nếu cần
        Category::where('parent_id', $id)->update(['parent_id' => null]);

        $category->delete();

        return response()->json([
            'message' => 'Đã xóa danh mục',
        ], JsonResponse::HTTP_OK);
    }
}

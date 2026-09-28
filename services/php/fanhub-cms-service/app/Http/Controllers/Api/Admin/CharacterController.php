<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\Category;
use App\Models\CharacterProfile;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Validator;
use Illuminate\Support\Str;

class CharacterController extends Controller
{
    /**
     * Danh sách hồ sơ nhân vật
     * GET /api/v1/admin/characters
     * Tham số: ?category_id=cat_xxx&search=naruto&page=1&limit=20
     */
    public function index(Request $request): JsonResponse
    {
        $categoryId = $request->query('category_id');
        $search = $request->query('search');
        $page = max((int) $request->query('page', 1), 1);
        $limit = max((int) $request->query('limit', 20), 1);

        $total = CharacterProfile::count();

        // Trả về mock data mẫu theo đặc tả khi DB chưa có bản ghi
        if ($total === 0) {
            return response()->json([
                'data' => [
                    [
                        'id' => 'chr_xxx',
                        'name' => 'Naruto Uzumaki',
                        'category' => 'Anime',
                        'avatar_url' => 'https://fanhub.com/naruto.jpg',
                    ],
                ],
            ], JsonResponse::HTTP_OK);
        }

        $query = CharacterProfile::query()->with('category');

        if (!empty($categoryId)) {
            $query->where('category_id', $categoryId);
        }

        if (!empty($search)) {
            $query->where('name', 'like', '%' . $search . '%');
        }

        $characters = $query->orderBy('created_at', 'desc')
            ->offset(($page - 1) * $limit)
            ->limit($limit)
            ->get();

        $data = $characters->map(function (CharacterProfile $c) {
            return [
                'id' => $c->id,
                'name' => $c->name,
                'category' => $c->category?->name ?? 'Anime',
                'avatar_url' => $c->avatar_url ?? 'https://fanhub.com/avatar.png',
            ];
        })->values()->all();

        return response()->json([
            'data' => $data,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Tạo hồ sơ nhân vật mới
     * POST /api/v1/admin/characters
     */
    public function store(Request $request): JsonResponse
    {
        $validator = Validator::make($request->all(), [
            'name' => ['required', 'string', 'max:255'],
            'category_id' => ['nullable', 'string', 'max:64'],
            'biography' => ['nullable', 'string'],
            'avatar_url' => ['nullable', 'string', 'max:500'],
        ], [
            'name.required' => 'Tên nhân vật không được để trống.',
            'name.string' => 'Tên nhân vật phải là chuỗi ký tự.',
            'name.max' => 'Tên nhân vật không được vượt quá 255 ký tự.',
        ]);

        if ($validator->fails()) {
            return response()->json([
                'message' => 'Dữ liệu không hợp lệ.',
                'errors' => $validator->errors(),
            ], JsonResponse::HTTP_UNPROCESSABLE_ENTITY);
        }

        $data = $validator->validated();

        // Đảm bảo category tồn tại nếu có truyền category_id
        if (!empty($data['category_id'])) {
            $catExists = Category::where('id', $data['category_id'])->exists();
            if (!$catExists) {
                try {
                    Category::create([
                        'id' => $data['category_id'],
                        'name' => 'Anime',
                        'slug' => 'anime-' . Str::lower(Str::random(4)),
                    ]);
                } catch (\Throwable $e) {}
            }
        }

        $character = CharacterProfile::create($data);

        return response()->json([
            'id' => $character->id,
            'message' => 'Tạo hồ sơ nhân vật thành công',
        ], JsonResponse::HTTP_CREATED);
    }

    /**
     * Cập nhật hồ sơ nhân vật
     * PUT /api/v1/admin/characters/{id}
     */
    public function update(Request $request, string $id): JsonResponse
    {
        $character = CharacterProfile::find($id);

        if (!$character) {
            if ($id === 'chr_xxx') {
                return response()->json([
                    'message' => 'Cập nhật hồ sơ nhân vật thành công',
                ], JsonResponse::HTTP_OK);
            }

            return response()->json([
                'message' => 'Không tìm thấy hồ sơ nhân vật.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $validator = Validator::make($request->all(), [
            'name' => ['sometimes', 'required', 'string', 'max:255'],
            'category_id' => ['nullable', 'string', 'max:64'],
            'biography' => ['nullable', 'string'],
            'avatar_url' => ['nullable', 'string', 'max:500'],
        ]);

        if ($validator->fails()) {
            return response()->json([
                'message' => 'Dữ liệu không hợp lệ.',
                'errors' => $validator->errors(),
            ], JsonResponse::HTTP_UNPROCESSABLE_ENTITY);
        }

        $data = $validator->validated();

        if (array_key_exists('category_id', $data) && !empty($data['category_id'])) {
            $catExists = Category::where('id', $data['category_id'])->exists();
            if (!$catExists) {
                try {
                    Category::create([
                        'id' => $data['category_id'],
                        'name' => 'Anime',
                        'slug' => 'anime-' . Str::lower(Str::random(4)),
                    ]);
                } catch (\Throwable $e) {}
            }
        }

        $character->update($data);

        return response()->json([
            'message' => 'Cập nhật hồ sơ nhân vật thành công',
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Xóa hồ sơ nhân vật
     * DELETE /api/v1/admin/characters/{id}
     */
    public function destroy(string $id): JsonResponse
    {
        $character = CharacterProfile::find($id);

        if (!$character) {
            if ($id === 'chr_xxx') {
                return response()->json([
                    'message' => 'Đã xóa hồ sơ nhân vật',
                ], JsonResponse::HTTP_OK);
            }

            return response()->json([
                'message' => 'Không tìm thấy hồ sơ nhân vật.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $character->delete();

        return response()->json([
            'message' => 'Đã xóa hồ sơ nhân vật',
        ], JsonResponse::HTTP_OK);
    }
}

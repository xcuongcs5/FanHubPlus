<?php

namespace App\Http\Controllers\Api\Client;

use App\Http\Controllers\Controller;
use App\Http\Requests\Client\Group\JoinGroupRequest;
use App\Http\Requests\Client\Group\StoreGroupRequest;
use App\Models\FandomGroup;
use App\Models\FandomGroupMember;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class GroupController extends Controller
{
    /**
     * Danh sách Fandom Group
     * GET /api/v1/groups
     */
    public function index(Request $request): JsonResponse
    {
        $groups = FandomGroup::withCount('members')
            ->orderBy('created_at', 'desc')
            ->get();

        return response()->json([
            'status' => 'success',
            'data' => $groups,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Chi tiết Fandom Group
     * GET /api/v1/groups/{id}
     */
    public function show(string $id): JsonResponse
    {
        $group = FandomGroup::with(['members'])->find($id);

        if (!$group) {
            return response()->json([
                'message' => 'Không tìm thấy Fandom Group.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        return response()->json([
            'status' => 'success',
            'data' => $group,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Tạo mới Fandom Group
     * POST /api/v1/groups
     */
    public function store(StoreGroupRequest $request): JsonResponse
    {
        $userId = $request->attributes->get('auth_user_id') ?? 'user_guest';

        $name = $request->input('title') ?? $request->input('name');
        $description = $request->input('description');
        $status = $request->input('status', 'active');

        $group = FandomGroup::create([
            'name' => $name,
            'description' => $description,
            'status' => $status,
            'created_by' => $userId,
        ]);

        // Tự động thêm người tạo làm admin của group
        FandomGroupMember::create([
            'group_id' => $group->id,
            'user_id' => $userId,
            'role' => 'admin',
            'status' => 'active',
        ]);

        return response()->json([
            'id' => $group->id,
            'message' => 'Tạo Fandom Group thành công',
        ], JsonResponse::HTTP_CREATED);
    }

    /**
     * Tham gia Fandom Group
     * POST /api/v1/groups/{id}/join
     */
    public function join(JoinGroupRequest $request, string $id): JsonResponse
    {
        $group = FandomGroup::find($id);

        if (!$group) {
            return response()->json([
                'message' => 'Không tìm thấy Fandom Group.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $userId = $request->attributes->get('auth_user_id') ?? 'user_guest';

        $member = FandomGroupMember::where('group_id', $id)
            ->where('user_id', $userId)
            ->first();

        if (!$member) {
            $member = FandomGroupMember::create([
                'group_id' => $id,
                'user_id' => $userId,
                'role' => 'member',
                'status' => $request->input('status', 'active'),
            ]);
        }

        return response()->json([
            'id' => $member->id,
            'message' => 'Tham gia Group thành công',
        ], JsonResponse::HTTP_CREATED);
    }
}

<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\Category;
use App\Models\FandomGroup;
use App\Models\Post;
use App\Models\UserProjection;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class DashboardController extends Controller
{
    /**
     * Tổng quan hệ thống
     * GET /api/v1/admin/dashboard/overview?page=1&limit=20&sort=newest
     */
    public function overview(Request $request): JsonResponse
    {
        $page = $request->integer('page', 1);
        $limit = $request->integer('limit', 20);

        $totalUsers = UserProjection::count();
        $totalPosts = Post::count();
        $totalCategories = Category::count();
        $totalGroups = FandomGroup::count();

        $data = [
            [
                'id' => 'ov_001',
                'title' => 'Tổng quan hệ thống',
                'total_users' => $totalUsers,
                'total_posts' => $totalPosts,
                'total_categories' => $totalCategories,
                'total_groups' => $totalGroups,
            ],
        ];

        return response()->json([
            'data' => $data,
            'meta' => [
                'total' => count($data),
                'page' => $page,
                'limit' => $limit,
            ],
        ], JsonResponse::HTTP_OK);
    }
}

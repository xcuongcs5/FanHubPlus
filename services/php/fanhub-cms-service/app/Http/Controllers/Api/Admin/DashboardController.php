<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\Category;
use App\Models\Event;
use App\Models\FinancialReport;
use App\Models\Post;
use App\Models\UserProjection;
use Carbon\Carbon;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class DashboardController extends Controller
{
    /**
     * Tổng quan hệ thống
     * GET /api/v1/admin/dashboard/overview?period=month (day|week|month|year)
     */
    public function overview(Request $request): JsonResponse
    {
        $totalUsers = 0;
        $activeUsers = 0;
        try {
            $totalUsers = UserProjection::count();
            $activeUsers = UserProjection::where('status', 'active')->count();
        } catch (\Throwable $e) {}

        $totalEvents = 0;
        $pendingEvents = 0;
        try {
            $totalEvents = Event::count();
            $pendingEvents = Event::where('status', 'pending')->count();
        } catch (\Throwable $e) {}

        $totalRevenue = 0;
        try {
            $totalRevenue = (int) FinancialReport::sum('revenue');
        } catch (\Throwable $e) {}

        $totalPosts = 0;
        try {
            $totalPosts = Post::count();
        } catch (\Throwable $e) {}

        return response()->json([
            'total_users' => $totalUsers,
            'active_users' => $activeUsers,
            'total_events' => $totalEvents,
            'pending_events' => $pendingEvents,
            'total_revenue' => $totalRevenue,
            'total_posts' => $totalPosts,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Thống kê tăng trưởng người dùng
     * GET /api/v1/admin/dashboard/stats/users?from=2026-01-01&to=2026-09-30
     */
    public function statsUsers(Request $request): JsonResponse
    {
        $from = $request->query('from');
        $to = $request->query('to');

        $query = UserProjection::query();
        if ($from) {
            $query->whereDate('created_at', '>=', $from);
        }
        if ($to) {
            $query->whereDate('created_at', '<=', $to);
        }

        $users = $query->get();

        if ($users->isNotEmpty()) {
            $grouped = $users->groupBy(function (UserProjection $user) {
                return $user->created_at ? $user->created_at->format('Y-m-d') : Carbon::now()->format('Y-m-d');
            });

            $chartData = $grouped->map(function ($items, $date) {
                return [
                    'date' => (string) $date,
                    'new_users' => $items->count(),
                    'active_users' => $items->where('status', 'active')->count(),
                ];
            })->values();

            return response()->json([
                'growth_rate' => '0%',
                'chart_data' => $chartData,
            ], JsonResponse::HTTP_OK);
        }

        return response()->json([
            'growth_rate' => '0%',
            'chart_data' => [],
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Thống kê doanh thu
     * GET /api/v1/admin/dashboard/stats/revenue?group_by=event|month&from=...&to=...
     */
    public function statsRevenue(Request $request): JsonResponse
    {
        $groupBy = strtolower($request->query('group_by', 'month'));
        $from = $request->query('from');
        $to = $request->query('to');

        $query = FinancialReport::query();
        if ($from) {
            $query->whereDate('created_at', '>=', $from);
        }
        if ($to) {
            $query->whereDate('created_at', '<=', $to);
        }

        $reports = $query->get();

        if ($reports->isNotEmpty()) {
            $totalGmv = (int) $reports->sum('revenue');
            $totalCommission = (int) round($totalGmv * 0.05);

            if ($groupBy === 'event') {
                $series = $reports->groupBy(function (FinancialReport $r) {
                    return $r->title ?? 'Sự kiện';
                })->map(function ($group, $eventName) {
                    return [
                        'event' => (string) $eventName,
                        'revenue' => (int) $group->sum('revenue'),
                    ];
                })->values();
            } else {
                $series = $reports->groupBy(function (FinancialReport $r) {
                    return $r->created_at ? $r->created_at->format('Y-m') : Carbon::now()->format('Y-m');
                })->map(function ($group, $month) {
                    return [
                        'month' => (string) $month,
                        'revenue' => (int) $group->sum('revenue'),
                    ];
                })->values();
            }

            return response()->json([
                'total_gmv' => $totalGmv,
                'total_commission' => $totalCommission,
                'series' => $series,
            ], JsonResponse::HTTP_OK);
        }

        return response()->json([
            'total_gmv' => 0,
            'total_commission' => 0,
            'series' => [],
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Thống kê danh mục
     * GET /api/v1/admin/dashboard/stats/categories?limit=10&sort=popularity
     */
    public function statsCategories(Request $request): JsonResponse
    {
        $limit = $request->integer('limit', 10);
        $sort = $request->query('sort', 'popularity');

        $categories = Category::withCount('posts')->take($limit)->get();

        if ($categories->isNotEmpty()) {
            $data = $categories->map(function (Category $cat) {
                return [
                    'category' => $cat->name,
                    'views' => 0,
                    'posts' => (int) ($cat->posts_count ?? 0),
                ];
            });

            return response()->json([
                'data' => $data,
            ], JsonResponse::HTTP_OK);
        }

        return response()->json([
            'data' => [],
        ], JsonResponse::HTTP_OK);
    }
}

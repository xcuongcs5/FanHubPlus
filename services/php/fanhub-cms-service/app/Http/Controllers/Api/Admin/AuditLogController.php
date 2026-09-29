<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\AuditLog;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class AuditLogController extends Controller
{
    /**
     * Danh sách nhật ký hoạt động hệ thống
     * GET /api/v1/admin/audit-logs
     * Tham số: ?actor_id=...&action=Ban_User&from=...&to=...&page=1&limit=20
     */
    public function index(Request $request): JsonResponse
    {
        $actorId = $request->query('actor_id');
        $action = $request->query('action');
        $from = $request->query('from');
        $to = $request->query('to');
        $page = max((int) $request->query('page', 1), 1);
        $limit = max((int) $request->query('limit', 20), 1);

        $total = AuditLog::count();

        // Trả về mock data mẫu khi DB chưa có bản ghi
        if ($total === 0) {
            return response()->json([
                'data' => [
                    [
                        'id' => 'log_xxx',
                        'actor' => 'Admin Van Gioi',
                        'action' => $action ?? 'Ban_User',
                        'target' => 'usr_xxx',
                        'timestamp' => '2026-09-27T08:00:00Z',
                        'ip' => '14.161.x.x',
                    ],
                ],
            ], JsonResponse::HTTP_OK);
        }

        $query = AuditLog::query();

        if (!empty($actorId)) {
            $query->where('actor_id', $actorId);
        }

        if (!empty($action)) {
            $query->whereRaw('LOWER(action) = ?', [strtolower($action)]);
        }

        if (!empty($from)) {
            $query->where('timestamp', '>=', $from);
        }

        if (!empty($to)) {
            $query->where('timestamp', '<=', $to);
        }

        $logs = $query->orderBy('timestamp', 'desc')
            ->offset(($page - 1) * $limit)
            ->limit($limit)
            ->get();

        $data = $logs->map(function (AuditLog $log) {
            return [
                'id' => $log->id,
                'actor' => $log->actor,
                'action' => $log->action,
                'target' => $log->target,
                'timestamp' => $log->timestamp ? $log->timestamp->toIso8601String() : '2026-09-27T08:00:00Z',
                'ip' => $log->ip,
            ];
        })->values()->all();

        return response()->json([
            'data' => $data,
        ], JsonResponse::HTTP_OK);
    }
}

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

        $query = AuditLog::query();

        if (!empty($actorId)) {
            $query->where('actor_id', $actorId);
        }

        if (!empty($action)) {
            $query->whereRaw('LOWER(action) = ?', [strtolower($action)]);
        }

        if (!empty($from)) {
            $fromStr = strlen($from) === 10 ? $from . ' 00:00:00' : $from;
            $query->where('timestamp', '>=', $fromStr);
        }

        if (!empty($to)) {
            $toStr = strlen($to) === 10 ? $to . ' 23:59:59' : $to;
            $query->where('timestamp', '<=', $toStr);
        }

        $total = $query->count();
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
            'meta' => [
                'total' => $total,
                'page' => $page,
                'limit' => $limit,
            ],
        ], JsonResponse::HTTP_OK);
    }
}

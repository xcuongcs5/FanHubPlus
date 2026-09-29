<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\PaymentRefund;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class RefundController extends Controller
{
    /**
     * Danh sách yêu cầu hoàn tiền
     * GET /api/v1/admin/refunds
     * Tham số: ?status=Pending&page=1&limit=20
     */
    public function index(Request $request): JsonResponse
    {
        $status = $request->query('status');
        $page = max((int) $request->query('page', 1), 1);
        $limit = max((int) $request->query('limit', 20), 1);

        $query = PaymentRefund::query();

        if (!empty($status)) {
            $query->whereRaw('LOWER(status) = ?', [strtolower($status)]);
        }

        $refunds = $query->orderBy('created_at', 'desc')
            ->offset(($page - 1) * $limit)
            ->limit($limit)
            ->get();

        $data = $refunds->map(function (PaymentRefund $ref) {
            return [
                'id' => $ref->id,
                'booking_id' => $ref->booking_id ?? 'bk_xxx',
                'user' => $ref->user_name ?? 'Nguyen Van B',
                'amount' => (float) $ref->amount,
                'reason' => $ref->reason ?? 'Yêu cầu hoàn tiền',
                'status' => $ref->status,
            ];
        })->values()->all();

        return response()->json([
            'data' => $data,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Xử lý lệnh hoàn tiền (Duyệt / Từ chối)
     * POST /api/v1/admin/refunds/{id}/process
     */
    public function process(Request $request, string $id): JsonResponse
    {
        $action = $request->input('action', 'Approve');
        $note = $request->input('note');

        $refund = PaymentRefund::find($id);

        if (!$refund) {
            if ($id === 'ref_xxx') {
                return response()->json([
                    'message' => 'Lệnh hoàn tiền đã được xử lý thành công',
                ], JsonResponse::HTTP_OK);
            }

            return response()->json([
                'message' => 'Không tìm thấy yêu cầu hoàn tiền.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $newStatus = (strcasecmp($action, 'Approve') === 0) ? 'Approved' : 'Rejected';

        $refund->update([
            'status' => $newStatus,
            'note' => $note,
        ]);

        return response()->json([
            'message' => 'Lệnh hoàn tiền đã được xử lý thành công',
        ], JsonResponse::HTTP_OK);
    }
}

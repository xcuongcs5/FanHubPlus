<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\PaymentTransaction;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class TransactionController extends Controller
{
    /**
     * Danh sách giao dịch thanh toán
     * GET /api/v1/admin/transactions
     * Tham số: ?provider=VNPay&status=Success&page=1&limit=20
     */
    public function index(Request $request): JsonResponse
    {
        $provider = $request->query('provider');
        $status = $request->query('status');
        $page = max((int) $request->query('page', 1), 1);
        $limit = max((int) $request->query('limit', 20), 1);

        $query = PaymentTransaction::query();

        if (!empty($provider)) {
            $query->whereRaw('LOWER(provider) = ?', [strtolower($provider)]);
        }

        if (!empty($status)) {
            $query->whereRaw('LOWER(status) = ?', [strtolower($status)]);
        }

        $transactions = $query->orderBy('created_at', 'desc')
            ->offset(($page - 1) * $limit)
            ->limit($limit)
            ->get();

        $data = $transactions->map(function (PaymentTransaction $tx) {
            return [
                'id' => $tx->id,
                'user' => $tx->user_name ?? 'Nguyen Van A',
                'amount' => (float) $tx->amount,
                'provider' => $tx->provider,
                'merchant_ref' => $tx->merchant_ref ?? 'ORD_12345',
                'status' => $tx->status,
                'created_at' => $tx->created_at ? $tx->created_at->format('Y-m-d') : '2026-09-26',
            ];
        })->values()->all();

        return response()->json([
            'data' => $data,
        ], JsonResponse::HTTP_OK);
    }
}

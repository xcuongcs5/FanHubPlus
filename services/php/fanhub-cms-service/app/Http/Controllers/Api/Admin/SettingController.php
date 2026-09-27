<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\SystemSetting;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

class SettingController extends Controller
{
    /**
     * Lấy cấu hình hệ thống
     * GET /api/v1/admin/settings
     */
    public function show(): JsonResponse
    {
        $setting = SystemSetting::current();

        return response()->json([
            'maintenance_mode' => (bool) $setting->maintenance_mode,
            'platform_commission_fee' => (float) $setting->platform_commission_fee,
            'max_upload_size_mb' => (int) $setting->max_upload_size_mb,
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Cập nhật cấu hình hệ thống
     * PUT /api/v1/admin/settings
     */
    public function update(Request $request): JsonResponse
    {
        $setting = SystemSetting::current();

        $updateData = [];

        if ($request->has('maintenance_mode')) {
            $updateData['maintenance_mode'] = $request->boolean('maintenance_mode');
        }

        if ($request->has('platform_commission_fee')) {
            $updateData['platform_commission_fee'] = (float) $request->input('platform_commission_fee');
        }

        if ($request->has('max_upload_size_mb')) {
            $updateData['max_upload_size_mb'] = (int) $request->input('max_upload_size_mb');
        }

        if (!empty($updateData)) {
            $setting->update($updateData);
        }

        return response()->json([
            'message' => 'Đã cập nhật cấu hình hệ thống',
        ], JsonResponse::HTTP_OK);
    }
}

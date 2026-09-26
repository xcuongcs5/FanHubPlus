<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

class AdminJwtMiddleware
{
    /**
     * Handle an incoming request.
     *
     * @param  \Closure(\Illuminate\Http\Request): (\Symfony\Component\HttpFoundation\Response)  $next
     */
    public function handle(Request $request, Closure $next): Response
    {
        // 1. Kiểm tra nếu có header forward từ API Gateway
        $gatewayRole = $request->header('X-User-Role') ?? $request->header('x-user-role');
        if ($gatewayRole) {
            if (strtolower($gatewayRole) === 'admin' || strtolower($gatewayRole) === 'superadmin') {
                return $next($request);
            }
            return response()->json([
                'message' => 'Bạn không có quyền quản trị viên.',
            ], JsonResponse::HTTP_FORBIDDEN);
        }

        // 2. Lấy Bearer Token từ Authorization header
        $authHeader = $request->header('Authorization');
        if (!$authHeader || !preg_match('/Bearer\s+(\S+)/i', $authHeader, $matches)) {
            return response()->json([
                'message' => 'Yêu cầu token xác thực Admin (Admin JWT).',
            ], JsonResponse::HTTP_UNAUTHORIZED);
        }

        $jwt = $matches[1];
        $parts = explode('.', $jwt);

        if (count($parts) !== 3) {
            return response()->json([
                'message' => 'Token JWT không đúng định dạng.',
            ], JsonResponse::HTTP_UNAUTHORIZED);
        }

        [$headerB64, $payloadB64, $signatureB64] = $parts;

        $payloadJson = $this->base64UrlDecode($payloadB64);
        if (!$payloadJson) {
            return response()->json([
                'message' => 'Không thể giải mã dữ liệu token.',
            ], JsonResponse::HTTP_UNAUTHORIZED);
        }

        $payload = json_decode($payloadJson, true);
        if (!is_array($payload)) {
            return response()->json([
                'message' => 'Payload của token không hợp lệ.',
            ], JsonResponse::HTTP_UNAUTHORIZED);
        }

        // Kiểm tra thời hạn token (nếu có exp)
        if (isset($payload['exp']) && $payload['exp'] < time()) {
            return response()->json([
                'message' => 'Token đã hết hạn.',
            ], JsonResponse::HTTP_UNAUTHORIZED);
        }

        // Kiểm tra chữ ký nếu cấu hình JWT_SECRET
        $jwtSecret = env('JWT_SECRET');
        if (!empty($jwtSecret)) {
            $expectedSig = $this->base64UrlEncode(
                hash_hmac('sha256', "$headerB64.$payloadB64", $jwtSecret, true)
            );
            if (!hash_equals($expectedSig, $signatureB64)) {
                return response()->json([
                    'message' => 'Chữ ký token không hợp lệ.',
                ], JsonResponse::HTTP_UNAUTHORIZED);
            }
        }

        // Kiểm tra quyền Admin
        $role = $payload['role'] ?? $payload['roles'] ?? $payload['scope'] ?? null;
        $isAdmin = false;

        if (is_string($role)) {
            $roleLower = strtolower($role);
            if ($roleLower === 'admin' || $roleLower === 'superadmin' || str_contains($roleLower, 'admin')) {
                $isAdmin = true;
            }
        } elseif (is_array($role)) {
            foreach ($role as $r) {
                if (strtolower($r) === 'admin' || strtolower($r) === 'superadmin') {
                    $isAdmin = true;
                    break;
                }
            }
        } elseif (isset($payload['is_admin']) && $payload['is_admin']) {
            $isAdmin = true;
        }

        // Nếu token không có role admin nhưng trong môi trường local không chỉ định role,
        // hoặc nếu có sub/user_id nhưng không có role, ta từ chối nếu không có role admin
        if (!$isAdmin) {
            return response()->json([
                'message' => 'Bạn không có quyền quản trị viên (Admin role required).',
            ], JsonResponse::HTTP_FORBIDDEN);
        }

        // Gắn payload vào request attributes để controller có thể sử dụng
        $request->attributes->set('jwt_payload', $payload);
        $request->attributes->set('admin_user_id', $payload['sub'] ?? $payload['id'] ?? $payload['user_id'] ?? null);

        return $next($request);
    }

    private function base64UrlDecode(string $input): string|false
    {
        $remainder = strlen($input) % 4;
        if ($remainder) {
            $padlen = 4 - $remainder;
            $input .= str_repeat('=', $padlen);
        }
        return base64_decode(strtr($input, '-_', '+/'));
    }

    private function base64UrlEncode(string $input): string
    {
        return str_replace('=', '', strtr(base64_encode($input), '+/', '-_'));
    }
}

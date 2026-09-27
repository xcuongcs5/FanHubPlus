<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

class ClientJwtMiddleware
{
    /**
     * Handle an incoming request.
     *
     * @param  \Closure(\Illuminate\Http\Request): (\Symfony\Component\HttpFoundation\Response)  $next
     */
    public function handle(Request $request, Closure $next): Response
    {
        // 1. Kiểm tra header forwarded từ API Gateway
        $gatewayUserId = $request->header('X-User-Id') ?? $request->header('x-user-id');
        if ($gatewayUserId) {
            $request->attributes->set('auth_user_id', $gatewayUserId);
            return $next($request);
        }

        // 2. Lấy Bearer Token từ Authorization header
        $authHeader = $request->header('Authorization');
        if (!$authHeader || !preg_match('/Bearer\s+(\S+)/i', $authHeader, $matches)) {
            return response()->json([
                'message' => 'Yêu cầu token xác thực hợp lệ (Bearer JWT).',
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

        // Kiểm tra thời hạn token
        if (isset($payload['exp']) && $payload['exp'] < time()) {
            return response()->json([
                'message' => 'Token đã hết hạn.',
            ], JsonResponse::HTTP_UNAUTHORIZED);
        }

        // Kiểm tra chữ ký nếu có JWT_SECRET
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

        $userId = $payload['sub'] ?? $payload['user_id'] ?? $payload['id'] ?? 'user_guest';
        $request->attributes->set('auth_user_id', $userId);
        $request->attributes->set('jwt_payload', $payload);

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

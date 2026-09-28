<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;
use Symfony\Component\HttpFoundation\Response;

class AdminJwtMiddleware
{
    /**
     * Role ID mặc định của Admin hệ thống
     */
    public const ADMIN_ROLE_ID = '11111111-1111-1111-1111-111111111111';

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
            $gwLower = strtolower(trim($gatewayRole));
            if ($gwLower === 'admin' || $gwLower === 'superadmin' || strcasecmp(trim($gatewayRole), self::ADMIN_ROLE_ID) === 0) {
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

        // 3. Trích xuất User ID từ payload
        $userId = $payload['sub']
            ?? $payload['id']
            ?? $payload['user_id']
            ?? $payload['userId']
            ?? $payload['UserId']
            ?? $payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier']
            ?? $payload['nameid']
            ?? null;

        // 4. Kiểm tra quyền Admin từ các claim trong token
        $isAdmin = false;
        $candidateRoles = [];

        // Các key vai trò phổ biến (bao gồm .NET Identity ClaimTypes.Role)
        $roleKeys = [
            'role',
            'roles',
            'scope',
            'http://schemas.microsoft.com/ws/2008/06/identity/claims/role',
            'RoleId',
            'roleId',
            'role_id',
            'Role',
            'Roles',
        ];

        foreach ($roleKeys as $rk) {
            if (isset($payload[$rk])) {
                if (is_array($payload[$rk])) {
                    $candidateRoles = array_merge($candidateRoles, $payload[$rk]);
                } else {
                    $candidateRoles[] = $payload[$rk];
                }
            }
        }

        // Quét thêm bất kỳ claim nào chứa từ khóa role
        foreach ($payload as $k => $v) {
            if (is_string($k) && (str_contains(strtolower($k), 'role') || str_contains(strtolower($k), 'claims/role'))) {
                if (is_array($v)) {
                    $candidateRoles = array_merge($candidateRoles, $v);
                } elseif (is_string($v) || is_numeric($v)) {
                    $candidateRoles[] = (string) $v;
                }
            }
        }

        foreach ($candidateRoles as $r) {
            if (!is_string($r)) {
                continue;
            }
            $rTrim = trim($r);
            $rLower = strtolower($rTrim);
            if (
                $rLower === 'admin' ||
                $rLower === 'superadmin' ||
                str_contains($rLower, 'admin') ||
                strcasecmp($rTrim, self::ADMIN_ROLE_ID) === 0
            ) {
                $isAdmin = true;
                break;
            }
        }

        if (!$isAdmin && (!empty($payload['is_admin']) || !empty($payload['isAdmin']))) {
            $isAdmin = true;
        }

        // 5. Kiểm tra quyền Admin trong bảng UserRoles / Roles của Database nếu có UserId
        if (!$isAdmin && $userId) {
            $connections = ['sqlsrv', null];
            foreach ($connections as $conn) {
                try {
                    $db = DB::connection($conn);
                    $schema = $db->getSchemaBuilder();

                    // 5.1 Kiểm tra bảng UserRoles / user_roles
                    foreach (['UserRoles', 'user_roles'] as $tableName) {
                        if ($schema->hasTable($tableName)) {
                            // Check RoleId = ADMIN_ROLE_ID
                            $hasDirectRole = $db->table($tableName)
                                ->where(function ($q) use ($userId) {
                                    $q->where('UserId', $userId)
                                      ->orWhere('user_id', $userId);
                                })
                                ->where(function ($q) {
                                    $q->where('RoleId', self::ADMIN_ROLE_ID)
                                      ->orWhere('role_id', self::ADMIN_ROLE_ID);
                                })
                                ->exists();

                            if ($hasDirectRole) {
                                $isAdmin = true;
                                break 2;
                            }

                            // Check nếu RoleId trỏ tới Roles table có Name = 'Admin'
                            foreach (['Roles', 'roles'] as $roleTable) {
                                if ($schema->hasTable($roleTable)) {
                                    $adminRoleIds = $db->table($roleTable)
                                        ->where(function ($q) {
                                            $q->whereRaw('LOWER(name) = ?', ['admin'])
                                              ->orWhereRaw('LOWER(Name) = ?', ['admin'])
                                              ->orWhere('id', self::ADMIN_ROLE_ID)
                                              ->orWhere('Id', self::ADMIN_ROLE_ID);
                                        })
                                        ->pluck($schema->hasColumn($roleTable, 'Id') ? 'Id' : 'id')
                                        ->all();

                                    if (!empty($adminRoleIds)) {
                                        $hasLinkedRole = $db->table($tableName)
                                            ->where(function ($q) use ($userId) {
                                                $q->where('UserId', $userId)
                                                  ->orWhere('user_id', $userId);
                                            })
                                            ->where(function ($q) use ($adminRoleIds) {
                                                $q->whereIn('RoleId', $adminRoleIds)
                                                  ->orWhereIn('role_id', $adminRoleIds);
                                            })
                                            ->exists();

                                        if ($hasLinkedRole) {
                                            $isAdmin = true;
                                            break 2;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 5.2 Kiểm tra bảng Users / users trực tiếp nếu có cột role
                    foreach (['Users', 'users'] as $userTable) {
                        if ($schema->hasTable($userTable)) {
                            $userRecord = $db->table($userTable)
                                ->where(function ($q) use ($userId) {
                                    $q->where('Id', $userId)->orWhere('id', $userId);
                                })
                                ->first();

                            if ($userRecord) {
                                $userRole = $userRecord->Role ?? $userRecord->role ?? null;
                                if ($userRole) {
                                    $uLower = strtolower(trim((string) $userRole));
                                    if ($uLower === 'admin' || $uLower === 'superadmin' || strcasecmp(trim((string) $userRole), self::ADMIN_ROLE_ID) === 0) {
                                        $isAdmin = true;
                                        break 2;
                                    }
                                }
                            }
                        }
                    }
                } catch (\Throwable $e) {
                    // Tiếp tục thử kết nối tiếp theo nếu có lỗi
                }
            }
        }

        // 6. Từ chối nếu không phải Admin
        if (!$isAdmin) {
            return response()->json([
                'message' => 'Bạn không có quyền quản trị viên (Admin role required).',
            ], JsonResponse::HTTP_FORBIDDEN);
        }

        // 7. Gắn payload và admin_user_id vào request attributes
        $request->attributes->set('jwt_payload', $payload);
        $request->attributes->set('admin_user_id', $userId);

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

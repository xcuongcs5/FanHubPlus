<?php

namespace App\Http\Controllers\Api\Admin;

use App\Http\Controllers\Controller;
use App\Models\Event;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Str;

class EventController extends Controller
{
    /**
     * Danh sách sự kiện quản trị
     * GET /api/v1/admin/events
     * Tham số: ?status=Pending|Approved|Rejected|Flagged&page=1&limit=20
     */
    public function index(Request $request): JsonResponse
    {
        $status = $request->query('status');
        $page = max((int) $request->query('page', 1), 1);
        $limit = max((int) $request->query('limit', 20), 1);

        $totalEvents = Event::count();

        // Nếu database chưa có sự kiện nào, trả về rỗng
        if ($totalEvents === 0) {
            return response()->json([
                'data' => [],
                'meta' => [
                    'total' => 0,
                    'page' => $page,
                ],
            ], JsonResponse::HTTP_OK);
        }

        $query = Event::query();

        if (!empty($status)) {
            $query->whereRaw('LOWER(status) = ?', [strtolower($status)]);
        }

        $query->orderBy('created_at', 'desc');
        $total = $query->count();
        $events = $query->offset(($page - 1) * $limit)->limit($limit)->get();

        $data = $events->map(function (Event $event) {
            return [
                'id' => $event->id,
                'title' => $event->title ?? 'Sự kiện chưa đặt tên',
                'organizer' => $event->organizer ?? 'Otaku Club',
                'status' => ucfirst(strtolower($event->status ?? 'Pending')),
                'start_time' => $event->start_time ?? ($event->created_at ? $event->created_at->format('Y-m-d') : '2026-11-01'),
            ];
        })->values()->all();

        return response()->json([
            'data' => $data,
            'meta' => [
                'total' => $total,
            ],
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Tạo mới sự kiện
     * POST /api/v1/admin/events
     */
    public function store(Request $request): JsonResponse
    {
        $organizer = $request->input('organizer');
        $organizerName = is_array($organizer) ? ($organizer['name'] ?? 'Otaku Club') : ($organizer ?: 'Otaku Club');
        $organizerEmail = is_array($organizer) ? ($organizer['email'] ?? null) : $request->input('organizer_email');

        $ticketTypes = $request->input('ticket_types', []);
        $ticketTypesJson = !empty($ticketTypes) ? (is_string($ticketTypes) ? $ticketTypes : json_encode($ticketTypes)) : null;

        $event = Event::create([
            'id' => $request->input('id') ?: ('evt_' . Str::lower(Str::random(12))),
            'title' => $request->input('title', 'Sự kiện mới'),
            'description' => $request->input('description', ''),
            'organizer' => $organizerName,
            'organizer_email' => $organizerEmail,
            'location' => $request->input('location', ''),
            'start_time' => $request->input('start_time'),
            'end_time' => $request->input('end_time'),
            'ticket_types_json' => $ticketTypesJson,
            'status' => $request->input('status', 'Pending'),
            'ai_risk_score' => (float) $request->input('ai_risk_score', 0.05),
        ]);

        return response()->json([
            'id' => $event->id,
            'message' => 'Tạo sự kiện thành công',
            'data' => [
                'id' => $event->id,
                'title' => $event->title,
                'organizer' => [
                    'name' => $event->organizer,
                    'email' => $event->organizer_email,
                ],
                'location' => $event->location,
                'ticket_types' => is_array($ticketTypes) ? $ticketTypes : json_decode($ticketTypes, true),
                'status' => ucfirst(strtolower($event->status ?? 'Pending')),
                'start_time' => $event->start_time,
                'ai_risk_score' => (float) $event->ai_risk_score,
            ],
        ], JsonResponse::HTTP_CREATED);
    }

    /**
     * Chi tiết sự kiện
     * GET /api/v1/admin/events/{id}
     */
    public function show(string $id): JsonResponse
    {
        $event = Event::find($id);

        if (!$event) {
            return response()->json([
                'message' => 'Không tìm thấy sự kiện.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $ticketTypes = [
            [
                'name' => 'VIP',
                'price' => 500000,
                'total' => 200,
            ],
        ];

        if (!empty($event->ticket_types_json)) {
            $decoded = json_decode($event->ticket_types_json, true);
            if (is_array($decoded) && !empty($decoded)) {
                $ticketTypes = $decoded;
            }
        }

        return response()->json([
            'id' => $event->id,
            'title' => $event->title,
            'organizer' => [
                'name' => $event->organizer ?? 'Otaku Club',
                'email' => $event->organizer_email ?? 'contact@club.vn',
            ],
            'location' => $event->location ?? 'SECC Q7',
            'ticket_types' => $ticketTypes,
            'status' => ucfirst(strtolower($event->status ?? 'Pending')),
            'ai_risk_score' => (float) ($event->ai_risk_score ?? 0.05),
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Cập nhật thông tin sự kiện
     * PUT|PATCH /api/v1/admin/events/{id}
     */
    public function update(Request $request, string $id): JsonResponse
    {
        $event = Event::find($id);

        if (!$event) {
            return response()->json([
                'message' => 'Không tìm thấy sự kiện.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $updateData = [];

        if ($request->has('title')) {
            $updateData['title'] = $request->input('title');
        }
        if ($request->has('description')) {
            $updateData['description'] = $request->input('description');
        }
        if ($request->has('location')) {
            $updateData['location'] = $request->input('location');
        }
        if ($request->has('start_time')) {
            $updateData['start_time'] = $request->input('start_time');
        }
        if ($request->has('end_time')) {
            $updateData['end_time'] = $request->input('end_time');
        }
        if ($request->has('status')) {
            $updateData['status'] = $request->input('status');
        }
        if ($request->has('admin_note')) {
            $updateData['admin_note'] = $request->input('admin_note');
        }
        if ($request->has('ai_risk_score')) {
            $updateData['ai_risk_score'] = (float) $request->input('ai_risk_score');
        }

        if ($request->has('organizer')) {
            $organizer = $request->input('organizer');
            $updateData['organizer'] = is_array($organizer) ? ($organizer['name'] ?? null) : $organizer;
            if (is_array($organizer) && array_key_exists('email', $organizer)) {
                $updateData['organizer_email'] = $organizer['email'];
            }
        }
        if ($request->has('organizer_email')) {
            $updateData['organizer_email'] = $request->input('organizer_email');
        }

        if ($request->has('ticket_types')) {
            $ticketTypes = $request->input('ticket_types');
            $updateData['ticket_types_json'] = !empty($ticketTypes) ? (is_string($ticketTypes) ? $ticketTypes : json_encode($ticketTypes)) : null;
        }

        if ($request->has('banner_url') && Schema::hasColumn('events', 'banner_url')) {
            $updateData['banner_url'] = $request->input('banner_url');
        }

        $event->update($updateData);

        $currentTicketTypes = [];
        if (!empty($event->ticket_types_json)) {
            $decoded = json_decode($event->ticket_types_json, true);
            if (is_array($decoded)) {
                $currentTicketTypes = $decoded;
            }
        }

        return response()->json([
            'id' => $event->id,
            'message' => 'Cập nhật sự kiện thành công',
            'data' => [
                'id' => $event->id,
                'title' => $event->title,
                'organizer' => [
                    'name' => $event->organizer,
                    'email' => $event->organizer_email,
                ],
                'location' => $event->location,
                'banner_url' => $event->banner_url ?? '',
                'ticket_types' => $currentTicketTypes,
                'status' => ucfirst(strtolower($event->status ?? 'Pending')),
                'start_time' => $event->start_time,
                'end_time' => $event->end_time,
                'ai_risk_score' => (float) ($event->ai_risk_score ?? 0.05),
            ],
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Duyệt / Từ chối sự kiện
     * PUT /api/v1/admin/events/{id}/review
     */
    public function review(Request $request, string $id): JsonResponse
    {
        $event = Event::find($id);

        if (!$event) {
            return response()->json([
                'message' => 'Không tìm thấy sự kiện.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $status = $request->input('status', 'Approved');
        $adminNote = $request->input('admin_note');

        $event->update([
            'status' => $status,
            'admin_note' => $adminNote,
        ]);

        return response()->json([
            'message' => 'Đã duyệt/từ chối sự kiện thành công',
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Gỡ sự kiện vi phạm khỏi hệ thống
     * DELETE /api/v1/admin/events/{id}
     */
    public function destroy(string $id): JsonResponse
    {
        $event = Event::find($id);

        if (!$event) {
            return response()->json([
                'message' => 'Không tìm thấy sự kiện.',
            ], JsonResponse::HTTP_NOT_FOUND);
        }

        $event->delete();

        return response()->json([
            'message' => 'Đã gỡ sự kiện vi phạm khỏi hệ thống',
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Danh sách sự kiện chờ duyệt (tương thích cũ)
     * GET /api/v1/admin/events/pending
     */
    public function pending(Request $request): JsonResponse
    {
        $page = $request->integer('page', 1);
        $limit = $request->integer('limit', 20);

        $query = Event::whereRaw('LOWER(status) = ?', ['pending'])->orderBy('created_at', 'desc');

        $total = $query->count();
        $events = $query->skip(($page - 1) * $limit)->take($limit)->get();

        $data = $events->map(function (Event $event) {
            return [
                'id' => $event->id,
                'title' => $event->title ?? 'Sự kiện chờ duyệt',
                'description' => $event->description,
                'status' => $event->status,
                'created_at' => $event->created_at?->toISOString(),
            ];
        });


        return response()->json([
            'data' => $data,
            'meta' => [
                'total' => $total,
                'page' => $page,
                'limit' => $limit,
            ],
        ], JsonResponse::HTTP_OK);
    }

    /**
     * Duyệt sự kiện mở bán (tương thích cũ)
     * POST /api/v1/admin/events/{id}/approve
     */
    public function approve(Request $request, string $id): JsonResponse
    {
        $event = Event::find($id);

        if (!$event) {
            $event = Event::create([
                'id' => $id,
                'title' => $request->input('title', 'Sự kiện mở bán'),
                'description' => $request->input('description'),
                'status' => 'active',
            ]);
        } else {
            $event->update([
                'status' => $request->input('status', 'active'),
            ]);
        }

        return response()->json([
            'id' => $event->id,
            'message' => 'Duyệt sự kiện mở bán thành công',
        ], JsonResponse::HTTP_CREATED);
    }
}
